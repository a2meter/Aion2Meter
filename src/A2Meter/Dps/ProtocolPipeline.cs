using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using A2Meter.Dps.Protocol;

namespace A2Meter.Dps;

/// Glues TcpSegment events from any IPacketSource into the C# protocol stack
/// (PacketProcessor → StreamProcessor → PacketDispatcher) and re-raises the
/// resulting combat events on the source itself, so DpsPipeline keeps using
/// IPacketSource.CombatHit / TargetChanged / PartyMemberSeen without knowing
/// that there's a parser sitting in between.
///
/// When the native PacketEngine.dll is available, it is used as the primary
/// parser for combat data (higher fidelity). The C# PacketDispatcher serves
/// as a fallback when the DLL is absent.
internal sealed class ProtocolPipeline : IDisposable
{
    private const int EstimatedBossHpThreshold = 1_000_000;

    private readonly IPacketSource _source;
    private readonly SkillDatabase _skills;
    private readonly PacketDispatcher _dispatcher;
    private readonly NativePacketEngine? _native;
    private readonly PacketProcessor _processor;
    private readonly PartyStreamParser _party;
    private readonly Channel<TcpSegment> _segments;
    private readonly CancellationTokenSource _segmentCts = new();
    private readonly Task _segmentConsumer;
    private readonly Action<string>? _log;
    private readonly HashSet<DamageEventKey> _managedDamageEvents = new();
    private readonly HashSet<BuffEventKey> _managedBuffEvents = new();
    private readonly HashSet<BuffRefreshEventKey> _managedBuffRefreshEvents = new();
    private bool _recordManagedEvents;

    /// entityId → (nickname, jobCode) populated by UserInfo packets.
    /// Damage events arrive before/after name packets in any order, so we
    /// stash names here and enrich CombatHit on emission.
    private readonly ConcurrentDictionary<int, (string Name, int JobCode)> _identities = new();

    /// entityId → combatPower buffered from CombatPower packets.
    /// Replayed into the PartyMember when UserInfo arrives (order-independent).
    private readonly ConcurrentDictionary<int, int> _combatPowers = new();

    /// entityId → serverId buffered from CharacterLookup packets.
    private readonly ConcurrentDictionary<int, int> _serverIds = new();

    /// clean nickname → (serverId, combatPower). Some packets carry server/CP
    /// by name before or instead of an entity-bound UserInfo packet.
    private readonly ConcurrentDictionary<string, (int ServerId, int CombatPower)> _nameHints =
        new(StringComparer.OrdinalIgnoreCase);

    /// petEntityId → ownerEntityId. Populated by Summon events so that
    /// summon damage is attributed to the summoning player.
    private readonly ConcurrentDictionary<int, int> _summons = new();

    /// Sub-hit skill codes whose damage should be attributed to a parent skill
    /// name. Matches original A2Power SubHitParentMap.
    private static readonly Dictionary<int, int> SubHitParentMap = new() { { 17040000, 17050000 } };

    /// Currently focused boss. Set on the first damage that lands on a known
    /// boss — *not* on MobSpawn — so multi-boss zones (where two bosses spawn
    /// almost simultaneously) latch onto whichever pull actually starts first.
    private MobTarget? _currentTarget;
    private int _currentTargetEntityId;
    /// All bosses we've seen MobSpawn for, indexed by entityId, so the damage
    /// path can promote one of them to _currentTarget when hits arrive.
    private readonly System.Collections.Generic.Dictionary<int, MobTarget> _knownBosses = new();
    private readonly System.Collections.Generic.HashSet<int> _nonBossEntities = new();
    private readonly System.Collections.Generic.HashSet<int> _estimatedBossEntities = new();

    public ProtocolPipeline(IPacketSource source, SkillDatabase? skills = null, Action<string>? log = null, int initialServerPort = 0, bool forceManagedParser = false)
    {
        _source = source;
        _log = log;
        _skills = skills ?? SkillDatabase.Shared;

        _dispatcher = new PacketDispatcher(_skills, log);
        _native = forceManagedParser ? null : NativePacketEngine.TryCreate(_skills, log);
        log?.Invoke(_native != null
            ? "[Pipeline] using NATIVE PacketEngine"
            : forceManagedParser
                ? "[Pipeline] using C# PacketDispatcher fallback (forced)"
                : "[Pipeline] using C# PacketDispatcher fallback");
        _party = new PartyStreamParser();

        _processor  = new PacketProcessor(
            messageHook: (data, off, len) =>
            {
                BeginMessageDispatch();
                try
                {
                    _recordManagedEvents = true;
                    _dispatcher.Dispatch(data, off, len);
                    _recordManagedEvents = false;
                    _native?.Dispatch(data, off, len);
                }
                finally
                {
                    _recordManagedEvents = false;
                    EndMessageDispatch();
                }
                _party.Feed(new ReadOnlySpan<byte>(data, off, len));
            },
            logSink: log,
            initialServerPort: initialServerPort);

        _segments = Channel.CreateUnbounded<TcpSegment>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _segmentConsumer = Task.Factory
            .StartNew(
                () => ProcessSegmentsAsync(_segmentCts.Token),
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap();

        _source.SegmentReceived += EnqueueSegment;

        // Managed parser is the dynamic protocol source. Its opcodes and boss
        // flags come from A2Web, so game packet/tag changes do not require a
        // PacketEngine.dll rebuild. Native PacketEngine remains supplemental
        // for packet families that the managed parser does not yet cover.
        _dispatcher.Damage        += OnManagedDamage;
        _dispatcher.UserInfo      += OnUserInfo;
        _dispatcher.MobSpawn      += OnMobSpawn;
        _dispatcher.BossHp        += OnBossHp;
        _dispatcher.Buff          += OnManagedBuff;
        _dispatcher.BuffRefresh   += OnManagedBuffRefresh;
        _dispatcher.CombatState   += OnCombatState;
        _dispatcher.RemainHp      += OnRemainHp;
        _dispatcher.NpcGroggy     += OnNpcGroggy;
        _dispatcher.TargetOn      += OnTargetOn;
        _dispatcher.TargetOff     += OnTargetOff;
        _dispatcher.ZoneMove      += OnZoneMove;
        _dispatcher.EntityRemoved += OnEntityRemoved;
        _dispatcher.Summon        += OnSummon;

        if (_native != null)
        {
            _native.Damage        += OnNativeDamage;
            _native.UserInfo      += OnUserInfo;
            _native.MobSpawn      += OnMobSpawn;
            _native.BossHp        += OnBossHp;
            _native.CombatPower   += OnCombatPower;
            _native.Summon        += OnSummon;
            _native.Buff          += OnNativeBuff;
            _native.BuffRefresh   += OnNativeBuffRefresh;
            _native.CombatState   += OnCombatState;
            _native.RemainHp      += OnRemainHp;
            _native.NpcGroggy     += OnNpcGroggy;
            _native.TargetOn      += OnTargetOn;
            _native.TargetOff     += OnTargetOff;
            _native.ZoneMove      += OnZoneMove;
            _native.PartyEvent    += OnNativePartyEvent;
            _native.EntityRemoved += OnEntityRemoved;
        }

        // Always wire C# dispatcher for packet types the native engine may miss.
        _dispatcher.CharacterLookup    += OnCharacterLookup;
        _dispatcher.CharacterEquipment += OnCharacterEquipment;
        _dispatcher.CombatPower        += OnCombatPower;

        _party.PartyList    += OnPartyRoster;
        _party.PartyUpdate  += OnPartyRoster;
        _party.PartyAccept  += OnPartyMember;
        _party.PartyRequest += OnPartyRequestMember;
        // Party-request packets (07 97) also raise a distinct event for the
        // UI toast for incoming party requests.
        _party.PartyRequest += OnPartyRequestReceived;
        _party.PartyLeft    += OnPartyLeft;
        _party.PartyEjected += OnPartyLeft;
        _party.CombatPowerDetected += OnPartyCpByName;
        _party.DungeonDetected     += OnDungeonDetected;
    }

    private readonly record struct DamageEventKey(
        int ActorId,
        int TargetId,
        int SkillCode,
        byte DamageType,
        int Damage,
        uint SpecialFlags,
        int MultiHitCount,
        int MultiHitDamage,
        int HealAmount,
        int IsDot);

    private readonly record struct BuffEventKey(
        int EntityId,
        int BuffId,
        int Type,
        uint DurationMs,
        long Timestamp,
        int CasterId);

    private readonly record struct BuffRefreshEventKey(
        int EntityId,
        int BuffId,
        uint DurationMs,
        long Timestamp,
        int CasterId);

    private void BeginMessageDispatch()
    {
        _managedDamageEvents.Clear();
        _managedBuffEvents.Clear();
        _managedBuffRefreshEvents.Clear();
    }

    private void EndMessageDispatch()
    {
        _managedDamageEvents.Clear();
        _managedBuffEvents.Clear();
        _managedBuffRefreshEvents.Clear();
    }

    public void Dispose()
    {
        _source.SegmentReceived -= EnqueueSegment;
        _segments.Writer.TryComplete();
        _segmentCts.Cancel();
        try { _segmentConsumer.Wait(TimeSpan.FromMilliseconds(500)); } catch { }
        _segmentCts.Dispose();
        _native?.Dispose();
    }

    private void OnSegment(TcpSegment seg) => _processor.Feed(seg);

    private void EnqueueSegment(TcpSegment seg)
    {
        _segments.Writer.TryWrite(seg);
    }

    private async Task ProcessSegmentsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var seg in _segments.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    OnSegment(seg);
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"[Pipeline] segment dispatch failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnManagedDamage(int actorId, int targetId, int skillCode, byte damageType,
                                 int damage, uint specialFlags, int multiHitCount, int multiHitDamage,
                                 int healAmount, int isDot)
    {
        if (_recordManagedEvents)
        {
            _managedDamageEvents.Add(new DamageEventKey(actorId, targetId, skillCode, damageType,
                damage, specialFlags, multiHitCount, multiHitDamage, healAmount, isDot));
        }

        OnDamage(actorId, targetId, skillCode, damageType, damage, specialFlags,
            multiHitCount, multiHitDamage, healAmount, isDot);
    }

    private void OnNativeDamage(int actorId, int targetId, int skillCode, byte damageType,
                                int damage, uint specialFlags, int multiHitCount, int multiHitDamage,
                                int healAmount, int isDot)
    {
        var key = new DamageEventKey(actorId, targetId, skillCode, damageType,
            damage, specialFlags, multiHitCount, multiHitDamage, healAmount, isDot);
        if (_managedDamageEvents.Contains(key)) return;

        OnDamage(actorId, targetId, skillCode, damageType, damage, specialFlags,
            multiHitCount, multiHitDamage, healAmount, isDot);
    }

    private void OnDamage(int actorId, int targetId, int skillCode, byte damageType,
                          int damage, uint specialFlags, int multiHitCount, int multiHitDamage,
                          int healAmount, int isDot)
    {
        bool isHeal = healAmount > 0 && damage == 0;
        // healAmount is tracked separately — don't inflate damage totals.
        long total  = isHeal ? healAmount : damage + multiHitDamage;
        if (total <= 0) return;

        // Resolve summon → owner so pet damage is attributed to the player.
        int resolvedActor = _summons.TryGetValue(actorId, out var owner) ? owner : actorId;

        // Decode specialization tiers from the raw → base skill code delta.
        int[]? specs = null;
        int rawSkill = _skills.LastRawSkillCode;
        if (isDot == 0)
        {
            if (rawSkill != 0 && rawSkill != skillCode)
                specs = SkillDatabase.DecodeSpecializations(rawSkill, skillCode);
        }

        // SubHitParentMap: remap sub-hit skill codes to parent for name lookup (matches original).
        int nameCode = skillCode;
        if (isDot == 0 && rawSkill % 10 != 0 && SubHitParentMap.TryGetValue(skillCode, out var parentCode))
            nameCode = parentCode;

        var skillName = _skills.GetSkillName(nameCode) ?? $"스킬#{nameCode}";
        var (name, jobCode) = _identities.TryGetValue(resolvedActor, out var id)
            ? id : ($"#{resolvedActor}", -1);

        // Update target BEFORE CombatHit so DpsPipeline sees the correct target
        // when processing the hit (matches A2Power's per-hit target lookup).
        MobTarget? bossForHit = null;
        if (!isHeal && _knownBosses.TryGetValue(targetId, out bossForHit))
        {
            if (_currentTargetEntityId != targetId)
            {
                _currentTargetEntityId = targetId;
                _currentTarget = bossForHit;
                TriggerTargetChanged(_currentTarget);
            }
        }

        TriggerCombatHit(resolvedActor, targetId, name, jobCode, total, specialFlags, isHeal, skillName, multiHitCount, isDot != 0, specs);

        if (isHeal) return;

        // Update HP after CombatHit so the damage is already recorded.
        if (bossForHit != null)
        {
            bossForHit.CurrentHp = Math.Max(0, bossForHit.CurrentHp - (damage + multiHitDamage));
            bossForHit.TotalDamageReceived += damage + multiHitDamage;
            TriggerTargetChanged(bossForHit);
        }
    }

    private void OnUserInfo(int entityId, string nickname, int serverId, int jobCode, int isSelf)
    {
        var hintKey = CleanNameKey(nickname);
        if (serverId <= 0 && _nameHints.TryGetValue(hintKey, out var hint) && hint.ServerId > 0)
            serverId = hint.ServerId;

        _identities[entityId] = (nickname, jobCode);
        if (serverId > 0)
            _serverIds[entityId] = serverId;
        // Replay any buffered CombatPower so it's never lost regardless of packet order.
        int cp = _combatPowers.TryGetValue(entityId, out var c) ? c
            : _nameHints.TryGetValue(hintKey, out hint) ? hint.CombatPower
            : 0;
        if (cp > 0)
            _combatPowers[entityId] = cp;
        bool isLocalUser = isSelf == 1 && serverId > 0 && jobCode > 0;

        TriggerPartyMemberSeen(new PartyMember
        {
            CharacterId = (uint)entityId,
            Nickname    = nickname,
            ServerId    = serverId,
            ServerName  = serverId > 0 ? ServerMap.GetName(serverId) : "",
            JobCode     = jobCode,
            IsSelf      = isLocalUser,
            IsLookup    = !isLocalUser && serverId > 0,
            CombatPower = cp,
        });
    }

    private void OnSummon(int actorId, int petId)
    {
        _summons[petId] = actorId;
    }

    private void OnMobSpawn(int mobId, int mobCode, int hp, int isBoss)
    {
        // A2Power: GetMobName → FormatUnknownMobName fallback. null이면 drop하지 않음.
        var name = _skills.GetMobName(mobCode);
        if (string.IsNullOrEmpty(name))
            name = $"몹#{mobCode}";
        if (name.StartsWith("M_PD_") || name.Contains("Invisible"))
        {
            MarkNonBossEntity(mobId, name, hp);
            return;
        }

        bool dummy = IsDummy(name);
        bool boss = isBoss != 0 || _skills.IsMobBoss(mobCode);
        if (!boss && !dummy)
        {
            MarkNonBossEntity(mobId, name, hp);
            return;
        }
        _nonBossEntities.Remove(mobId);
        _estimatedBossEntities.Remove(mobId);

        // A2Power: GetOrAdd + DeathConfirmed 시 리셋.
        if (!_knownBosses.TryGetValue(mobId, out var t))
        {
            t = new MobTarget { EntityId = mobId };
            _knownBosses[mobId] = t;
            (_source as IInternalEventRaise)?.RaiseMobSpawned(t);
        }
        else if (t.DeathConfirmed)
        {
            t.TotalDamageReceived = 0;
            t.DeathConfirmed = false;
            t.FirstBossHpSet = false;
            t.FirstBossHpSample = 0;
            t.HpAtLastSample = 0;
            t.DamageAtLastHpSample = 0;
            t.LastZeroHpAt = DateTime.MinValue;
        }

        t.Name = name;
        t.IsBoss = boss;

        // A2Power: hp > 0일 때만 MaxHp 설정.
        if (hp > 0)
        {
            long resolvedHp = (name == "가라앉은 에몬") ? ResolveEmonHp(hp) : hp;
            t.MaxHp = resolvedHp;
            t.CurrentHp = resolvedHp;
        }

        if (_currentTarget == null)
        {
            _currentTargetEntityId = mobId;
            _currentTarget = t;
            TriggerTargetChanged(_currentTarget);
        }
    }

    private void OnPartyRoster(System.Collections.Generic.List<PartyMember> members)
    {
        foreach (var m in members) OnPartyMember(m);
    }

    private void OnPartyMember(PartyMember m)
    {
        m.IsPartyMember = true;
        m.IsPartyRequest = false;
        if (m.CharacterId != 0)
        {
            _identities[(int)m.CharacterId] = (m.Nickname, m.JobCode);
            if (m.ServerId > 0)
                _serverIds[(int)m.CharacterId] = m.ServerId;
        }
        TriggerPartyMemberSeen(m);
    }

    private void OnPartyRequestMember(PartyMember m)
    {
        m.IsPartyRequest = true;
        m.IsLookup = true;
        m.IsPartyMember = false;
        if (m.CharacterId != 0 && m.ServerId > 0)
            _serverIds[(int)m.CharacterId] = m.ServerId;
        TriggerPartyMemberSeen(m);
    }

    private void OnPartyLeft()
    {
        // Signal that the party has disbanded — downstream clears party flags.
        (_source as IInternalEventRaise)?.RaisePartyLeft();
    }

    private void OnNativePartyEvent(int eventType, int memberIndex, int totalMembers,
        uint characterId, int serverId, string nickname, int jobCode, int level,
        int combatPower, int itemLevel)
    {
        if (eventType == 29)
        {
            OnPartyLeft();
            return;
        }

        if (string.IsNullOrWhiteSpace(nickname)) return;

        var member = new PartyMember
        {
            CharacterId = characterId,
            ServerId = serverId,
            ServerName = ServerMap.GetName(serverId),
            Nickname = nickname,
            JobCode = jobCode,
            JobName = JobMapping.GameToJobName(jobCode),
            Level = level,
            CombatPower = combatPower,
        };

        if (eventType == 7 || eventType == 100)
        {
            OnPartyRequestMember(member);
            OnPartyRequestReceived(member);
            return;
        }

        OnPartyMember(member);
    }

    /// Some server/CP packets are keyed by nickname instead of entityId. Keep
    /// those hints and replay them onto known identities so DPS rows can resolve
    /// server name, combat power, and score without guessing from API results.
    private void OnPartyCpByName(string nickname, int serverId, int cp)
    {
        var identity = Api.PlayncClient.NormalizeCharacterQuery(nickname, serverId);
        if (string.IsNullOrWhiteSpace(identity.Name) || identity.ServerId <= 0)
            return;

        nickname = identity.Name;
        serverId = identity.ServerId;
        string serverName = string.IsNullOrEmpty(identity.ServerName) ? ServerMap.GetName(serverId) : identity.ServerName;
        var hintKey = CleanNameKey(nickname);
        _nameHints[hintKey] = (serverId, cp);

        TriggerPartyMemberSeen(new PartyMember
        {
            Nickname    = nickname,
            ServerId    = serverId,
            ServerName  = serverName,
            CombatPower = cp,
            IsLookup    = true,
        });

        foreach (var kvp in _identities)
        {
            if (!string.Equals(CleanNameKey(kvp.Value.Name), hintKey, StringComparison.OrdinalIgnoreCase))
                continue;

            int entityId = kvp.Key;
            _serverIds[entityId] = serverId;
            if (cp > 0)
                _combatPowers[entityId] = cp;

            TriggerPartyMemberSeen(new PartyMember
            {
                CharacterId = (uint)entityId,
                Nickname    = kvp.Value.Name,
                ServerId    = serverId,
                ServerName  = serverName,
                JobCode     = kvp.Value.JobCode,
                CombatPower = cp,
                IsLookup    = true,
            });
        }
    }

    private void OnCombatPower(int entityId, int combatPower)
    {
        // Always buffer so OnUserInfo can replay if identity isn't known yet.
        _combatPowers[entityId] = combatPower;

        if (!_identities.TryGetValue(entityId, out var id)) return;
        int serverId = _serverIds.GetValueOrDefault(entityId, 0);
        TriggerPartyMemberSeen(new PartyMember
        {
            CharacterId = (uint)entityId,
            Nickname    = id.Name,
            ServerId    = serverId,
            ServerName  = serverId > 0 ? ServerMap.GetName(serverId) : "",
            JobCode     = id.JobCode,
            CombatPower = combatPower,
            IsLookup    = serverId > 0,
        });
    }

    private void OnBossHp(int entityId, int currentHp)
    {
        // A2Power: _mobs에 없거나 IsBoss가 아니면 무시.
        if (_nonBossEntities.Contains(entityId)) return;

        if (!_knownBosses.TryGetValue(entityId, out var t))
        {
            if (currentHp < EstimatedBossHpThreshold) return;

            t = new MobTarget
            {
                EntityId = entityId,
                Name = $"Boss #{entityId}",
                MaxHp = currentHp,
                CurrentHp = currentHp,
                IsBoss = true,
            };
            _knownBosses[entityId] = t;
            _estimatedBossEntities.Add(entityId);
            (_source as IInternalEventRaise)?.RaiseMobSpawned(t);
        }
        else if (!t.IsBoss)
        {
            return;
        }
        ApplyBossHpSample(t, currentHp);
    }

    private void MarkNonBossEntity(int entityId, string name, int hp)
    {
        _nonBossEntities.Add(entityId);
        if (!_estimatedBossEntities.Remove(entityId)) return;

        _knownBosses.Remove(entityId);
        (_source as IInternalEventRaise)?.RaiseMobSpawned(new MobTarget
        {
            EntityId = entityId,
            Name = name,
            MaxHp = Math.Max(0, hp),
            CurrentHp = Math.Max(0, hp),
            IsBoss = false,
        });
    }

    private void ApplyBossHpSample(MobTarget t, long currentHp)
    {
        // Track HP samples for cumulative damage death detection (A2Power).
        if (currentHp > 0)
        {
            // Boss is alive — undo death confirmation if it was set (A2Power: HP 재수신으로 처치확정 해제).
            if (t.DeathConfirmed)
                t.DeathConfirmed = false;
            t.LastZeroHpAt = DateTime.MinValue;
            t.HpAtLastSample = currentHp;
            t.DamageAtLastHpSample = t.TotalDamageReceived;

            // Track first BossHp sample below MaxHp for MaxHp correction.
            if (!t.FirstBossHpSet && currentHp < t.MaxHp)
            {
                t.FirstBossHpSample = currentHp;
                t.FirstBossHpSet = true;
            }
        }
        else
        {
            t.DeathConfirmed = true;
            t.LastZeroHpAt = DateTime.UtcNow;
            t.HpAtLastSample = 0;
            t.DamageAtLastHpSample = t.TotalDamageReceived;
        }

        t.CurrentHp = Math.Max(0, currentHp);
        if (currentHp > t.MaxHp) t.MaxHp = currentHp;
        if (t.EntityId == _currentTargetEntityId) TriggerTargetChanged(t);
    }

    private void OnRemainHp(int targetId, uint remainHp)
    {
        (_source as IInternalEventRaise)?.RaiseRemainHpChanged(targetId, remainHp);
        if (!_knownBosses.TryGetValue(targetId, out var t) || !t.IsBoss) return;

        if (t.HpAtLastSample == remainHp && t.CurrentHp == remainHp)
            return;

        ApplyBossHpSample(t, remainHp);
    }

    private void OnNpcGroggy(int targetId, uint maxGroggy, uint currentGroggy, int groggyStatus)
    {
        (_source as IInternalEventRaise)?.RaiseNpcGroggyChanged(targetId, maxGroggy, currentGroggy, groggyStatus);
        if (!_knownBosses.TryGetValue(targetId, out var t)) return;

        t.MaxGroggy = maxGroggy;
        t.CurrentGroggy = currentGroggy;
        t.GroggyStatus = groggyStatus;
        if (targetId == _currentTargetEntityId) TriggerTargetChanged(t);
    }

    private void OnTargetOn(int targetId, int aggroId, int targetingMode)
    {
        (_source as IInternalEventRaise)?.RaiseTargetOn(targetId, aggroId, targetingMode);
        if (!_knownBosses.TryGetValue(targetId, out var t)) return;

        t.AggroEntityId = aggroId;
        t.TargetingMode = targetingMode;
        if (targetId == _currentTargetEntityId) TriggerTargetChanged(t);
    }

    private void OnTargetOff(int targetId, int offMode)
    {
        (_source as IInternalEventRaise)?.RaiseTargetOff(targetId, offMode);
        if (!_knownBosses.TryGetValue(targetId, out var t)) return;

        t.AggroEntityId = 0;
        t.TargetingMode = 0;
        if (targetId == _currentTargetEntityId) TriggerTargetChanged(t);
    }

    private void OnCombatState(int entityId, int state)
    {
        (_source as IInternalEventRaise)?.RaiseCombatStateChanged(entityId, state);
        if (!_knownBosses.TryGetValue(entityId, out var t)) return;

        t.CombatState = state;
        if (entityId == _currentTargetEntityId) TriggerTargetChanged(t);
    }

    private void OnZoneMove(uint zoneId)
    {
        (_source as IInternalEventRaise)?.RaiseZoneMoved(zoneId);
        if (zoneId <= int.MaxValue && _skills.IsDungeon((int)zoneId))
            (_source as IInternalEventRaise)?.RaiseDungeonChanged((int)zoneId);
    }

    private void OnEntityRemoved(int entityId)
    {
        _nonBossEntities.Remove(entityId);
        _estimatedBossEntities.Remove(entityId);
        (_source as IInternalEventRaise)?.RaiseEntityRemoved(entityId);
    }

    // The IPacketSource events are publicly read-only; we route through reflection-free
    // helpers that the source itself exposes (PacketSniffer/PcapReplaySource each
    // expose internal raise methods).
    private void TriggerCombatHit(int actorId, int targetId, string? name, int jobCode, long damage, uint hitFlags, bool isHeal, string? skill, int extraHits, bool isDot, int[]? specs = null)
        => (_source as IInternalEventRaise)?.RaiseCombatHit(
            new CombatHitArgs(actorId, targetId, name ?? "", jobCode, damage, hitFlags, isHeal, skill, extraHits, isDot, specs));

    private static string CleanNameKey(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        int idx = name.IndexOf('[');
        return (idx > 0 ? name[..idx] : name).Trim();
    }

    private static bool IsDummy(string? name)
        => name != null && (name.Contains("허수아비") || name.Contains("샌드백"));

    /// Emon boss HP resolution table (A2Power _emonHpTiers).
    private static readonly (long packetHp, long realHp)[] EmonHpTiers =
    {
        (22200000L, 32200000L),
        (60750000L, 85100000L),
    };

    private static long ResolveEmonHp(long packetHp)
    {
        foreach (var (pHp, rHp) in EmonHpTiers)
            if ((double)Math.Abs(packetHp - pHp) < (double)pHp * 0.05)
                return rHp;
        return packetHp < 15000000 ? packetHp : (long)(packetHp * 1.4);
    }

    private void TriggerTargetChanged(MobTarget target)
        => (_source as IInternalEventRaise)?.RaiseTargetChanged(target);

    private void TriggerPartyMemberSeen(PartyMember member)
        => (_source as IInternalEventRaise)?.RaisePartyMemberSeen(member);

    private void OnPartyRequestReceived(PartyMember member)
        => (_source as IInternalEventRaise)?.RaisePartyRequestReceived(member);

    private void OnDungeonDetected(int dungeonId, int stage)
    {
        if (dungeonId > 0)
            (_source as IInternalEventRaise)?.RaiseZoneMoved((uint)dungeonId);
        (_source as IInternalEventRaise)?.RaiseDungeonChanged(dungeonId);
    }

    private void OnManagedBuff(int entityId, int buffId, int type, uint durationMs, long timestamp, int casterId)
    {
        if (_recordManagedEvents)
            _managedBuffEvents.Add(new BuffEventKey(entityId, buffId, type, durationMs, timestamp, casterId));
        OnBuff(entityId, buffId, type, durationMs, timestamp, casterId);
    }

    private void OnNativeBuff(int entityId, int buffId, int type, uint durationMs, long timestamp, int casterId)
    {
        if (_managedBuffEvents.Contains(new BuffEventKey(entityId, buffId, type, durationMs, timestamp, casterId))) return;
        OnBuff(entityId, buffId, type, durationMs, timestamp, casterId);
    }

    private void OnBuff(int entityId, int buffId, int type, uint durationMs, long timestamp, int casterId)
        => (_source as IInternalEventRaise)?.RaiseBuffEvent(entityId, buffId, type, durationMs, timestamp, casterId);

    private void OnManagedBuffRefresh(int entityId, int buffId, uint durationMs, long timestamp, int casterId)
    {
        if (_recordManagedEvents)
            _managedBuffRefreshEvents.Add(new BuffRefreshEventKey(entityId, buffId, durationMs, timestamp, casterId));
        OnBuffRefresh(entityId, buffId, durationMs, timestamp, casterId);
    }

    private void OnNativeBuffRefresh(int entityId, int buffId, uint durationMs, long timestamp, int casterId)
    {
        if (_managedBuffRefreshEvents.Contains(new BuffRefreshEventKey(entityId, buffId, durationMs, timestamp, casterId))) return;
        OnBuffRefresh(entityId, buffId, durationMs, timestamp, casterId);
    }

    private void OnBuffRefresh(int entityId, int buffId, uint durationMs, long timestamp, int casterId)
    {
        var source = _source as IInternalEventRaise;
        source?.RaiseBuffRefreshEvent(entityId, buffId, durationMs, timestamp, casterId);
        source?.RaiseBuffEvent(entityId, buffId, 0, durationMs, timestamp, casterId);
    }

    private void OnCharacterLookup(int entityId, string nickname, int serverId, int jobCode, int level, int combatPower)
    {
        _identities[entityId] = (nickname, jobCode);
        _serverIds[entityId] = serverId;
        TriggerPartyMemberSeen(new PartyMember
        {
            CharacterId = (uint)entityId,
            Nickname    = nickname,
            ServerId    = serverId,
            ServerName  = ServerMap.GetName(serverId),
            JobCode     = jobCode,
            Level       = level,
            CombatPower = combatPower,
            IsLookup    = true,
        });
    }

    private void OnCharacterEquipment(int entityId, List<EquipmentItem> items)
    {
        string? nick = _identities.TryGetValue(entityId, out var id) ? id.Name : null;
        int serverId = _serverIds.GetValueOrDefault(entityId, 0);
        Api.EquipmentCache.Instance.Store(entityId, nick, serverId, items);
    }
}

/// Internal hook that lets ProtocolPipeline re-raise events on its source
/// without exposing the events as writable from outside the source itself.
internal interface IInternalEventRaise
{
    void RaiseCombatHit(CombatHitArgs args);
    void RaiseTargetChanged(MobTarget? target);
    void RaiseMobSpawned(MobTarget mob);
    void RaiseEntityRemoved(int entityId);
    void RaisePartyMemberSeen(PartyMember member);
    void RaisePartyRequestReceived(PartyMember member);
    void RaisePartyLeft();
    void RaiseDungeonChanged(int dungeonId);
    void RaiseBuffEvent(int entityId, int buffId, int type, uint durationMs, long timestamp, int casterId);
    void RaiseBuffRefreshEvent(int entityId, int buffId, uint durationMs, long timestamp, int casterId);
    void RaiseCombatStateChanged(int entityId, int state);
    void RaiseRemainHpChanged(int targetId, uint remainHp);
    void RaiseNpcGroggyChanged(int targetId, uint maxGroggy, uint currentGroggy, int groggyStatus);
    void RaiseTargetOn(int targetId, int aggroId, int targetingMode);
    void RaiseTargetOff(int targetId, int offMode);
    void RaiseZoneMoved(uint zoneId);
}
