using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Namter.GameData;

namespace Namter.Encounter;

public sealed class EncounterReducer
{
    private sealed class ParticipantState(uint id)
    {
        public uint Id { get; } = id;
        public string Name { get; set; } = "";
        public ushort JobId { get; set; }
        public bool IsSelf { get; set; }
        public ulong Damage;
        public ulong Multi;
        public ulong Dot;
        public ulong Healing;
    }

    private sealed class OpenBuff(uint owner, uint target, uint buff, long start)
    {
        public uint Owner { get; } = owner; public uint Target { get; } = target; public uint Buff { get; } = buff;
        public long Start { get; } = start; public long Refresh { get; set; } = start;
    }

    private readonly EncounterReducerOptions _options;
    private readonly Dictionary<uint, ParticipantState> _participants = [];
    private readonly Dictionary<uint, uint> _summonOwners = [];
    private readonly Dictionary<uint, EntityRecord> _entities = [];
    private readonly Dictionary<(uint Owner, uint Target, uint Buff), OpenBuff> _openBuffs = [];
    private readonly List<DamageRecord> _events = [];
    private readonly List<BuffWindowRecord> _buffWindows = [];
    private readonly SortedSet<string> _incompleteReasons = new(StringComparer.Ordinal);
    private EncounterRecord? _final;
    private uint _contentId, _dungeonId, _bossActorId, _bossCode;
    private string _bossName = "";
    private ulong _lastHp, _maxHp;
    private long _startMs, _lastInputMs = -1, _lastCombatMs;

    public EncounterReducer(GameDataSnapshot gameData, EncounterReducerOptions options)
    {
        PinnedGameData = gameData ?? throw new ArgumentNullException(nameof(gameData));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.IdleTimeoutMs < 0 || options.MaxParticipants <= 0 || options.MaxEntities <= 0 || options.MaxEvents <= 0 || options.MaxBuffWindows <= 0)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public GameDataSnapshot PinnedGameData { get; }
    public EncounterState State { get; private set; }
    public EncounterSnapshot? Current => State == EncounterState.Active ? BuildSnapshot() : null;

    public EncounterUpdate Apply(CombatEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (_final is not null) return Update([], _final);
        long timestamp = TimestampMs(value.Provenance.FirstTimestampNs);
        if (timestamp < _lastInputMs)
        {
            const string reason = "out-of-order event";
            _incompleteReasons.Add(reason);
            return Update([new(EncounterDiagnosticCode.OutOfOrderEvent, reason, timestamp)]);
        }
        _lastInputMs = timestamp;
        EncounterUpdate timed = AdvanceTo(timestamp);
        if (_final is not null) return timed;

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
                return Finalize(timestamp, EncounterCompletionReason.BossRemoved);
            case CombatStateEvent combat when combat.ActorId == _bossActorId:
                if (combat.State != 0 && State == EncounterState.Idle) Start(timestamp);
                else if (combat.State == 0 && State == EncounterState.Active) return Finalize(timestamp, EncounterCompletionReason.CombatEnded);
                break;
        }
        return Update([], _final);
    }

    public EncounterUpdate AdvanceTo(long captureTimestampMs)
    {
        if (captureTimestampMs < 0) throw new ArgumentOutOfRangeException(nameof(captureTimestampMs));
        if (_final is not null) return Update([], _final);
        if (captureTimestampMs < _lastInputMs)
        {
            const string reason = "out-of-order capture clock";
            _incompleteReasons.Add(reason);
            return Update([new(EncounterDiagnosticCode.OutOfOrderEvent, reason, captureTimestampMs)]);
        }
        if (State == EncounterState.Active && captureTimestampMs >= _lastCombatMs &&
            captureTimestampMs - _lastCombatMs >= _options.IdleTimeoutMs)
            return Finalize(captureTimestampMs, EncounterCompletionReason.IdleTimeout);
        return Update([]);
    }

    public EncounterUpdate CompleteInput(long captureTimestampMs)
    {
        if (captureTimestampMs < 0) throw new ArgumentOutOfRangeException(nameof(captureTimestampMs));
        if (_final is not null) return Update([], _final);
        if (captureTimestampMs < _lastInputMs)
        {
            _incompleteReasons.Add("out-of-order end-of-input");
            captureTimestampMs = _lastInputMs;
        }
        return State == EncounterState.Active
            ? Finalize(captureTimestampMs, EncounterCompletionReason.EndOfInput)
            : Update([]);
    }

    public EncounterUpdate MarkIncomplete(string reason)
    {
        if (_final is not null) return Update([], _final);
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An incomplete reason is required.", nameof(reason));
        _incompleteReasons.Add(reason);
        return Update([new(EncounterDiagnosticCode.CaptureIncomplete, reason, Math.Max(0, _lastInputMs))]);
    }

    private void ApplyContent(ContentEvent content, long timestamp)
    {
        if (State == EncounterState.Active && content.State == 0)
        {
            Finalize(timestamp, EncounterCompletionReason.ContentExited);
            return;
        }
        if (content.State != 0) { _contentId = content.ContentId; _dungeonId = content.DungeonId; }
    }

    private void ApplyActor(ActorObservedEvent actor)
    {
        if (!_participants.TryGetValue(actor.ActorId, out ParticipantState? participant))
        {
            if (_participants.Count >= _options.MaxParticipants) { _incompleteReasons.Add("participant capacity exceeded"); return; }
            _participants.Add(actor.ActorId, participant = new(actor.ActorId));
        }
        participant.Name = actor.Name; participant.JobId = actor.JobId; participant.IsSelf = actor.IsSelf;
        if (PutEntity(new(actor.ActorId, actor.OwnerId, 0, actor.OwnerId == 0 ? EntityKind.Player : EntityKind.Summon, actor.Name)) && actor.OwnerId != 0)
            _summonOwners[actor.ActorId] = actor.OwnerId;
    }

    private void ApplyParty(PartyEvent party)
    {
        if (party.ContentId != 0) _contentId = party.ContentId;
        if (party.DungeonId != 0) _dungeonId = party.DungeonId;
        if (!TryParticipant(party.ActorId, out ParticipantState? participant)) return;
        if (!string.IsNullOrEmpty(party.Name)) participant.Name = party.Name;
        PutEntity(new(party.ActorId, 0, 0, EntityKind.Player, party.Name));
    }

    private void ApplyMob(MobSpawnedEvent mob)
    {
        uint code = mob.BossId != 0 ? mob.BossId : mob.MobId;
        if (PinnedGameData.Bosses.TryGetValue(code, out Boss? boss))
        {
            _bossActorId = mob.ActorId; _bossCode = code; _bossName = boss.Name;
            if (mob.CurrentHp != 0) _lastHp = mob.CurrentHp;
            if (mob.MaxHp != 0) _maxHp = mob.MaxHp;
            PutEntity(new(mob.ActorId, 0, code, EntityKind.Boss, boss.Name));
        }
        else if (mob.OwnerId != 0)
        {
            _summonOwners[mob.ActorId] = mob.OwnerId;
            if (!PutEntity(new(mob.ActorId, mob.OwnerId, mob.MobId, EntityKind.Summon, mob.Name)))
                _summonOwners.Remove(mob.ActorId);
        }
        else PutEntity(new(mob.ActorId, 0, mob.MobId, EntityKind.Add, mob.Name));
    }

    private void ApplyBossHp(BossHpEvent hp, long timestamp)
    {
        uint code = hp.BossId != 0 ? hp.BossId : _bossCode;
        if (!PinnedGameData.Bosses.TryGetValue(code, out Boss? boss)) return;
        _bossActorId = hp.ActorId; _bossCode = code; _bossName = boss.Name;
        PutEntity(new(hp.ActorId, 0, code, EntityKind.Boss, boss.Name));
        if (hp.CurrentHp != 0) _lastHp = hp.CurrentHp;
        if (hp.MaxHp != 0) _maxHp = hp.MaxHp;
        if (State == EncounterState.Active) _lastCombatMs = timestamp;
        if (State == EncounterState.Active && hp.CurrentHp == 0) Finalize(timestamp, EncounterCompletionReason.BossDeath);
    }

    private void ApplyDamage(DamageEvent damage, long timestamp)
    {
        bool bossTarget = _bossActorId != 0 && damage.TargetId == _bossActorId;
        if (bossTarget && State == EncounterState.Idle) Start(timestamp);
        if (State != EncounterState.Active) return;
        if (_events.Count >= _options.MaxEvents) { _incompleteReasons.Add("event capacity exceeded"); return; }
        uint attributed = _summonOwners.GetValueOrDefault(damage.ActorId, damage.ActorId);
        string actorName = _participants.GetValueOrDefault(attributed)?.Name ?? "";
        string skillName = PinnedGameData.Skills.GetValueOrDefault(damage.SkillId)?.Name ?? "";
        _events.Add(new(timestamp, damage.ActorId, attributed, actorName, damage.TargetId, bossTarget,
            damage.SkillId, skillName, damage.Damage, damage.MultiDamage, damage.Healing,
            damage.SpecialMask, damage.DamageType, damage.IsDot ? DamageCategory.Dot : DamageCategory.Damage));
        if (!TryParticipant(attributed, out ParticipantState? participant)) return;
        if (bossTarget)
        {
            if (damage.IsDot) Add(ref participant.Dot, damage.Damage); else Add(ref participant.Damage, damage.Damage);
            Add(ref participant.Multi, damage.MultiDamage);
        }
        Add(ref participant.Healing, damage.Healing);
        _lastCombatMs = timestamp;
    }

    private void ApplyBuff(BuffEvent buff, long timestamp)
    {
        if (State != EncounterState.Active) return;
        var key = (buff.OwnerId, buff.TargetId, buff.BuffId);
        if (buff.Action == (byte)BuffAction.Apply)
        {
            if (!_openBuffs.ContainsKey(key)) TryOpenBuff(key, buff, timestamp);
        }
        else if (buff.Action == (byte)BuffAction.Refresh)
        {
            if (_openBuffs.TryGetValue(key, out OpenBuff? open)) open.Refresh = timestamp;
            else TryOpenBuff(key, buff, timestamp);
        }
        else if (buff.Action == (byte)BuffAction.Remove && _openBuffs.Remove(key, out OpenBuff? open))
            CloseBuff(open, timestamp, BuffWindowEnd.Removed);
    }

    private void Start(long timestamp) { State = EncounterState.Active; _startMs = _lastCombatMs = timestamp; }

    private EncounterUpdate Finalize(long timestamp, EncounterCompletionReason reason)
    {
        if (_final is not null) return Update([], _final);
        foreach (OpenBuff open in _openBuffs.Values.OrderBy(x => x.Start).ThenBy(x => x.Owner).ThenBy(x => x.Target).ThenBy(x => x.Buff))
            CloseBuff(open, timestamp, BuffWindowEnd.EncounterEnd);
        _openBuffs.Clear();
        DataProvenance provenance = Provenance();
        _final = new(RecordId(), Identity(), _startMs, timestamp, provenance.IsComplete, reason,
            Participants(), Entities(), [.. _events], [.. _buffWindows], provenance);
        State = provenance.IsComplete ? EncounterState.Completed : EncounterState.Incomplete;
        return Update([], _final);
    }

    private bool TryParticipant(uint id, out ParticipantState participant)
    {
        if (_participants.TryGetValue(id, out participant!)) return true;
        if (_participants.Count >= _options.MaxParticipants) { _incompleteReasons.Add("participant capacity exceeded"); return false; }
        _participants.Add(id, participant = new(id)); return true;
    }

    private void CloseBuff(OpenBuff open, long timestamp, BuffWindowEnd end)
    {
        if (_buffWindows.Count >= _options.MaxBuffWindows) { _incompleteReasons.Add("buff window capacity exceeded"); return; }
        _buffWindows.Add(new(open.Owner, open.Target, open.Buff,
            PinnedGameData.Buffs.GetValueOrDefault(open.Buff)?.Name ?? "", open.Start, open.Refresh, timestamp, end));
    }

    private void TryOpenBuff((uint OwnerId, uint TargetId, uint BuffId) key, BuffEvent buff, long timestamp)
    {
        if (_openBuffs.Count >= _options.MaxBuffWindows)
        {
            _incompleteReasons.Add("buff window capacity exceeded");
            return;
        }
        _openBuffs[key] = new(buff.OwnerId, buff.TargetId, buff.BuffId, timestamp);
    }

    private bool PutEntity(EntityRecord entity)
    {
        if (_entities.ContainsKey(entity.ActorId)) { _entities[entity.ActorId] = entity; return true; }
        if (_entities.Count >= _options.MaxEntities) { _incompleteReasons.Add("entity capacity exceeded"); return false; }
        _entities.Add(entity.ActorId, entity); return true;
    }

    private void Add(ref ulong target, ulong value)
    {
        ulong old = target; target = unchecked(old + value);
        if (target < old) { target = ulong.MaxValue; _incompleteReasons.Add("arithmetic overflow"); }
    }

    private EncounterSnapshot BuildSnapshot() => new(RecordId(), Identity(), _startMs, _lastInputMs,
        Participants(), Entities(), [.. _events], [.. _buffWindows], Provenance());
    private EncounterIdentity Identity() => new(_contentId, _dungeonId, _bossActorId, _bossCode, _bossName, _lastHp, _maxHp);
    private ImmutableArray<ParticipantRecord> Participants() => [.. _participants.Values
        .Where(p => p.Damage != 0 || p.Multi != 0 || p.Dot != 0 || p.Healing != 0)
        .OrderBy(p => p.Id).Select(p => new ParticipantRecord(p.Id, p.Name, p.JobId, p.IsSelf, p.Damage, p.Multi, p.Dot, p.Healing))];
    private ImmutableArray<EntityRecord> Entities() => [.. _entities.Values.OrderBy(e => e.ActorId)];
    private DataProvenance Provenance() => new(_options.AppVersion, _options.AbiVersion, PinnedGameData.DataVersion,
        PinnedGameData.SchemaVersion, PinnedGameData.ProtocolProfileVersion, PinnedGameData.ProtocolProfileName,
        _options.Backend, _options.CaptureId, _incompleteReasons.Count == 0, [.. _incompleteReasons]);
    private EncounterUpdate Update(ImmutableArray<EncounterDiagnostic> diagnostics, EncounterRecord? final = null) =>
        new(State, Current, final, diagnostics);
    private Guid RecordId()
    {
        if (_options.RecordId != Guid.Empty) return _options.RecordId;
        string material = string.Create(CultureInfo.InvariantCulture,
            $"namter-encounter-v1\n{_options.CaptureId}\n{_startMs}\n{_bossActorId}\n{_bossCode}");
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(material), digest);
        Span<byte> id = digest[..16];
        id[6] = (byte)((id[6] & 0x0f) | 0x50);
        id[8] = (byte)((id[8] & 0x3f) | 0x80);
        return new Guid(id);
    }
    private static long TimestampMs(ulong timestampNs) => timestampNs / 1_000_000 > long.MaxValue ? long.MaxValue : (long)(timestampNs / 1_000_000);
}
