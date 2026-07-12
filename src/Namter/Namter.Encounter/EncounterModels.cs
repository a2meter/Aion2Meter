using System.Collections.Immutable;

namespace Namter.Encounter;

public enum EncounterState { Idle, Active, Completed, Incomplete }
public enum EncounterCompletionReason { BossDeath, BossRemoved, CombatEnded, ContentExited, IdleTimeout, EndOfInput }
public enum EncounterDiagnosticCode { OutOfOrderEvent, CapacityExceeded, ArithmeticOverflow, CaptureIncomplete }
public enum DamageCategory { Damage, Dot }
public enum BuffAction : byte { Apply = 1, Refresh = 2, Remove = 3 }
public enum BuffWindowEnd { Removed, EncounterEnd }
public enum EntityKind { Player, Summon, Boss, Add }

public sealed record EncounterReducerOptions(
    long IdleTimeoutMs,
    Guid RecordId,
    string AppVersion,
    uint AbiVersion,
    string Backend,
    string CaptureId,
    int MaxParticipants = 1024,
    int MaxEntities = 4096,
    int MaxEvents = 1_000_000,
    int MaxBuffWindows = 65_536)
{
    public EncounterReducerOptions() : this(30_000, Guid.Empty, "", 1, "", "") { }
}

public sealed record EncounterIdentity(
    uint ContentId,
    uint DungeonId,
    uint BossActorId,
    uint BossCode,
    string Name,
    ulong LastHp,
    ulong MaxHp);

public sealed record ParticipantRecord(
    uint ActorId,
    string Name,
    ushort JobId,
    bool IsSelf,
    ulong Damage,
    ulong MultiDamage,
    ulong DotDamage,
    ulong Healing);

public sealed record EntityRecord(uint ActorId, uint OwnerActorId, uint MobCode, EntityKind Kind, string Name);

public sealed record DamageRecord(
    long TimestampMs,
    uint SourceActorId,
    uint AttributedActorId,
    string ActorName,
    uint TargetActorId,
    bool IsBossTarget,
    uint SkillId,
    string SkillName,
    ulong Damage,
    ulong MultiDamage,
    ulong Healing,
    uint SpecialMask,
    byte DamageType,
    DamageCategory Category);

public sealed record BuffWindowRecord(
    uint OwnerId,
    uint TargetId,
    uint BuffId,
    string Name,
    long StartTimestampMs,
    long LastRefreshTimestampMs,
    long EndTimestampMs,
    BuffWindowEnd EndReason);

public sealed record DataProvenance(
    string AppVersion,
    uint AbiVersion,
    ulong DataVersion,
    uint SchemaVersion,
    uint ProtocolProfileVersion,
    string ProtocolProfileName,
    string Backend,
    string CaptureId,
    bool IsComplete,
    ImmutableArray<string> IncompleteReasons);

public sealed record EncounterSnapshot(
    Guid Id,
    EncounterIdentity Encounter,
    long StartTimestampMs,
    long LastTimestampMs,
    ImmutableArray<ParticipantRecord> Participants,
    ImmutableArray<EntityRecord> Entities,
    ImmutableArray<DamageRecord> Events,
    ImmutableArray<BuffWindowRecord> BuffWindows,
    DataProvenance Provenance);

public sealed record EncounterRecord(
    Guid Id,
    EncounterIdentity Encounter,
    long StartTimestampMs,
    long EndTimestampMs,
    bool IsComplete,
    EncounterCompletionReason CompletionReason,
    ImmutableArray<ParticipantRecord> Participants,
    ImmutableArray<EntityRecord> Entities,
    ImmutableArray<DamageRecord> Events,
    ImmutableArray<BuffWindowRecord> BuffWindows,
    DataProvenance Provenance);

public sealed record EncounterDiagnostic(EncounterDiagnosticCode Code, string Message, long TimestampMs);

public sealed record EncounterUpdate(
    EncounterState State,
    EncounterSnapshot? Snapshot,
    EncounterRecord? FinalRecord,
    ImmutableArray<EncounterDiagnostic> Diagnostics);
