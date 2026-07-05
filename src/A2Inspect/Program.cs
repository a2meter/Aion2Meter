// a2inspect — diagnose an a2cap session against the ported protocol stack.
//
// Walks every pcap file in the session, prints:
//   1. Top TCP (src→dst) flows by packet count
//   2. Magic-payload heuristic match counts per server-port candidate
//   3. After picking the best candidate, runs StreamProcessor + PacketDispatcher
//      and prints how many Damage / UserInfo / MobSpawn / BossHp / Buff /
//      CombatPower events fired, plus a sample of each.
//
// Usage:  a2inspect <session-dir>

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
using A2Meter.Dps.Protocol;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: a2inspect <session-dir>");
    return 1;
}

var sessionDir = args[0];
var manifestPath = Path.Combine(sessionDir, "manifest.json");
List<string> files;
if (File.Exists(manifestPath))
{
    using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
    files = doc.RootElement.GetProperty("files").EnumerateArray()
              .Select(f => Path.Combine(sessionDir, f.GetString()!)).ToList();
}
else
{
    files = Directory.EnumerateFiles(sessionDir, "*.pcap*").ToList();
}
Console.WriteLine($"[a2inspect] session={sessionDir}");
Console.WriteLine($"[a2inspect] {files.Count} file(s): {string.Join(", ", files.Select(Path.GetFileName))}");
Console.WriteLine();

// ── pass 1: port histogram + magic-payload counts ──────────────────────
var portPairs   = new Dictionary<(int Src, int Dst), (int Packets, long Bytes)>();
var magicByPort = new Dictionary<int, int>();
long totalPackets = 0, totalTcp = 0;

foreach (var path in files) WalkPcap(path, OnSegment1);
void OnSegment1(DateTime ts, IPAddress src, int sp, IPAddress dst, int dp, uint seq, byte[] payload)
{
    totalTcp++;
    var key = (sp, dp);
    portPairs[key] = portPairs.TryGetValue(key, out var v)
        ? (v.Packets + 1, v.Bytes + payload.Length)
        : (1, payload.Length);

    if (payload.Length > 3 && ProtocolUtils.LooksLikeGameMagicPayload(payload))
    {
        // The "server port" in a server→client segment is srcPort.
        magicByPort[sp] = magicByPort.GetValueOrDefault(sp) + 1;
    }
}

Console.WriteLine($"[a2inspect] pass 1 done: tcp={totalTcp} pcapPkts={totalPackets}");
Console.WriteLine();
Console.WriteLine("Top 10 TCP flows by packet count:");
Console.WriteLine($"  {"src",6} -> {"dst",6}  {"packets",8}  {"bytes",10}");
foreach (var kv in portPairs.OrderByDescending(kv => kv.Value.Packets).Take(10))
    Console.WriteLine($"  {kv.Key.Src,6} -> {kv.Key.Dst,6}  {kv.Value.Packets,8}  {kv.Value.Bytes,10}");

Console.WriteLine();
Console.WriteLine("Magic-payload (likely server) ports:");
if (magicByPort.Count == 0)
{
    Console.WriteLine("  (none — the capture does not contain the framed game protocol)");
}
else
{
    foreach (var kv in magicByPort.OrderByDescending(kv => kv.Value))
        Console.WriteLine($"  port {kv.Key,6}: {kv.Value} payloads matched");
}
Console.WriteLine();

int serverPort = magicByPort.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
if (serverPort == 0 && portPairs.ContainsKey((13328, 0))) serverPort = 13328;
if (serverPort == 0)
{
    // Look for *any* segment with src or dst = 13328 (for sessions where the
    // heuristic missed). Otherwise we have nothing to parse.
    var any13328 = portPairs.Keys.Any(k => k.Src == 13328 || k.Dst == 13328);
    if (any13328) serverPort = 13328;
}

if (serverPort == 0)
{
    Console.WriteLine("[a2inspect] no server port detected — stopping before parser pass.");
    return 0;
}

Console.WriteLine($"[a2inspect] using server port {serverPort} for parser pass");
Console.WriteLine();

// ── pass 2: feed the chosen flow through StreamProcessor + PacketDispatcher ──
var skills     = SkillDatabase.Shared;
var dispatcher = new PacketDispatcher(skills, msg => Console.Error.WriteLine(msg));
var party      = new PartyStreamParser();
var dumpName   = Environment.GetEnvironmentVariable("A2INSPECT_DUMP_NAME");
var dumpNeedle = string.IsNullOrWhiteSpace(dumpName) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(dumpName);
int dumpNameFrames = 0;
int nPartyList = 0, nPartyUpdate = 0, nPartyAccept = 0, nPartyRequest = 0, nPartyLeft = 0, nPartyEjected = 0, nDungeon = 0, nPartyCp = 0;
var samplePartyList = new List<string>();
party.PartyList    += list => { nPartyList++;   if (samplePartyList.Count < 3) samplePartyList.Add($"list[{list.Count}] " + string.Join(", ", list.Select(m => $"{m.Nickname}({m.JobName} Lv{m.Level} CP{m.CombatPower}/{m.ServerName})"))); };
party.PartyUpdate  += list => { nPartyUpdate++; if (samplePartyList.Count < 3) samplePartyList.Add($"update[{list.Count}] " + string.Join(", ", list.Select(m => $"{m.Nickname}({m.JobName})"))); };
party.PartyAccept  += m    => nPartyAccept++;
party.PartyRequest += m    => nPartyRequest++;
party.PartyLeft    += ()   => nPartyLeft++;
party.PartyEjected += ()   => nPartyEjected++;
party.DungeonDetected     += (id, st) => nDungeon++;
party.CombatPowerDetected += (n, sid, cp) => nPartyCp++;
int nDamage = 0, nUserInfo = 0, nMobSpawn = 0, nBossHp = 0, nBuff = 0, nCp = 0, nCpName = 0, nEntityRem = 0;
var sampleDamage = new List<string>();
var sampleUser   = new List<string>();
var sampleMob    = new List<string>();
var sampleBossHp = new List<string>();

dispatcher.EnableUnparsedDump(false);
dispatcher.Damage += (a, t, sk, dt, dmg, fl, mhc, mhd, heal, dot) =>
{
    nDamage++;
    if (sampleDamage.Count < 5)
    {
        var name = skills.GetSkillName(sk) ?? "?";
        sampleDamage.Add($"actor={a} target={t} skill={sk}({name}) dmg={dmg} heal={heal} multi={mhc}/{mhd} crit={(fl & 0x80) != 0}");
    }
};
dispatcher.UserInfo += (e, n, sid, jc, self) =>
{
    nUserInfo++;
    if (sampleUser.Count < 5) sampleUser.Add($"entity={e} nick={n} server={sid} job={jc} self={self}");
};
dispatcher.MobSpawn += (mid, mc, hp, isBoss) =>
{
    nMobSpawn++;
    if (sampleMob.Count < 5) sampleMob.Add($"mobId={mid} code={mc} hp={hp} boss={isBoss}");
};
dispatcher.BossHp        += (e, hp) =>
{
    nBossHp++;
    if (sampleBossHp.Count < 12) sampleBossHp.Add($"entity={e} hp={hp}");
};
dispatcher.Buff          += (e, b, t, d, ts, c) => nBuff++;
dispatcher.CombatPower   += (e, cp) => nCp++;
dispatcher.CombatPowerByName += (n, sid, cp) => nCpName++;
dispatcher.EntityRemoved += e => nEntityRem++;

var flows = new Dictionary<(IPAddress, int, IPAddress, int), TcpReassembler>();
StreamProcessor stream = new StreamProcessor(
    (data, off, len) =>
    {
        DumpNamedFrame(data, off, len, dumpNeedle, ref dumpNameFrames);
        dispatcher.Dispatch(data, off, len);
        party.Feed(new ReadOnlySpan<byte>(data, off, len));
    },
    msg => Console.Error.WriteLine("[stream] " + msg));

foreach (var path in files) WalkPcap(path, OnSegment2);
void OnSegment2(DateTime ts, IPAddress src, int sp, IPAddress dst, int dp, uint seq, byte[] payload)
{
    if (sp != serverPort) return;   // only server→client carries combat data
    var key = (src, sp, dst, dp);
    if (!flows.TryGetValue(key, out var rasm))
    {
        rasm = new TcpReassembler(b => stream.ProcessData(b));
        flows[key] = rasm;
    }
    rasm.Feed(seq, payload);
}

Console.WriteLine("Parser results:");
Console.WriteLine($"  Damage      : {nDamage}");
Console.WriteLine($"  UserInfo    : {nUserInfo}");
Console.WriteLine($"  MobSpawn    : {nMobSpawn}");
Console.WriteLine($"  BossHp      : {nBossHp}");
Console.WriteLine($"  Buff        : {nBuff}");
Console.WriteLine($"  CombatPower : {nCp} (by entity), {nCpName} (by name)");
Console.WriteLine($"  EntityRem   : {nEntityRem}");
Console.WriteLine();
Console.WriteLine("Party parser results:");
Console.WriteLine($"  PartyList   : {nPartyList}");
Console.WriteLine($"  PartyUpdate : {nPartyUpdate}");
Console.WriteLine($"  PartyAccept : {nPartyAccept}");
Console.WriteLine($"  PartyRequest: {nPartyRequest}");
Console.WriteLine($"  PartyLeft   : {nPartyLeft}");
Console.WriteLine($"  PartyEjected: {nPartyEjected}");
Console.WriteLine($"  Dungeon     : {nDungeon}");
Console.WriteLine($"  PartyCP     : {nPartyCp}");
if (samplePartyList.Count > 0) { Console.WriteLine("Sample party:"); foreach (var s in samplePartyList) Console.WriteLine("  " + s); }

// ── pass 3: per-actor DPS over time using packet timestamps ───────────
// Boss-scoped semantics matching the original A2Power UI:
//   * Each new boss MobSpawn (isBoss==1) resets the accumulator and starts
//     the timer from that boss's first incoming damage.
//   * Only damage to that boss's entityId counts.
//   * Heals are excluded (they don't show up in the original DPS bars).
Console.WriteLine();
Console.WriteLine("DPS timeline (packet-clock, boss-scoped):");

var actorDmg  = new Dictionary<int, long>();
var actorName = new Dictionary<int, string>();
var actorJob  = new Dictionary<int, int>();

int    currentBossId    = 0;
int    currentBossCode  = 0;
long   currentBossMaxHp = 0;
long   currentBossHp    = 0;
DateTime? firstBossHit  = null;
DateTime  lastSeen      = DateTime.MinValue;
double    nextDumpAt    = 5.0;

var dispatcher2 = new PacketDispatcher(skills);
var party2      = new PartyStreamParser();
dispatcher2.UserInfo += (e, n, sid, jc, self) => { actorName[e] = n; actorJob[e] = jc; };
dispatcher2.MobSpawn += (mobId, mobCode, hp, isBoss) =>
{
    if (isBoss == 0 || hp <= 0) return;
    // Skip invisible/control bosses the game uses for room transitions.
    var probedName = skills.GetMobName(mobCode);
    if (probedName == null || probedName.StartsWith("M_PD_") || probedName.Contains("Invisible")) return;
    if (mobId == currentBossId) { currentBossHp = hp; return; }
    // New visible boss → finalise the previous fight (if any) before resetting.
    if (firstBossHit != null && actorDmg.Count > 0)
    {
        double dur = (lastSeen - firstBossHit.Value).TotalSeconds;
        Console.WriteLine($"  ── final {skills.GetMobName(currentBossCode) ?? $"mob {currentBossCode}"} (id={currentBossId}) at t={dur:0.0}s ──");
        DumpDps(dur);
    }
    currentBossId    = mobId;
    currentBossCode  = mobCode;
    currentBossMaxHp = hp;
    currentBossHp    = hp;
    firstBossHit     = null;
    nextDumpAt       = 5.0;
    actorDmg.Clear();
    Console.WriteLine($"  ── boss spawn: {probedName} (id={mobId} hp={hp:n0}) ──");
};
dispatcher2.BossHp += (entityId, hp) =>
{
    if (entityId == currentBossId) currentBossHp = hp;
};
dispatcher2.Damage += (a, t, sk, dt, dmg, fl, mhc, mhd, heal, dot) =>
{
    if (currentBossId == 0 || t != currentBossId) return;   // boss-scoped
    long total = (long)dmg + mhd;
    if (heal > 0 && dmg == 0) return;
    if (total <= 0) return;
    actorDmg[a] = actorDmg.GetValueOrDefault(a) + total;
    currentBossHp = Math.Max(0, currentBossHp - total);
    firstBossHit ??= lastSeen;
};

var flows2 = new Dictionary<(IPAddress, int, IPAddress, int), TcpReassembler>();
var stream2 = new StreamProcessor(
    (data, off, len) => { dispatcher2.Dispatch(data, off, len); party2.Feed(new ReadOnlySpan<byte>(data, off, len)); });

foreach (var path in files) WalkPcap(path, OnSegment3);
void OnSegment3(DateTime ts, IPAddress src, int sp, IPAddress dst, int dp, uint seq, byte[] payload)
{
    if (sp != serverPort) return;
    var key = (src, sp, dst, dp);
    if (!flows2.TryGetValue(key, out var rasm))
    {
        rasm = new TcpReassembler(b => stream2.ProcessData(b));
        flows2[key] = rasm;
    }
    lastSeen = ts;
    rasm.Feed(seq, payload);

    if (firstBossHit != null)
    {
        double elapsed = (ts - firstBossHit.Value).TotalSeconds;
        if (elapsed >= nextDumpAt)
        {
            DumpDps(elapsed);
            nextDumpAt += 5.0;
        }
    }
}
if (firstBossHit != null) DumpDps((lastSeen - firstBossHit.Value).TotalSeconds);

// ── pass 4: target histogram — every entityId that received any damage,
// sorted by total. Reveals whether a "missing boss" is parsed under a
// different entityId than the MobSpawn packet announced. ───────────
Console.WriteLine();
Console.WriteLine("Top 15 damage receivers (any target):");
var rxDmg  = new Dictionary<int, long>();
var rxHits = new Dictionary<int, int>();
var rxMobCode = new Dictionary<int, int>();
int rxBossId = 0;

var dispatcher3 = new PacketDispatcher(skills);
dispatcher3.MobSpawn += (mobId, mobCode, hp, isBoss) =>
{
    if (!rxMobCode.ContainsKey(mobId) || isBoss == 1) rxMobCode[mobId] = mobCode;
};
dispatcher3.Damage += (a, t, sk, dt, dmg, fl, mhc, mhd, heal, dot) =>
{
    if (heal > 0 && dmg == 0) return;
    long total = (long)dmg + mhd;
    if (total <= 0) return;
    rxDmg[t]  = rxDmg.GetValueOrDefault(t)  + total;
    rxHits[t] = rxHits.GetValueOrDefault(t) + 1;
};
var flows3  = new Dictionary<(IPAddress, int, IPAddress, int), TcpReassembler>();
var stream3 = new StreamProcessor((data, off, len) => dispatcher3.Dispatch(data, off, len));
foreach (var path in files) WalkPcap(path, OnSegment4);
void OnSegment4(DateTime ts, IPAddress src, int sp, IPAddress dst, int dp, uint seq, byte[] payload)
{
    if (sp != serverPort) return;
    var key = (src, sp, dst, dp);
    if (!flows3.TryGetValue(key, out var rasm))
    {
        rasm = new TcpReassembler(b => stream3.ProcessData(b));
        flows3[key] = rasm;
    }
    rasm.Feed(seq, payload);
}
foreach (var kv in rxDmg.OrderByDescending(k => k.Value).Take(15))
{
    var mobCode = rxMobCode.TryGetValue(kv.Key, out var mc) ? mc : 0;
    var name = mobCode > 0 ? (skills.GetMobName(mobCode) ?? $"mob {mobCode}") : "(unknown — no MobSpawn)";
    Console.WriteLine($"  rx={kv.Value,12:n0}  hits={rxHits[kv.Key],5}  entityId={kv.Key,6}  mobCode={mobCode,8}  {name}");
}

void DumpDps(double elapsed)
{
    if (actorDmg.Count == 0) return;
    long total = actorDmg.Values.Sum();
    var bossName = skills.GetMobName(currentBossCode) ?? $"mob {currentBossCode}";
    double hpPct = currentBossMaxHp > 0 ? (double)currentBossHp / currentBossMaxHp * 100 : 0;
    Console.WriteLine($"  t={elapsed,6:0.0}s  ★{bossName}  HP={currentBossHp:n0}/{currentBossMaxHp:n0} ({hpPct:0.#}%)  total={total,12:n0}");
    foreach (var kv in actorDmg.OrderByDescending(k => k.Value).Take(8))
    {
        var nick = actorName.GetValueOrDefault(kv.Key, $"#{kv.Key}");
        var jc   = actorJob.GetValueOrDefault(kv.Key, -1);
        long dps = elapsed > 0 ? (long)(kv.Value / elapsed) : 0;
        double pct = total > 0 ? (double)kv.Value / total * 100 : 0;
        Console.WriteLine($"      {nick,-12} job={jc,2} {kv.Value,12:n0}  {dps,8:n0}/s  {pct,5:0.0}%");
    }
}
Console.WriteLine();
Console.WriteLine("StreamProcessor diag: " + stream.Diag);
Console.WriteLine();
if (sampleDamage.Count > 0) { Console.WriteLine("Sample damage:");   foreach (var s in sampleDamage) Console.WriteLine("  " + s); }
if (sampleUser.Count   > 0) { Console.WriteLine("Sample userinfo:"); foreach (var s in sampleUser) Console.WriteLine("  " + s); }
if (sampleMob.Count    > 0) { Console.WriteLine("Sample mobspawn:"); foreach (var s in sampleMob)  Console.WriteLine("  " + s); }
if (sampleBossHp.Count > 0) { Console.WriteLine("Sample bosshp:");   foreach (var s in sampleBossHp) Console.WriteLine("  " + s); }

// ── pass 5: scan large dispatched messages for skill code patterns ──
Console.WriteLine();
Console.WriteLine("=== Scanning large messages (tag 0x1156) for skill codes ===");
var skillScanHits = new Dictionary<string, List<(int offset, int code)>>();
int msgCount = 0;
var scanStream = new StreamProcessor(
    (data, off, len) =>
    {
        if (len < 100) return;
        // Check for tag 0x11 0x56 within first 10 bytes
        int tagPos = -1;
        for (int i = off; i < Math.Min(off + 10, off + len - 1); i++)
            if (data[i] == 0x11 && data[i+1] == 0x56) { tagPos = i; break; }
        if (tagPos < 0 && len < 2000) return; // only scan large or tagged packets

        msgCount++;
        var found = new List<(int, int)>();
        // Scan for 4-byte LE integers in skill range 11000000-20000000
        for (int i = off; i + 4 <= off + len; i++)
        {
            int val = data[i] | (data[i+1] << 8) | (data[i+2] << 16) | (data[i+3] << 24);
            if (val >= 11_000_000 && val < 20_000_000 && val % 10000 == 0)
            {
                var sn = skills.GetSkillName(val);
                if (sn != null)
                    found.Add((i - off, val));
            }
        }
        if (found.Count >= 5)
        {
            string key = $"msg#{msgCount} tag={(tagPos >= 0 ? "0x1156" : "other")} len={len}";
            skillScanHits[key] = found;
        }
        // Find "대지의 응보" (17010000=0x01039C40) and "단죄" (17350000=0x0108DCA0) in large packets
        if (tagPos >= 0 && found.Count > 100)
        {
            Console.Error.WriteLine($"[SKILLDUMP] len={len} skillCount={found.Count}");
            // Search for target skills
            int[] targets = { 17010000, 17350000, 17440000 }; // 대지의응보, 단죄, 고결한기운
            foreach (int target in targets)
            {
                byte[] pattern = BitConverter.GetBytes(target);
                for (int i = off; i + 4 <= off + len; i++)
                {
                    if (data[i] == pattern[0] && data[i+1] == pattern[1] && data[i+2] == pattern[2] && data[i+3] == pattern[3])
                    {
                        // Found! Dump 30 bytes before and 30 bytes after
                        int relOff = i - off;
                        var sb = new System.Text.StringBuilder();
                        int from = Math.Max(off, i - 20);
                        int to   = Math.Min(off + len, i + 30);
                        for (int b = from; b < to; b++)
                        {
                            if (b == i) sb.Append("[");
                            sb.Append($"{data[b]:X2}");
                            if (b == i + 3) sb.Append("]");
                            sb.Append(" ");
                        }
                        Console.Error.WriteLine($"  {skills.GetSkillName(target)} at off={relOff}: {sb}");
                        break; // only first occurrence
                    }
                }
            }
        }
    });
var flows5 = new Dictionary<(IPAddress, int, IPAddress, int), TcpReassembler>();
foreach (var path in files) WalkPcap(path, (ts, src, sp, dst, dp, seq, payload) =>
{
    if (sp != serverPort) return;
    var key = (src, sp, dst, dp);
    if (!flows5.TryGetValue(key, out var rasm))
    {
        rasm = new TcpReassembler(b => scanStream.ProcessData(b));
        flows5[key] = rasm;
    }
    rasm.Feed(seq, payload);
});
if (skillScanHits.Count == 0)
    Console.WriteLine("  (no messages with 5+ skill codes found)");
else
{
    foreach (var kv in skillScanHits.OrderByDescending(x => x.Value.Count).Take(5))
    {
        Console.WriteLine($"  {kv.Key}: {kv.Value.Count} skill codes found");
        foreach (var (ofs, code) in kv.Value.Take(20))
            Console.WriteLine($"    offset={ofs,5}: {code} ({skills.GetSkillName(code)})");
        if (kv.Value.Count > 20) Console.WriteLine($"    ... +{kv.Value.Count - 20} more");
    }
}

return 0;

void WalkPcap(string path, Action<DateTime, IPAddress, int, IPAddress, int, uint, byte[]> onSegment)
{
    using var dev = new CaptureFileReaderDevice(path);
    dev.Open();
    while (dev.GetNextPacket(out var pc) == GetPacketStatus.PacketRead)
    {
        totalPackets++;
        var rc = pc.GetPacket();
        try
        {
            var pkt = rc.GetPacket();
            var ip  = pkt.Extract<IPPacket>();
            var tcp = pkt.Extract<TcpPacket>();
            if (ip is null || tcp is null) continue;
            var payload = tcp.PayloadData ?? Array.Empty<byte>();
            if (payload.Length == 0) continue;
            onSegment(rc.Timeval.Date, ip.SourceAddress, tcp.SourcePort, ip.DestinationAddress, tcp.DestinationPort, tcp.SequenceNumber, payload);
        }
        catch { }
    }
}

static void DumpNamedFrame(byte[] data, int off, int len, byte[] needle, ref int frameCount)
{
    if (needle.Length == 0 || frameCount >= 20) return;

    var span = new ReadOnlySpan<byte>(data, off, len);
    int first = span.IndexOf(needle);
    if (first < 0) return;

    frameCount++;
    Console.WriteLine($"[dump-name] frame#{frameCount} len={len} firstNameOffset={first}");
    Console.WriteLine($"[dump-name] head {HexWindow(span, 0, Math.Min(96, span.Length))}");

    int pos = first;
    int occurrence = 0;
    while (pos >= 0 && occurrence < 8)
    {
        occurrence++;
        int start = Math.Max(0, pos - 48);
        int end = Math.Min(span.Length, pos + needle.Length + 96);
        Console.WriteLine($"[dump-name] occ#{occurrence} off={pos} {HexWindow(span, start, end - start)}");

        int nextStart = pos + needle.Length;
        if (nextStart >= span.Length) break;
        int next = span[nextStart..].IndexOf(needle);
        pos = next < 0 ? -1 : nextStart + next;
    }
}

static string HexWindow(ReadOnlySpan<byte> span, int start, int count)
{
    var slice = span.Slice(start, count);
    var sb = new StringBuilder(slice.Length * 3);
    for (int i = 0; i < slice.Length; i++)
    {
        if (i > 0) sb.Append(' ');
        sb.Append(slice[i].ToString("X2"));
    }
    return sb.ToString();
}
