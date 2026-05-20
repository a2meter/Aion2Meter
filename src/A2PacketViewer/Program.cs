// a2pktview — pcapng 패킷 뷰어.
// pcapng 파일(또는 세션 디렉토리)을 열어 프로토콜 파이프라인을 통과시키고,
// 파싱된 패킷을 순서대로 출력합니다.
//
// Usage:
//   a2pktview <file.pcapng>                     # 단일 파일
//   a2pktview <session-dir>                     # manifest.json 기반 세션
//   a2pktview <file.pcapng> --raw               # 프레임만 표시 (파싱 없음)
//   a2pktview <file.pcapng> --filter damage     # 특정 타입만 필터
//   a2pktview <file.pcapng> --hex               # hex dump 포함
//   a2pktview <file.pcapng> --limit 100         # 최대 출력 수 제한

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using A2Meter.Dps;
using A2Meter.Dps.Protocol;

// ── CLI parsing ──────────────────────────────────────────────────────────────

if (args.Length < 1)
{
    PrintUsage();
    return 1;
}

string target = args[0];
bool showRaw = args.Contains("--raw");
bool showHex = args.Contains("--hex");
string? filter = null;
int limit = int.MaxValue;

for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--filter" && i + 1 < args.Length) filter = args[++i].ToLowerInvariant();
    if (args[i] == "--limit" && i + 1 < args.Length) limit = int.Parse(args[++i]);
}

// ── Resolve input files ──────────────────────────────────────────────────────

List<string> pcapFiles;
if (Directory.Exists(target))
{
    var manifestPath = Path.Combine(target, "manifest.json");
    if (File.Exists(manifestPath))
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        pcapFiles = doc.RootElement.GetProperty("files").EnumerateArray()
                      .Select(f => Path.Combine(target, f.GetString()!)).ToList();
    }
    else
    {
        pcapFiles = Directory.EnumerateFiles(target, "*.pcap*").OrderBy(f => f).ToList();
    }
}
else if (File.Exists(target))
{
    pcapFiles = new List<string> { target };
}
else
{
    Console.Error.WriteLine($"[error] 파일 또는 디렉토리를 찾을 수 없습니다: {target}");
    return 1;
}

if (pcapFiles.Count == 0)
{
    Console.Error.WriteLine("[error] pcapng 파일이 없습니다.");
    return 1;
}

Console.Error.WriteLine($"[a2pktview] 입력: {(pcapFiles.Count == 1 ? pcapFiles[0] : $"{target} ({pcapFiles.Count}개 파일)")}");

// ── Pass 1: detect server port via magic-payload heuristic ───────────────────

var magicByPort = new Dictionary<int, int>();
foreach (var path in pcapFiles)
{
    if (!File.Exists(path)) continue;
    using var dev = new CaptureFileReaderDevice(path);
    dev.Open();
    while (dev.GetNextPacket(out var pc) == GetPacketStatus.PacketRead)
    {
        try
        {
            var pkt = pc.GetPacket().GetPacket();
            var tcp = pkt.Extract<TcpPacket>();
            if (tcp is null) continue;
            var payload = tcp.PayloadData;
            if (payload is null || payload.Length < 4) continue;
            if (ProtocolUtils.LooksLikeGameMagicPayload(payload))
                magicByPort[tcp.SourcePort] = magicByPort.GetValueOrDefault(tcp.SourcePort) + 1;
        }
        catch { }
    }
}

int serverPort = magicByPort.Count > 0
    ? magicByPort.OrderByDescending(kv => kv.Value).First().Key
    : 13328;
Console.Error.WriteLine($"[a2pktview] 서버 포트: {serverPort}");

// ── Pass 2: replay through protocol pipeline ─────────────────────────────────

int msgIndex = 0;
int outputCount = 0;
var skillDb = SkillDatabase.Shared;
var dispatcher = new PacketDispatcher(skillDb);
var partyParser = new PartyStreamParser();

// Event handlers — print each parsed event.
dispatcher.Damage += (actorId, targetId, skillCode, dmgType, damage, flags, multi, multiDmg, heal, isDot) =>
{
    if (!PassFilter("damage")) return;
    string skillName = skillDb.GetSkillName(skillCode) ?? $"#{skillCode}";
    string prefix = isDot != 0 ? "DOT" : "DMG";
    string extra = "";
    if (multi > 0) extra += $" multi={multi}(+{multiDmg})";
    if (heal > 0) extra += $" heal={heal}";
    if ((flags & 0x100) != 0) extra += " [크리]";
    Print($"  [{prefix}] {actorId}→{targetId} 스킬={skillName} 피해={damage}{extra}");
};

dispatcher.MobSpawn += (mobId, mobCode, hp, isBoss) =>
{
    if (!PassFilter("mob")) return;
    string boss = isBoss != 0 ? " [보스]" : "";
    string hpStr = hp > 0 ? $" HP={hp:N0}" : "";
    Print($"  [MOB] entityId={mobId} mobCode={mobCode}{hpStr}{boss}");
};

dispatcher.Summon += (actorId, petId) =>
{
    if (!PassFilter("summon")) return;
    Print($"  [SUMMON] actor={actorId} pet={petId}");
};

dispatcher.UserInfo += (entityId, nick, serverId, jobCode, isSelf) =>
{
    if (!PassFilter("user")) return;
    string self = isSelf != 0 ? " [자신]" : "";
    string job = JobMapping.GameToName.GetValueOrDefault(jobCode, $"직업{jobCode}");
    Print($"  [USER] entityId={entityId} 닉네임={nick} 서버={serverId} 직업={job}{self}");
};

dispatcher.CombatPower += (entityId, cp) =>
{
    if (!PassFilter("cp")) return;
    Print($"  [CP] entityId={entityId} 전투력={cp:N0}");
};

dispatcher.CombatPowerByName += (nick, serverId, cp) =>
{
    if (!PassFilter("cp")) return;
    Print($"  [CP] 닉네임={nick} 서버={serverId} 전투력={cp:N0}");
};

dispatcher.EntityRemoved += entityId =>
{
    if (!PassFilter("entity")) return;
    Print($"  [REMOVE] entityId={entityId}");
};

dispatcher.BossHp += (entityId, hp) =>
{
    if (!PassFilter("bosshp")) return;
    Print($"  [BOSSHP] entityId={entityId} HP={hp:N0}");
};

dispatcher.Buff += (entityId, buffId, type, durationMs, timestamp, casterId) =>
{
    if (!PassFilter("buff")) return;
    string name = skillDb.GetSkillName(buffId) ?? $"#{buffId}";
    double sec = durationMs / 1000.0;
    Print($"  [BUFF] entityId={entityId} 버프={name} 타입={type} 지속={sec:F1}s caster={casterId}");
};

dispatcher.CharacterLookup += (entityId, nick, serverId, jobCode, level, cp) =>
{
    if (!PassFilter("lookup")) return;
    string job = JobMapping.GameToName.GetValueOrDefault(jobCode, $"직업{jobCode}");
    Print($"  [LOOKUP] entityId={entityId} 닉네임={nick} 서버={serverId} 직업={job} Lv{level} CP={cp:N0}");
};

dispatcher.CharacterEquipment += (entityId, items) =>
{
    if (!PassFilter("equip")) return;
    Print($"  [EQUIP] entityId={entityId} 장비 {items.Count}개:");
    foreach (var item in items)
    {
        string gem = item.GemSlotCount > 0 ? $" 마석={item.GemSlotCount}" : "";
        string stats = "";
        if (item.SubStats is { Count: > 0 })
        {
            var parts = item.SubStats.Select(s =>
            {
                string name = StatMapping.GetName(s.StatId) ?? $"#{s.StatId}";
                return s.Value > 500 ? $"{name}={s.Value / 100.0:F1}%" : $"{name}={s.Value}";
            });
            stats = $" [{string.Join(", ", parts)}]";
        }
        Print($"           itemId={item.ItemId} +{item.EnchantLevel}{gem}{stats}");
    }
};

partyParser.PartyList += members =>
{
    if (!PassFilter("party")) return;
    Print($"  [PARTY LIST] {members.Count}명: {string.Join(", ", members.Select(m => $"{m.Nickname}({m.JobName})"))}");
};

partyParser.PartyUpdate += members =>
{
    if (!PassFilter("party")) return;
    Print($"  [PARTY UPD] {members.Count}명: {string.Join(", ", members.Select(m => $"{m.Nickname}({m.JobName})"))}");
};

partyParser.PartyLeft += () =>
{
    if (!PassFilter("party")) return;
    Print("  [PARTY LEFT] 파티 해산/퇴장");
};

partyParser.DungeonDetected += (id, stage) =>
{
    if (!PassFilter("dungeon")) return;
    string name = skillDb.GetDungeonName(id) ?? $"#{id}";
    Print($"  [DUNGEON] {name} (id={id} stage={stage})");
};

partyParser.CombatPowerDetected += (nick, serverId, cp) =>
{
    if (!PassFilter("cp")) return;
    Print($"  [PARTY CP] {nick} 서버={serverId} 전투력={cp:N0}");
};

// Stream processor dispatches framed messages.
void OnMessage(byte[] data, int offset, int length)
{
    msgIndex++;
    if (outputCount >= limit) return;

    if (showRaw || showHex)
    {
        string tag = IdentifyTag(data, offset, length);
        Print($"#{msgIndex,5} [{tag,-14}] len={length}");
        if (showHex)
            PrintHexDump(data, offset, Math.Min(length, 128));
    }

    // Dispatch through both parsers.
    dispatcher.Dispatch(data, offset, length);
    partyParser.Feed(new ReadOnlySpan<byte>(data, offset, length));
}

var stream = new StreamProcessor(OnMessage, _ => { });
var flows = new Dictionary<(IPAddress, int, IPAddress, int), TcpReassembler>();

Console.Error.WriteLine($"[a2pktview] 파싱 시작...");
Console.Error.WriteLine();

// Header
if (showRaw)
    Console.WriteLine($"{"#",5} {"태그",-14} {"길이",6}");
else
    Console.WriteLine($"--- 패킷 순서 출력 (필터: {filter ?? "없음"}) ---");
Console.WriteLine();

foreach (var path in pcapFiles)
{
    if (!File.Exists(path)) continue;
    if (outputCount >= limit) break;

    using var dev = new CaptureFileReaderDevice(path);
    dev.Open();

    while (dev.GetNextPacket(out var pc) == GetPacketStatus.PacketRead)
    {
        if (outputCount >= limit) break;
        try
        {
            var rc = pc.GetPacket();
            var pkt = rc.GetPacket();
            var ip = pkt.Extract<IPPacket>();
            var tcp = pkt.Extract<TcpPacket>();
            if (ip is null || tcp is null) continue;
            var payload = tcp.PayloadData;
            if (payload is null || payload.Length == 0) continue;
            if (tcp.SourcePort != serverPort) continue;

            var key = (ip.SourceAddress, tcp.SourcePort, ip.DestinationAddress, tcp.DestinationPort);
            if (!flows.TryGetValue(key, out var rasm))
            {
                rasm = new TcpReassembler(b => stream.ProcessData(b));
                flows[key] = rasm;
            }
            rasm.Feed(tcp.SequenceNumber, payload);
        }
        catch { }
    }
}

Console.Error.WriteLine();
Console.Error.WriteLine($"[a2pktview] 완료: 총 {msgIndex}개 메시지 파싱됨, {outputCount}개 출력됨");
return 0;

// ── Helpers ──────────────────────────────────────────────────────────────────

bool PassFilter(string type)
{
    if (outputCount >= limit) return false;
    if (filter is null) return true;
    return type.Contains(filter);
}

void Print(string line)
{
    if (outputCount >= limit) return;
    Console.WriteLine(line);
    outputCount++;
}

void PrintHexDump(byte[] data, int offset, int length)
{
    int end = offset + length;
    for (int row = offset; row < end; row += 16)
    {
        Console.Write($"    {row - offset:X4}: ");
        for (int col = 0; col < 16; col++)
        {
            int i = row + col;
            Console.Write(i < end ? $"{data[i]:X2} " : "   ");
            if (col == 7) Console.Write(" ");
        }
        Console.Write(" |");
        for (int col = 0; col < 16; col++)
        {
            int i = row + col;
            if (i < end)
            {
                byte b = data[i];
                Console.Write(b >= 0x20 && b < 0x7F ? (char)b : '.');
            }
        }
        Console.WriteLine("|");
    }
}

static string IdentifyTag(byte[] data, int offset, int length)
{
    if (length < 3) return "TOO_SHORT";

    // Skip leading varint.
    int p = offset;
    int end = offset + length;
    while (p < end && (data[p] & 0x80) != 0) p++;
    if (p < end) p++; // consume final byte of varint

    if (p + 1 >= end) return "NO_TAG";

    byte t1 = data[p], t2 = data[p + 1];

    return (t1, t2) switch
    {
        (4, 56)   => "DAMAGE",
        (5, 56)   => "DOT/HEAL",
        (42, 56)  => "BUFF",
        (43, 56)  => "BUFF_ALT",
        (51, 54)  => "SELF_INFO",
        (68, 54)  => "OTHER_INFO",
        (64, 54)  => "MOB_SPAWN",
        (3, 54)   => "GUARD",
        (33, 141) => "ENTITY_RM",
        (79, 54)  => "CHAR_LOOKUP",
        _ when t2 == 151 => $"PARTY_{t1:X2}",
        _ => $"UNK_{t1:X2}_{t2:X2}",
    };
}

static void PrintUsage()
{
    Console.WriteLine("a2pktview — A2 pcapng 패킷 뷰어");
    Console.WriteLine();
    Console.WriteLine("사용법:");
    Console.WriteLine("  a2pktview <file.pcapng>                  단일 pcapng 파일 분석");
    Console.WriteLine("  a2pktview <session-dir>                  세션 디렉토리 (manifest.json)");
    Console.WriteLine();
    Console.WriteLine("옵션:");
    Console.WriteLine("  --raw         프레임 단위 출력 (태그 + 길이)");
    Console.WriteLine("  --hex         hex dump 포함");
    Console.WriteLine("  --filter <t>  특정 타입만 출력");
    Console.WriteLine("                (damage, mob, user, cp, buff, party, dungeon, bosshp, entity, lookup, equip, summon)");
    Console.WriteLine("  --limit <N>   최대 출력 개수 제한");
    Console.WriteLine();
    Console.WriteLine("예시:");
    Console.WriteLine("  a2pktview capture.pcapng --raw --hex --limit 50");
    Console.WriteLine("  a2pktview ./session/ --filter damage");
    Console.WriteLine("  a2pktview capture.pcapng --filter party");
}
