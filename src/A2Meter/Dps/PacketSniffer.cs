using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace A2Meter.Dps;

/// Live capture source. Opens ALL viable adapters in parallel, waits for the
/// first game-protocol packet (tcp port 13328), locks onto that adapter, and
/// closes the rest. This eliminates adapter-guessing on multi-NIC machines.
internal sealed class PacketSniffer : IPacketSource, IInternalEventRaise
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

    public bool IsRunning { get; private set; }

    private readonly string _filter;
    private readonly string? _adapterSpec;

    /// The single adapter we locked onto after detecting game traffic.
    private ICaptureDevice? _lockedDevice;
    /// All candidate adapters opened during the detection phase.
    private List<ICaptureDevice>? _candidates;
    /// 0 = still scanning, 1 = locked onto an adapter.
    private int _locked;

    public PacketSniffer(string filter = "tcp port 13328", string? adapterSpec = null)
    {
        _filter = filter;
        _adapterSpec = adapterSpec;
    }

    public void Start()
    {
        if (IsRunning) return;

        // If the user specified an adapter, use single-adapter mode (no scanning).
        if (!string.IsNullOrWhiteSpace(_adapterSpec))
        {
            var dev = SelectExplicit(_adapterSpec!)
                      ?? throw new InvalidOperationException($"Adapter '{_adapterSpec}' not found.");
            OpenSingle(dev);
            return;
        }

        // Multi-adapter scan: open every viable adapter and wait for game packets.
        var devices = CaptureDeviceList.Instance;
        _candidates = new List<ICaptureDevice>();

        foreach (var d in devices)
        {
            if (d is LibPcapLiveDevice ld)
            {
                var desc = ld.Description ?? "";
                if (desc.Contains("loopback", StringComparison.OrdinalIgnoreCase)) continue;
                if (!HasIPv4(ld)) continue;
            }

            try
            {
                d.Open(DeviceModes.Promiscuous, read_timeout: 1000);
                if (!string.IsNullOrWhiteSpace(_filter)) d.Filter = _filter;
                d.OnPacketArrival += OnProbePacket;
                d.StartCapture();
                _candidates.Add(d);

                Console.Error.WriteLine($"[sniffer] probing: {DisplayName(d)}");
            }
            catch
            {
                // Some adapters may fail to open (permissions, busy, etc.) — skip.
            }
        }

        if (_candidates.Count == 0)
            throw new InvalidOperationException("No suitable capture adapter found.");

        IsRunning = true;
        Console.Error.WriteLine($"[sniffer] scanning {_candidates.Count} adapter(s) for game traffic (filter={_filter})...");
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        // Close the locked device.
        StopDevice(_lockedDevice);
        _lockedDevice = null;

        // Close any remaining candidates (shouldn't be any after lock, but be safe).
        if (_candidates != null)
        {
            foreach (var d in _candidates) StopDevice(d);
            _candidates = null;
        }
    }

    /// Test hook — synthesize a CombatHit without going through the wire.
    public void EmitCombatForTest(int actorId, int targetId, string name, int jobCode, long dmg, uint hitFlags, bool heal, string? skill = null, int extraHits = 0, bool isDot = false)
        => CombatHit?.Invoke(new CombatHitArgs(actorId, targetId, name, jobCode, dmg, hitFlags, heal, skill, extraHits, isDot, null));

    public void Dispose() => Stop();

    /// Callback during the scanning phase. The first adapter that delivers a
    /// TCP packet with payload wins — we lock onto it and close all others.
    private void OnProbePacket(object sender, PacketCapture e)
    {
        if (_locked != 0) return; // Already locked, ignore stragglers.

        try
        {
            var rc  = e.GetPacket();
            var pkt = rc.GetPacket();
            var seg = TryExtractTcpSegment(pkt, rc.Timeval.Date);
            if (!seg.HasValue || seg.Value.Payload.Length == 0) return;

            // First adapter to deliver a real TCP payload wins.
            if (Interlocked.CompareExchange(ref _locked, 1, 0) != 0) return;

            var winner = (ICaptureDevice)sender;
            _lockedDevice = winner;

            // Swap callback from probe to permanent.
            winner.OnPacketArrival -= OnProbePacket;
            winner.OnPacketArrival += OnPacket;

            // Emit this first segment so it doesn't get lost.
            SegmentReceived?.Invoke(seg.Value);

            // Close all other adapters.
            var others = _candidates;
            _candidates = null;
            if (others != null)
            {
                foreach (var d in others)
                {
                    if (ReferenceEquals(d, winner)) continue;
                    d.OnPacketArrival -= OnProbePacket;
                    StopDevice(d);
                }
            }

            Console.Error.WriteLine($"[sniffer] locked onto: {DisplayName(winner)}");
        }
        catch
        {
            // Probe errors are non-fatal.
        }
    }

    /// Permanent callback after we've locked onto an adapter.
    private void OnPacket(object sender, PacketCapture e)
    {
        try
        {
            var rc  = e.GetPacket();
            var pkt = rc.GetPacket();
            var seg = TryExtractTcpSegment(pkt, rc.Timeval.Date);
            if (seg.HasValue) SegmentReceived?.Invoke(seg.Value);
        }
        catch
        {
            // Decoding errors must never kill the capture loop.
        }
    }

    /// Single-adapter mode (explicit --adapter spec).
    private void OpenSingle(ICaptureDevice dev)
    {
        dev.Open(DeviceModes.Promiscuous, read_timeout: 1000);
        if (!string.IsNullOrWhiteSpace(_filter)) dev.Filter = _filter;
        dev.OnPacketArrival += OnPacket;
        dev.StartCapture();
        _lockedDevice = dev;
        Interlocked.Exchange(ref _locked, 1);
        IsRunning = true;

        Console.Error.WriteLine($"[sniffer] capturing on: {DisplayName(dev)}  filter={_filter}");
    }

    internal static TcpSegment? TryExtractTcpSegment(Packet root, DateTime tsUtc)
    {
        var ip  = root.Extract<IPPacket>();
        var tcp = root.Extract<TcpPacket>();
        if (ip is null || tcp is null) return null;
        var payload = tcp.PayloadData ?? Array.Empty<byte>();
        return new TcpSegment(
            tsUtc, ip.SourceAddress, tcp.SourcePort,
            ip.DestinationAddress, tcp.DestinationPort,
            tcp.SequenceNumber, payload);
    }

    private static ICaptureDevice? SelectExplicit(string spec)
    {
        var devices = CaptureDeviceList.Instance;
        if (devices.Count == 0) return null;

        if (int.TryParse(spec, out int idx) && idx >= 0 && idx < devices.Count)
            return devices[idx];

        foreach (var d in devices)
        {
            if (d.Description?.Contains(spec, StringComparison.OrdinalIgnoreCase) == true) return d;
            if (d is LibPcapLiveDevice ld &&
                (ld.Interface?.FriendlyName?.Contains(spec, StringComparison.OrdinalIgnoreCase) == true ||
                 ld.Interface?.Name?.Contains(spec, StringComparison.OrdinalIgnoreCase) == true))
                return d;
        }
        return null;
    }

    private static bool HasIPv4(LibPcapLiveDevice d)
    {
        if (d.Interface?.Addresses is null) return false;
        foreach (var a in d.Interface.Addresses)
            if (a.Addr?.ipAddress?.AddressFamily == AddressFamily.InterNetwork)
                return true;
        return false;
    }

    private static string DisplayName(ICaptureDevice d)
    {
        var friendlyName = (d as LibPcapLiveDevice)?.Interface?.FriendlyName;
        return friendlyName ?? d.Description ?? d.Name ?? "<unknown>";
    }

    private static void StopDevice(ICaptureDevice? d)
    {
        if (d == null) return;
        try { d.StopCapture(); } catch { }
        try { d.Close(); }       catch { }
    }
}
