using System;
using System.Net;

namespace A2Meter.Dps;

/// Single TCP segment payload, direction-tagged. Live capture and pcap replay
/// both produce these so downstream parsers don't care which one is the source.
internal readonly record struct TcpSegment(
    DateTime TimestampUtc,
    IPAddress SrcAddress, int SrcPort,
    IPAddress DstAddress, int DstPort,
    uint SeqNumber,
    byte[] Payload);

/// Aggregated combat hit raised by the protocol parser through IPacketSource.
/// hitFlags bitmask: 0x01=back, 0x02|0x04=block, 0x08=perfect, 0x10=hardHit, 0x100=crit
internal readonly record struct CombatHitArgs(
    int ActorId, int TargetId, string Name, int JobCode,
    long Damage, uint HitFlags, bool IsHeal, string? Skill,
    int ExtraHits, bool IsDot, int[]? Specs);

/// Common contract for anything that produces ordered TCP segments.
/// Two implementations: LivePacketSniffer (Npcap/SharpPcap) and PcapReplaySource (offline file).
internal interface IPacketSource : IDisposable
{
    bool IsRunning { get; }
    event Action<TcpSegment>? SegmentReceived;

    /// Combat events parsed downstream. Wired up by the protocol parser, not the source itself.
    event Action<CombatHitArgs>? CombatHit;
    event Action<MobTarget?>? TargetChanged;
    /// Fired when a boss or dummy mob spawns (before any damage).
    event Action<MobTarget>? MobSpawned;
    /// Fired when an entity is removed from the world (death / despawn).
    event Action<int>? EntityRemoved;
    event Action<PartyMember>? PartyMemberSeen;
    /// Fired specifically when a party-request packet arrives (07 97). Distinct
    /// from PartyMemberSeen — used to trigger the party-request toast in the UI.
    event Action<PartyMember>? PartyRequestReceived;
    event Action? PartyLeft;
    /// Dungeon enter/exit. Arg = dungeonId (>0 = entered, 0 = left).
    event Action<int>? DungeonChanged;
    /// Buff apply/remove. Args: (entityId, buffId, type, durationMs, timestamp, casterId).
    event Action<int, int, int, uint, long, int>? BuffEvent;
    event Action<int, int, uint, long, int>? BuffRefreshEvent;
    event Action<int, int>? CombatStateChanged;
    event Action<int, uint>? RemainHpChanged;
    event Action<int, uint, uint, int>? NpcGroggyChanged;
    event Action<int, int, int>? TargetOn;
    event Action<int, int>? TargetOff;
    event Action<uint>? ZoneMoved;

    void Start();
    void Stop();
}
