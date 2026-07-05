using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using INGMeter.Core;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: IngInspect <capture-session-dir>");
    return 1;
}

var sessionDir = args[0];
var manifestPath = Path.Combine(sessionDir, "manifest.json");
List<string> files;
if (File.Exists(manifestPath))
{
    using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
    files = doc.RootElement.GetProperty("files").EnumerateArray()
        .Select(f => Path.Combine(sessionDir, f.GetString()!))
        .ToList();
}
else
{
    files = Directory.EnumerateFiles(sessionDir, "*.pcap*").ToList();
}

int serverPort = DetectServerPort(files);
if (serverPort == 0) serverPort = 13328;

Console.WriteLine($"[inginspect] session={sessionDir}");
Console.WriteLine($"[inginspect] serverPort={serverPort}");

using var bridge = new PacketProcessorBridge(
    serverPort,
    tcpReorder: true,
    getSkillName: _ => null,
    containsSkillCode: _ => true,
    isStigmaSkillCode: _ => false);

int damage = 0, user = 0, extended = 0, mob = 0, local = 0, removed = 0;
var sampleDamage = new List<string>();
var sampleUser = new List<string>();
var sampleExtended = new List<string>();
var sampleLocal = new List<string>();
var sampleMob = new List<string>();
var targetDamage = new Dictionary<int, long>();
var targetHits = new Dictionary<int, int>();
var mobCodes = new Dictionary<int, int>();

bridge.OnDamage += (ts, actor, target, skill, raw, dtype, dmg, flags, mhc, mhd, heal, dot) =>
{
    damage++;
    long total = dmg + mhd;
    if (total > 0)
    {
        targetDamage[target] = targetDamage.GetValueOrDefault(target) + total;
        targetHits[target] = targetHits.GetValueOrDefault(target) + 1;
    }
    if (sampleDamage.Count < 12)
        sampleDamage.Add($"actor={actor} target={target} skill={skill} raw={raw} dmg={dmg} heal={heal} multi={mhc}/{mhd} dot={dot}");
};
bridge.OnUserInfo += (entityId, nickname, sid, job, extra, characterNumber) =>
{
    user++;
    if (sampleUser.Count < 30)
        sampleUser.Add($"entity={entityId} nick={nickname} sid={sid} job={job} extra={extra} charNo={characterNumber}");
};
bridge.OnExtendedUserInfo += info =>
{
    extended++;
    if (sampleExtended.Count < 20)
        sampleExtended.Add($"entity={info.EntityId} nick={info.Nickname} sid={info.ServerId} job={info.JobCode} src={info.Source} cp={info.CombatPower}");
};
bridge.OnLocalPlayerState += info =>
{
    local++;
    if (sampleLocal.Count < 30)
        sampleLocal.Add($"kind={info.Kind} value={info.Value} max={info.MaxValue} bonus={info.BonusValue} entity={info.EntityId} sid={info.ServerId} charNo={info.CharacterNumber} ctx={info.Context}");
};
bridge.OnMobSpawn += (entityId, mobCode, hp) =>
{
    mob++;
    mobCodes[entityId] = mobCode;
    if (sampleMob.Count < 12)
        sampleMob.Add($"entity={entityId} code={mobCode} hp={hp}");
};
bridge.OnEntityRemoved += _ => removed++;

bridge.Start();
foreach (var path in files)
    WalkPcap(path, (ts, src, sp, dst, dp, seq, payload) => bridge.Enqueue(sp, dp, payload, null, seq, ts));
bridge.Stop();

Console.WriteLine($"Damage={damage} UserInfo={user} Extended={extended} LocalState={local} MobSpawn={mob} Removed={removed}");
Dump("Sample local", sampleLocal);
Dump("Sample user", sampleUser);
Dump("Sample extended", sampleExtended);
Dump("Sample damage", sampleDamage);
Dump("Sample mob", sampleMob);
Console.WriteLine("Top targets:");
foreach (var row in targetDamage.OrderByDescending(kvp => kvp.Value).Take(12))
{
    int entityId = row.Key;
    mobCodes.TryGetValue(entityId, out var mobCode);
    targetHits.TryGetValue(entityId, out var hits);
    Console.WriteLine($"  target={entityId} damage={row.Value} hits={hits} mobCode={mobCode}");
}

return 0;

static int DetectServerPort(List<string> files)
{
    var portBytes = new Dictionary<int, long>();
    foreach (var path in files)
    {
        WalkPcap(path, (_, _, sp, _, _, _, payload) =>
        {
            if (payload.Length > 0)
                portBytes[sp] = portBytes.GetValueOrDefault(sp) + payload.Length;
        });
    }

    return portBytes
        .Where(kvp => kvp.Key < 20000)
        .OrderByDescending(kvp => kvp.Value)
        .Select(kvp => kvp.Key)
        .FirstOrDefault();
}

static void Dump(string title, List<string> rows)
{
    Console.WriteLine(title + ":");
    foreach (var row in rows)
        Console.WriteLine("  " + row);
}

static void WalkPcap(string path, Action<DateTime, IPAddress, int, IPAddress, int, uint, byte[]> onSegment)
{
    using var device = new CaptureFileReaderDevice(path);
    device.Open();
    while (device.GetNextPacket(out var pc) == GetPacketStatus.PacketRead)
    {
        var raw = pc.GetPacket();
        var packet = raw.GetPacket();
        var ip = packet.Extract<IPPacket>();
        var tcp = packet.Extract<TcpPacket>();
        var payload = tcp?.PayloadData ?? Array.Empty<byte>();
        if (ip == null || tcp == null || payload.Length == 0) continue;
        onSegment(raw.Timeval.Date, ip.SourceAddress, tcp.SourcePort, ip.DestinationAddress, tcp.DestinationPort, (uint)tcp.SequenceNumber, payload);
    }
}
