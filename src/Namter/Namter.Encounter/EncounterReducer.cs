using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Namter.GameData;

namespace Namter.Encounter;

// One ordered consumer owns each instance. The type intentionally does not synchronize mutable state.
public sealed class EncounterReducer
{
    private const long MaxUnixTimestampMs = 253_402_300_799_999;

    private sealed class ParticipantState(uint id)
    {
        public uint Id { get; } = id;
        public string Name { get; set; } = "";
        public ushort JobId { get; set; }
        public bool IsSelf { get; set; }
        public ulong Damage, Multi, Dot, Healing;
    }

    private sealed class OpenBuff(uint owner, uint target, uint buff, long start, long expiry)
    {
        public uint Owner { get; } = owner;
        public uint Target { get; } = target;
        public uint Buff { get; } = buff;
        public long Start { get; } = start;
        public long Refresh { get; set; } = start;
        public long Expiry { get; set; } = expiry;
    }

    private sealed class UptimeState { public ulong Duration; public uint Windows; }
    private sealed class BossCandidate(uint actor, uint code, string name, ulong? hp, ulong? maxHp)
    {
        public uint Actor { get; } = actor;
        public uint Code { get; set; } = code;
        public string Name { get; set; } = name;
        public ulong? Hp { get; set; } = hp;
        public ulong? MaxHp { get; set; } = maxHp;
    }

    private readonly EncounterReducerOptions _options;
    private readonly Dictionary<uint, ParticipantState> _participants = [];
    private readonly Dictionary<uint, uint> _summonOwners = [];
    private readonly Dictionary<uint, EntityRecord> _entities = [];
    private readonly Dictionary<(uint Owner, uint Target, uint Buff), OpenBuff> _openBuffs = [];
    private readonly Dictionary<(uint Owner, uint Target, uint Buff), UptimeState> _uptimes = [];
    private readonly Dictionary<uint, BossCandidate> _bossCandidates = [];
    private readonly List<DamageRecord> _events = [];
    private readonly List<BuffWindowRecord> _buffWindows = [];
    private readonly Dictionary<(IncompleteReasonCode Code, string Message), ulong> _reasons = [];
    private readonly List<EncounterDiagnostic> _callDiagnostics = [];
    private ulong _reasonOverflowCount;
    private EncounterRecord? _final;
    private uint _contentId, _dungeonId, _bossActorId, _bossCode;
    private string _bossName = "";
    private ulong? _lastHp, _maxHp;
    private long _startMs, _captureClockMs = -1, _lastCombatMs;

    public EncounterReducer(GameDataSnapshot gameData, EncounterReducerOptions options)
    {
        PinnedGameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.IdleTimeoutMs < 0 || options.MaxParticipants <= 0 || options.MaxEntities <= 0 ||
            options.MaxEvents <= 0 || options.MaxBuffWindows <= 0 || options.MaxIncompleteReasons <= 0 ||
            options.MaxIncompleteReasonUtf8Bytes <= 0 || options.MaxDiagnosticsPerUpdate <= 0 || options.MaxBossCandidates <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public GameDataSnapshot PinnedGameData { get; }
    public EncounterState State { get; private set; }
    public EncounterSnapshot? Current => State == EncounterState.Active ? BuildSnapshot() : null;

    public EncounterUpdate Apply(CombatEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        BeginCall();
        if (_final is not null) return Update(_final);
        long timestamp = TimestampMs(value.Provenance.FirstTimestampNs);
        if (!AdvanceClock(timestamp)) return Update(_final);
        if (_final is not null) return Update(_final);

        switch (value)
        {
            case ContentEvent content: ApplyContent(content, timestamp); break;
            case ActorObservedEvent actor: ApplyActor(actor); break;
            case PartyEvent party: ApplyParty(party); break;
            case MobSpawnedEvent mob: ApplyMob(mob); break;
            case BossHpEvent hp: ApplyBossHp(hp, timestamp); break;
            case DamageEvent damage: ApplyDamage(damage, timestamp); break;
            case BuffEvent buff: ApplyBuff(buff, timestamp); break;
            case EntityRemovedEvent removed when State == EncounterState.Active && removed.ActorId == _bossActorId:
                Finalize(timestamp, EncounterCompletionReason.BossRemoved); break;
            case CombatStateEvent combat: ApplyCombatState(combat, timestamp); break;
        }
        return Update(_final);
    }

    public EncounterUpdate AdvanceTo(long captureTimestampMs)
    {
        BeginCall();
        if (captureTimestampMs < 0) throw new ArgumentOutOfRangeException(nameof(captureTimestampMs));
        if (_final is not null) return Update(_final);
        AdvanceClock(captureTimestampMs);
        return Update(_final);
    }

    public EncounterUpdate CompleteInput(long captureTimestampMs)
    {
        BeginCall();
        if (captureTimestampMs < 0) throw new ArgumentOutOfRangeException(nameof(captureTimestampMs));
        if (_final is not null) return Update(_final);
        if (!AdvanceClock(captureTimestampMs)) captureTimestampMs = _captureClockMs;
        if (_final is null && State == EncounterState.Active)
            Finalize(captureTimestampMs, EncounterCompletionReason.EndOfInput);
        return Update(_final);
    }

    public EncounterUpdate MarkIncomplete(string reason)
    {
        BeginCall();
        if (_final is not null) return Update(_final);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An incomplete reason is required.", nameof(reason));
        Report(IncompleteReasonCode.ExternalIncomplete, EncounterDiagnosticCode.CaptureIncomplete, reason);
        return Update();
    }

    private bool AdvanceClock(long timestamp)
    {
        if (timestamp < _captureClockMs)
        {
            Report(IncompleteReasonCode.OutOfOrderEvent, EncounterDiagnosticCode.OutOfOrderEvent, "out-of-order capture time", timestamp);
            return false;
        }
        _captureClockMs = timestamp;
        if (timestamp > MaxUnixTimestampMs)
            Report(IncompleteReasonCode.TimestampOutOfRange, EncounterDiagnosticCode.TimestampOutOfRange, "capture timestamp outside UTC range", timestamp);
        CloseDueBuffs(timestamp);
        if (State == EncounterState.Active && timestamp - _lastCombatMs >= _options.IdleTimeoutMs)
            Finalize(timestamp, EncounterCompletionReason.IdleTimeout);
        return true;
    }

    private void ApplyContent(ContentEvent content, long timestamp)
    {
        if (State == EncounterState.Active && content.State == 0) { Finalize(timestamp, EncounterCompletionReason.ContentExited); return; }
        if (content.State != 0) { _contentId = content.ContentId; _dungeonId = content.DungeonId; }
    }

    private void ApplyActor(ActorObservedEvent actor)
    {
        if (!TryParticipant(actor.ActorId, out ParticipantState? participant)) return;
        participant.Name = actor.Name; participant.JobId = actor.JobId; participant.IsSelf = actor.IsSelf;
        EntityKind kind = actor.OwnerId == 0 ? EntityKind.Player : EntityKind.Summon;
        MergeEntity(new(actor.ActorId, actor.OwnerId, 0, kind, actor.Name));
    }

    private void ApplyParty(PartyEvent party)
    {
        if (party.ContentId != 0) _contentId = party.ContentId;
        if (party.DungeonId != 0) _dungeonId = party.DungeonId;
        if (!TryParticipant(party.ActorId, out ParticipantState? participant)) return;
        if (!string.IsNullOrEmpty(party.Name)) participant.Name = party.Name;
        MergeEntity(new(party.ActorId, 0, 0, EntityKind.Player, party.Name));
    }

    private void ApplyMob(MobSpawnedEvent mob)
    {
        uint code = mob.BossId != 0 ? mob.BossId : mob.MobId;
        if (PinnedGameData.Bosses.TryGetValue(code, out Boss? boss))
        {
            if (State == EncounterState.Idle)
            {
                if (AddBossCandidate(mob.ActorId, code, boss.Name, mob.CurrentHp, mob.MaxHp))
                    MergeEntity(new(mob.ActorId, 0, code, EntityKind.Boss, boss.Name));
                else MergeEntity(new(mob.ActorId, 0, code, EntityKind.Add, mob.Name));
            }
            else if (AcceptActiveBoss(mob.ActorId, code, boss.Name))
            {
                _lastHp = mob.CurrentHp; _maxHp = mob.MaxHp;
                MergeEntity(new(mob.ActorId, 0, code, EntityKind.Boss, boss.Name));
            }
            else MergeEntity(new(mob.ActorId, 0, code, EntityKind.Add, mob.Name));
        }
        else MergeEntity(new(mob.ActorId, mob.OwnerId, mob.MobId,
            mob.OwnerId == 0 ? EntityKind.Add : EntityKind.Summon, mob.Name));
    }

    private void ApplyBossHp(BossHpEvent hp, long timestamp)
    {
        uint code = hp.BossId != 0 ? hp.BossId : _bossCode;
        if (!PinnedGameData.Bosses.TryGetValue(code, out Boss? boss)) return;
        if (State == EncounterState.Idle)
        {
            if (AddBossCandidate(hp.ActorId, code, boss.Name, hp.CurrentHp, hp.MaxHp))
                MergeEntity(new(hp.ActorId, 0, code, EntityKind.Boss, boss.Name));
            else MergeEntity(new(hp.ActorId, 0, code, EntityKind.Add, boss.Name));
            return;
        }
        if (!AcceptActiveBoss(hp.ActorId, code, boss.Name))
        {
            MergeEntity(new(hp.ActorId, 0, code, EntityKind.Add, boss.Name));
            return;
        }
        MergeEntity(new(hp.ActorId, 0, code, EntityKind.Boss, boss.Name));
        _lastHp = hp.CurrentHp; _maxHp = hp.MaxHp;
        if (State == EncounterState.Active) _lastCombatMs = timestamp;
        if (State == EncounterState.Active && hp.CurrentHp == 0) Finalize(timestamp, EncounterCompletionReason.BossDeath);
    }

    private bool AddBossCandidate(uint actor, uint code, string name, ulong? hp, ulong? maxHp)
    {
        if (_bossCandidates.TryGetValue(actor, out BossCandidate? candidate))
        {
            candidate.Code = code; candidate.Name = name; candidate.Hp = hp; candidate.MaxHp = maxHp;
            return true;
        }
        if (_bossCandidates.Count >= _options.MaxBossCandidates)
        {
            Report(IncompleteReasonCode.CapacityExceeded, EncounterDiagnosticCode.CapacityExceeded, "boss candidate capacity exceeded");
            return false;
        }
        _bossCandidates.Add(actor, new(actor, code, name, hp, maxHp));
        return true;
    }

    private bool SelectBossCandidate(uint actor, long timestamp)
    {
        if (!_bossCandidates.TryGetValue(actor, out BossCandidate? candidate)) return false;
        _bossActorId = candidate.Actor; _bossCode = candidate.Code; _bossName = candidate.Name;
        _lastHp = candidate.Hp; _maxHp = candidate.MaxHp;
        Start(timestamp);
        _bossCandidates.Clear();
        return true;
    }

    private bool AcceptActiveBoss(uint actor, uint code, string name)
    {
        if (_bossActorId == actor && _bossCode == code) return true;
        Report(IncompleteReasonCode.BossIdentityConflict, EncounterDiagnosticCode.BossIdentityConflict,
            "conflicting authoritative boss identity");
        return false;
    }

    private void ApplyCombatState(CombatStateEvent combat, long timestamp)
    {
        if (combat.State != 0 && State == EncounterState.Idle) { SelectBossCandidate(combat.ActorId, timestamp); return; }
        if (combat.State == 0 && State == EncounterState.Active && combat.ActorId == _bossActorId)
            Finalize(timestamp, EncounterCompletionReason.CombatEnded);
    }

    private void ApplyDamage(DamageEvent damage, long timestamp)
    {
        if (State == EncounterState.Idle) SelectBossCandidate(damage.TargetId, timestamp);
        bool bossTarget = State == EncounterState.Active && damage.TargetId == _bossActorId;
        if (State != EncounterState.Active) return;
        if (_events.Count >= _options.MaxEvents)
        {
            Report(IncompleteReasonCode.CapacityExceeded, EncounterDiagnosticCode.CapacityExceeded, "event capacity exceeded");
            return;
        }
        uint attributed = _summonOwners.GetValueOrDefault(damage.ActorId, damage.ActorId);
        string actorName = _participants.GetValueOrDefault(attributed)?.Name ?? "";
        string skillName = PinnedGameData.Skills.GetValueOrDefault(damage.SkillId)?.Name ?? "";
        _events.Add(new(timestamp, damage.ActorId, attributed, actorName, damage.TargetId, bossTarget,
            damage.SkillId, skillName, damage.Damage, damage.MultiDamage, damage.Healing,
            damage.SpecialMask, damage.DamageType, damage.IsDot ? DamageCategory.Dot : DamageCategory.Damage));
        if (!TryParticipant(attributed, out ParticipantState? participant)) return;
        if (bossTarget)
        {
            if (damage.IsDot) SaturatingAdd(ref participant.Dot, damage.Damage); else SaturatingAdd(ref participant.Damage, damage.Damage);
            SaturatingAdd(ref participant.Multi, damage.MultiDamage);
        }
        SaturatingAdd(ref participant.Healing, damage.Healing);
        _lastCombatMs = timestamp;
    }

    private void ApplyBuff(BuffEvent buff, long timestamp)
    {
        if (State != EncounterState.Active) return;
        var key = (buff.OwnerId, buff.TargetId, buff.BuffId);
        if (buff.Operation == BuffOperation.Remove)
        {
            if (_openBuffs.Remove(key, out OpenBuff? removed)) CloseBuff(removed, timestamp, BuffWindowEnd.Removed);
            return;
        }
        if (buff.Operation is not (BuffOperation.Apply or BuffOperation.Refresh)) return;
        long expiry = AddDuration(timestamp, buff.DurationMs);
        if (_openBuffs.TryGetValue(key, out OpenBuff? open))
        {
            open.Refresh = timestamp; open.Expiry = expiry;
        }
        else TryOpenBuff(key, buff, timestamp, expiry);
        CloseDueBuffs(timestamp);
    }

    private void CloseDueBuffs(long timestamp)
    {
        foreach (var pair in _openBuffs.Where(x => x.Value.Expiry <= timestamp)
                     .OrderBy(x => x.Value.Expiry).ThenBy(x => x.Key.Owner).ThenBy(x => x.Key.Target).ThenBy(x => x.Key.Buff).ToArray())
        {
            _openBuffs.Remove(pair.Key);
            CloseBuff(pair.Value, pair.Value.Expiry, BuffWindowEnd.Expired);
        }
    }

    private void Start(long timestamp)
    {
        State = EncounterState.Active; _startMs = _lastCombatMs = timestamp;
        PinBossEntityRoles();
    }

    private void PinBossEntityRoles()
    {
        foreach (uint actor in _entities.Where(x => x.Value.Kind == EntityKind.Boss && x.Key != _bossActorId).Select(x => x.Key).ToArray())
            _entities[actor] = _entities[actor] with { Kind = EntityKind.Add };
    }

    private void Finalize(long timestamp, EncounterCompletionReason reason)
    {
        if (_final is not null) return;
        CloseDueBuffs(timestamp);
        foreach (OpenBuff open in _openBuffs.Values.OrderBy(x => x.Start).ThenBy(x => x.Owner).ThenBy(x => x.Target).ThenBy(x => x.Buff))
            CloseBuff(open, timestamp, BuffWindowEnd.EncounterEnd);
        _openBuffs.Clear();
        DataProvenance provenance = Provenance();
        _final = new(RecordId(), Identity(), _startMs, timestamp, provenance.IsComplete, reason,
            Participants(), Entities(), [.. _events], [.. _buffWindows], BuffUptimes(), provenance);
        State = provenance.IsComplete ? EncounterState.Completed : EncounterState.Incomplete;
    }

    private bool TryParticipant(uint id, out ParticipantState participant)
    {
        if (_participants.TryGetValue(id, out participant!)) return true;
        if (_participants.Count >= _options.MaxParticipants)
        {
            Report(IncompleteReasonCode.CapacityExceeded, EncounterDiagnosticCode.CapacityExceeded, "participant capacity exceeded");
            participant = null!; return false;
        }
        _participants.Add(id, participant = new(id)); return true;
    }

    private void MergeEntity(EntityRecord incoming)
    {
        if (!_entities.TryGetValue(incoming.ActorId, out EntityRecord? current))
        {
            if (_entities.Count >= _options.MaxEntities)
            {
                Report(IncompleteReasonCode.CapacityExceeded, EncounterDiagnosticCode.CapacityExceeded, "entity capacity exceeded");
                return;
            }
            current = incoming;
        }
        else if (incoming.Kind < current.Kind)
        {
            incoming = current;
        }
        else
        {
            uint owner = incoming.Kind == EntityKind.Summon
                ? (current.OwnerActorId != 0 ? current.OwnerActorId : incoming.OwnerActorId) : incoming.OwnerActorId;
            string name = string.IsNullOrEmpty(incoming.Name) ? current.Name : incoming.Name;
            incoming = incoming with { OwnerActorId = owner, Name = name };
        }
        _entities[incoming.ActorId] = incoming;
        if (incoming.Kind == EntityKind.Summon && incoming.OwnerActorId != 0)
            _summonOwners[incoming.ActorId] = incoming.OwnerActorId;
        else _summonOwners.Remove(incoming.ActorId);
    }

    private void CloseBuff(OpenBuff open, long timestamp, BuffWindowEnd end)
    {
        long safeEnd = Math.Max(open.Start, timestamp);
        if (_buffWindows.Count >= _options.MaxBuffWindows)
        {
            Report(IncompleteReasonCode.CapacityExceeded, EncounterDiagnosticCode.CapacityExceeded, "buff window capacity exceeded");
            return;
        }
        var key = (open.Owner, open.Target, open.Buff);
        if (!_uptimes.TryGetValue(key, out UptimeState? uptime)) _uptimes.Add(key, uptime = new());
        SaturatingAdd(ref uptime.Duration, (ulong)(safeEnd - open.Start));
        if (uptime.Windows != uint.MaxValue) uptime.Windows++; else Report(IncompleteReasonCode.ArithmeticOverflow, EncounterDiagnosticCode.ArithmeticOverflow, "buff window count overflow");
        _buffWindows.Add(new(open.Owner, open.Target, open.Buff,
            PinnedGameData.Buffs.GetValueOrDefault(open.Buff)?.Name ?? "", open.Start, open.Refresh, safeEnd, end));
    }

    private void TryOpenBuff((uint OwnerId, uint TargetId, uint BuffId) key, BuffEvent buff, long timestamp, long expiry)
    {
        if (_openBuffs.Count >= _options.MaxBuffWindows)
        {
            Report(IncompleteReasonCode.CapacityExceeded, EncounterDiagnosticCode.CapacityExceeded, "buff window capacity exceeded");
            return;
        }
        _openBuffs[key] = new(buff.OwnerId, buff.TargetId, buff.BuffId, timestamp, expiry);
    }

    private void SaturatingAdd(ref ulong target, ulong value)
    {
        if (ulong.MaxValue - target < value)
        {
            target = ulong.MaxValue;
            Report(IncompleteReasonCode.ArithmeticOverflow, EncounterDiagnosticCode.ArithmeticOverflow, "numeric total saturated");
        }
        else target += value;
    }

    private void Report(IncompleteReasonCode reasonCode, EncounterDiagnosticCode diagnosticCode, string message, long? timestamp = null)
    {
        string bounded = BoundUtf8(message);
        var key = (reasonCode, bounded);
        if (_reasons.TryGetValue(key, out ulong count)) _reasons[key] = count == ulong.MaxValue ? count : count + 1;
        else if (_reasons.Count < _options.MaxIncompleteReasons) _reasons.Add(key, 1);
        else _reasonOverflowCount = _reasonOverflowCount == ulong.MaxValue ? _reasonOverflowCount : _reasonOverflowCount + 1;
        if (_callDiagnostics.Count < _options.MaxDiagnosticsPerUpdate)
            _callDiagnostics.Add(new(diagnosticCode, bounded, timestamp ?? Math.Max(0, _captureClockMs)));
    }

    private string BoundUtf8(string value)
    {
        if (Encoding.UTF8.GetByteCount(value) <= _options.MaxIncompleteReasonUtf8Bytes) return value;
        byte[] bytes = new byte[_options.MaxIncompleteReasonUtf8Bytes];
        Encoding.UTF8.GetEncoder().Convert(value.AsSpan(), bytes, true, out _, out int used, out _);
        return Encoding.UTF8.GetString(bytes, 0, used);
    }

    private void BeginCall() => _callDiagnostics.Clear();
    private EncounterUpdate Update(EncounterRecord? final = null) => new(State, Current, final, [.. _callDiagnostics]);
    private EncounterSnapshot BuildSnapshot() => new(RecordId(), Identity(), _startMs, _captureClockMs,
        Participants(), Entities(), [.. _events], [.. _buffWindows], BuffUptimes(), Provenance());
    private EncounterIdentity Identity() => new(_contentId, _dungeonId, _bossActorId, _bossCode, _bossName, _lastHp, _maxHp);
    private ImmutableArray<ParticipantRecord> Participants() => [.. _participants.Values
        .Where(p => p.Damage != 0 || p.Multi != 0 || p.Dot != 0 || p.Healing != 0)
        .OrderBy(p => p.Id).Select(p => new ParticipantRecord(p.Id, p.Name, p.JobId, p.IsSelf, p.Damage, p.Multi, p.Dot, p.Healing))];
    private ImmutableArray<EntityRecord> Entities() => [.. _entities.Values.OrderBy(e => e.ActorId)];
    private ImmutableArray<BuffUptimeRecord> BuffUptimes() => [.. _uptimes.OrderBy(x => x.Key.Owner).ThenBy(x => x.Key.Target).ThenBy(x => x.Key.Buff)
        .Select(x => new BuffUptimeRecord(x.Key.Owner, x.Key.Target, x.Key.Buff,
            PinnedGameData.Buffs.GetValueOrDefault(x.Key.Buff)?.Name ?? "", x.Value.Duration, x.Value.Windows))];
    private DataProvenance Provenance()
    {
        var reasons = _reasons.OrderBy(x => x.Key.Code).ThenBy(x => x.Key.Message, StringComparer.Ordinal)
            .Select(x => new IncompleteReasonRecord(x.Key.Code, x.Key.Message, x.Value)).ToList();
        if (_reasonOverflowCount != 0)
            reasons.Add(new(IncompleteReasonCode.ReasonLimitReached, BoundUtf8("reason limit reached"), _reasonOverflowCount));
        return new(_options.AppVersion, _options.AbiVersion, PinnedGameData.DataVersion, PinnedGameData.SchemaVersion,
            PinnedGameData.ProtocolProfileVersion, PinnedGameData.ProtocolProfileName, _options.Backend, _options.CaptureId,
            reasons.Count == 0, [.. reasons]);
    }

    private Guid RecordId()
    {
        if (_options.RecordId != Guid.Empty) return _options.RecordId;
        string material = string.Create(CultureInfo.InvariantCulture,
            $"org.namter.encounter/uuidv8-sha256/v1\n{_options.CaptureId}\n{_startMs}\n{_bossActorId}\n{_bossCode}");
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), digest);
        Span<byte> id = digest[..16];
        id[6] = (byte)((id[6] & 0x0f) | 0x80);
        id[8] = (byte)((id[8] & 0x3f) | 0x80);
        return new Guid(id, bigEndian: true);
    }

    private static long AddDuration(long timestamp, uint duration) =>
        long.MaxValue - timestamp < duration ? long.MaxValue : timestamp + duration;
    private static long TimestampMs(ulong timestampNs) => timestampNs / 1_000_000 > long.MaxValue
        ? long.MaxValue : (long)(timestampNs / 1_000_000);
}
