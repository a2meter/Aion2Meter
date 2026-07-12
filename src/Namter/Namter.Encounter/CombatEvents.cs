using System.Collections.Immutable;
using Namter.Core.Interop;

namespace Namter.Encounter;

public sealed record EventProvenance(
    ulong FirstTimestampNs,
    ulong LastTimestampNs,
    ulong Epoch,
    ulong FirstFileOffset,
    ulong LastFileOffset,
    uint SourceAddress,
    uint DestinationAddress,
    ushort SourcePort,
    ushort DestinationPort);

public abstract record CombatEvent(EventProvenance Provenance);

public sealed record DamageEvent(
    EventProvenance Provenance,
    uint ActorId,
    uint TargetId,
    uint SkillId,
    ulong Damage,
    ulong MultiDamage,
    ulong Healing,
    uint SpecialMask,
    byte DamageType,
    bool IsDot) : CombatEvent(Provenance);

public sealed record BuffEvent(
    EventProvenance Provenance,
    uint OwnerId,
    uint TargetId,
    uint BuffId,
    uint DurationMs,
    BuffOperation Operation,
    byte RawAction) : CombatEvent(Provenance);

public sealed record ActorObservedEvent(
    EventProvenance Provenance,
    uint ActorId,
    uint OwnerId,
    ushort ServerId,
    ushort JobId,
    string Name,
    bool IsSelf) : CombatEvent(Provenance);

public sealed record MobSpawnedEvent(
    EventProvenance Provenance,
    uint ActorId,
    uint OwnerId,
    uint MobId,
    uint BossId,
    ulong CurrentHp,
    ulong MaxHp,
    string Name,
    bool IsBoss) : CombatEvent(Provenance);

public sealed record BossHpEvent(
    EventProvenance Provenance,
    uint ActorId,
    uint BossId,
    ulong CurrentHp,
    ulong MaxHp) : CombatEvent(Provenance);

public sealed record EntityRemovedEvent(EventProvenance Provenance, uint ActorId) : CombatEvent(Provenance);

public sealed record PartyEvent(
    EventProvenance Provenance,
    uint PartyId,
    uint ActorId,
    uint ContentId,
    uint DungeonId,
    byte Action,
    string Name) : CombatEvent(Provenance);

public sealed record ContentEvent(
    EventProvenance Provenance,
    uint ContentId,
    uint DungeonId,
    byte State,
    string Name) : CombatEvent(Provenance);

public sealed record CombatStateEvent(EventProvenance Provenance, uint ActorId, byte State) : CombatEvent(Provenance);

public sealed record UnknownProtocolEvent(
    EventProvenance Provenance,
    ImmutableArray<byte> Payload) : CombatEvent(Provenance);

public static class CombatEventMapper
{
    public static CombatEvent Map(NativeEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        EventProvenance provenance = new(
            value.FirstTimestampNs, value.LastTimestampNs, value.Epoch,
            value.FirstFileOffset, value.LastFileOffset,
            value.SourceAddress, value.DestinationAddress,
            value.SourcePort, value.DestinationPort);

        return value.Kind switch
        {
            NativeEventKind.Damage => Damage(value, provenance, value.IsDot),
            NativeEventKind.Dot => Damage(value, provenance, true),
            NativeEventKind.Buff => new BuffEvent(provenance, value.OwnerId, value.TargetId,
                value.BuffId, value.DurationMs, (BuffOperation)value.BuffOperation, value.Action),
            NativeEventKind.SelfActor => Actor(value, provenance, true),
            NativeEventKind.OtherActor => Actor(value, provenance, false),
            NativeEventKind.MobSpawn => new MobSpawnedEvent(provenance, value.ActorId, value.OwnerId,
                value.MobId, value.BossId, value.CurrentHp, value.MaxHp, value.Name, value.IsBoss),
            NativeEventKind.BossHp => new BossHpEvent(provenance, value.ActorId, value.BossId,
                value.CurrentHp, value.MaxHp),
            NativeEventKind.EntityRemoved => new EntityRemovedEvent(provenance, value.ActorId),
            NativeEventKind.Party => new PartyEvent(provenance, value.PartyId, value.ActorId,
                value.ContentId, value.DungeonId, value.Action, value.Name),
            NativeEventKind.Content => new ContentEvent(provenance, value.ContentId, value.DungeonId,
                value.State, value.Name),
            NativeEventKind.CombatState => new CombatStateEvent(provenance, value.ActorId, value.State),
            NativeEventKind.UnknownProtocol => new UnknownProtocolEvent(provenance,
                ImmutableArray.Create(value.Payload.AsSpan().ToArray())),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Kind, "Not a combat event kind."),
        };
    }

    private static DamageEvent Damage(NativeEvent value, EventProvenance provenance, bool isDot) =>
        new(provenance, value.ActorId, value.TargetId, value.SkillId, value.Damage,
            value.MultiDamage, value.Healing, value.SpecialMask, value.DamageType, isDot);

    private static ActorObservedEvent Actor(NativeEvent value, EventProvenance provenance, bool isSelf) =>
        new(provenance, value.ActorId, value.OwnerId, value.ServerId, value.JobId, value.Name, isSelf);
}
