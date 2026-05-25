using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace A2Meter.Dps;

/// Replays a captured A2Capture session back through the same pipeline a live
/// sniffer would use. Reads the manifest.json sidecar to play files in order.
///
/// Two playback modes:
///   - Realtime  : honors original inter-packet timing (good for visual smoke tests)
///   - AsFastAsPossible : drains all packets immediately (good for unit tests)
internal sealed class PcapReplaySource : IPacketSource, IInternalEventRaise
{
    void IInternalEventRaise.RaiseCombatHit(CombatHitArgs args) => CombatHit?.Invoke(args);
    void IInternalEventRaise.RaiseTargetChanged(MobTarget? t)   => TargetChanged?.Invoke(t);
    void IInternalEventRaise.RaiseMobSpawned(MobTarget m)        => MobSpawned?.Invoke(m);
    void IInternalEventRaise.RaiseEntityRemoved(int id)          => EntityRemoved?.Invoke(id);
    void IInternalEventRaise.RaisePartyMemberSeen(PartyMember m) => PartyMemberSeen?.Invoke(m);
    void IInternalEventRaise.RaisePartyRequestReceived(PartyMember m) => PartyRequestReceived?.Invoke(m);
    void IInternalEventRaise.RaisePartyLeft() => PartyLeft?.Invoke();
    void IInternalEventRaise.RaiseDungeonChanged(int id) => DungeonChanged?.Invoke(id);
    void IInternalEventRaise.RaiseBuffEvent(int eid, int bid, int type, uint dur, long ts, int casterId) => BuffEvent?.Invoke(eid, bid, type, dur, ts, casterId);

    public event Action<TcpSegment>? SegmentReceived;
    public event Action<CombatHitArgs>? CombatHit;
    public event Action<MobTarget?>? TargetChanged;
    public event Action<MobTarget>? MobSpawned;
    public event Action<int>? EntityRemoved;
    public event Action<PartyMember>? PartyMemberSeen;
    public event Action<PartyMember>? PartyRequestReceived;
    public event Action? PartyLeft;
    public event Action<int>? DungeonChanged;
    public event Action<int, int, int, uint, long, int>? BuffEvent;
    public event Action? Completed;

    public bool IsRunning { get; private set; }

    private readonly string _sessionDir;
    private readonly bool _realtime;
    private readonly double _speed;
    private CancellationTokenSource? _cts;
    private Thread? _worker;

    public PcapReplaySource(string sessionDir, bool realtime = true, double speed = 1.0)
    {
        _sessionDir = sessionDir;
        _realtime = realtime;
        _speed = Math.Max(0.01, speed);
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _worker = new Thread(() => Run(_cts.Token)) { IsBackground = true, Name = "PcapReplay" };
        _worker.Start();
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        try { _cts?.Cancel(); } catch { }
        try { _worker?.Join(2000); } catch { }
    }

    public void Dispose() => Stop();

    private void Run(CancellationToken ct)
    {
        try
        {
            foreach (var pcap in EnumerateFilesFromManifest())
            {
                if (ct.IsCancellationRequested) break;
                ReplayFile(pcap, ct);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[replay] " + ex.Message);
        }
        finally
        {
            IsRunning = false;
            Completed?.Invoke();
        }
    }

    private IEnumerable<string> EnumerateFilesFromManifest()
    {
        var manifestPath = Path.Combine(_sessionDir, "manifest.json");
        if (File.Exists(manifestPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (doc.RootElement.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in files.EnumerateArray())
                {
                    var name = f.GetString();
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    yield return Path.Combine(_sessionDir, name);
                }
                yield break;
            }
        }
        // No manifest? Fall back to alphabetical ordering of any pcap files in the dir.
        foreach (var p in Directory.EnumerateFiles(_sessionDir, "*.pcap*"))
            yield return p;
    }

    private void ReplayFile(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return;

        using var dev = new CaptureFileReaderDevice(path);
        dev.Open();

        DateTime? firstTs = null;
        DateTime startWall = DateTime.UtcNow;

        while (!ct.IsCancellationRequested && dev.GetNextPacket(out var pc) == GetPacketStatus.PacketRead)
        {
            var rc = pc.GetPacket();
            DateTime tsUtc = rc.Timeval.Date;

            if (_realtime)
            {
                firstTs ??= tsUtc;
                var packetOffset = (tsUtc - firstTs.Value).TotalMilliseconds / _speed;
                var wallOffset   = (DateTime.UtcNow - startWall).TotalMilliseconds;
                var sleep = packetOffset - wallOffset;
                if (sleep > 1) Thread.Sleep((int)Math.Min(500, sleep));
            }

            try
            {
                var pkt = rc.GetPacket();
                var seg = PacketSniffer.TryExtractTcpSegment(pkt, tsUtc);
                if (seg.HasValue) SegmentReceived?.Invoke(seg.Value);
            }
            catch
            {
                // Skip malformed frames silently — replay should never abort on one bad packet.
            }
        }
    }
}
