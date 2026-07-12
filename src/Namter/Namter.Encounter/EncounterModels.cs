using System.Collections.Immutable;

namespace Namter.Encounter;

public enum EncounterState { Idle, Active, Completed, Incomplete }
public enum EncounterCompletionReason { BossDeath, BossRemoved, CombatEnded, ContentExited, IdleTimeout, EndOfInput }
public enum EncounterDiagnosticCode { OutOfOrderEvent, BossIdentityConflict, CapacityExceeded, ArithmeticOverflow, CaptureIncomplete, TimestampOutOfRange }
public enum DamageCategory { Damage, Dot }
public enum BuffOperation : byte { Unknown = 0, Apply = 1, Refresh = 2, Remove = 3 }
public enum BuffWindowEnd { Removed, Expired, EncounterEnd }
public enum EntityKind { Unknown, Add, Player, Summon, Boss }
public enum IncompleteReasonCode { OutOfOrderEvent, BossIdentityConflict, CapacityExceeded, ArithmeticOverflow, ExternalIncomplete, TimestampOutOfRange, ReasonLimitReached }

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
    int MaxBuffWindows = 65_536,
    int MaxIncompleteReasons = 32,
    int MaxIncompleteReasonUtf8Bytes = 256,
    int MaxDiagnosticsPerUpdate = 64,
    int MaxBossCandidates = 32,
    bool RequireKnownParticipants = false,
    bool RequireCombatStart = false,
    bool PreserveBuffObservations = false,
    bool CarryInitialBuffState = false)
{
    public EncounterReducerOptions() : this(30_000, Guid.Empty, "", 1, "", "") { }
}

public sealed record EncounterIdentity(
    uint ContentId,
    uint DungeonId,
    uint BossActorId,
    uint BossCode,
    string Name,
    ulong? LastHp,
    ulong? MaxHp);

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

public sealed record BuffUptimeRecord(
    uint OwnerId,
    uint TargetId,
    uint BuffId,
    string Name,
    ulong TotalDurationMs,
    uint WindowCount);

public sealed record IncompleteReasonRecord(IncompleteReasonCode Code, string Message, ulong Count);

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
    ImmutableArray<IncompleteReasonRecord> IncompleteReasons);

public sealed record EncounterSnapshot(
    Guid Id,
    EncounterIdentity Encounter,
    long StartTimestampMs,
    long LastTimestampMs,
    ImmutableArray<ParticipantRecord> Participants,
    ImmutableArray<EntityRecord> Entities,
    ImmutableArray<DamageRecord> Events,
    ImmutableArray<BuffWindowRecord> BuffWindows,
    ImmutableArray<BuffUptimeRecord> BuffUptimes,
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
    ImmutableArray<BuffUptimeRecord> BuffUptimes,
    DataProvenance Provenance);

public sealed record EncounterDiagnostic(EncounterDiagnosticCode Code, string Message, long TimestampMs);

public sealed record EncounterUpdate(
    EncounterState State,
    EncounterSnapshot? Snapshot,
    EncounterRecord? FinalRecord,
    ImmutableArray<EncounterDiagnostic> Diagnostics);
