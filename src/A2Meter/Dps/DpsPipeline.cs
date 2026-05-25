using System;
using System.Collections.Generic;
using System.Linq;
using A2Meter.Api;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps.Protocol;
using D2DColor = Vortice.Mathematics.Color4;

namespace A2Meter.Dps;

/// Glues sniffer events to the meter, then pushes snapshots to the D2D canvas
/// at a fixed cadence (~10 Hz). Supports both WinForms (DpsCanvas) and headless
/// (ImGui polling via DataPushed event) modes.
internal sealed class DpsPipeline : IDisposable
{
    private readonly IPacketSource _source;
    private readonly DpsMeter _meter;
    private readonly PartyTracker _party;
    private readonly BuffTracker _buffTracker = new();
    private readonly System.Threading.Timer _pushTimer;
    private readonly CombatHistory _history = new();
    private readonly WebUploader _uploader = new();

    private const int PushIntervalMs = 100;
    /// Idle window after which an active session ends (matches original: 3s).
    private const double SessionIdleSeconds = 3.0;

    private MobTarget? _currentTarget;
    private int        _currentTargetId;     // _primaryTargetId in A2Power
    private DateTime   _lastHitUtc      = DateTime.MinValue;
    private long       _peakDpsThisSess;
    private bool       _sessionActive;
    private bool       _combatRecordSaved;   // duplicate-save guard
    private string?    _sessionId;           // UUID assigned at combat start, used for dedup
    private bool       _viewingHistory;
    private bool       _selfDetectedOnce;
    private int        _selfEntityId;        // tracks self entityId for zone-change detection
    private DpsCanvas.SessionSummary? _lastSummary;

    // ── Per-mob tracking (matches A2Power _mobs) ──
    private readonly Dictionary<int, MobTarget> _knownBosses = new();

    // ── Countdown timer mode ──
    private int        _countdownSec;       // 0 = off, 30/60/90/... = active limit
    private DateTime   _countdownStart;     // combat start time for countdown
    private bool       _countdownExpired;   // true = timer ended, stop measuring

    // ── Dungeon state ──
    private bool       _inDungeon;          // true when inside a dungeon instance
    private int?       _dungeonId;          // current dungeon ID for record storage

    /// Current dungeon ID (null if not in a dungeon instance). Exposed so the
    /// overlay can prefer the active dungeon when displaying a player's tier.
    public int? CurrentDungeonId => _dungeonId;

    // ── Removed entities tracking (A2Power _removedEntities) ──
    private readonly HashSet<int> _removedEntities = new();

    // ── Boss HP correction (A2Power TryCorrectMaxHp) ──
    private bool       _maxHpCorrected;

    // ── Timeline / HitLog (A2Power _timeline, _hitLog) ──
    private readonly List<TimelineEntry> _timeline = new();
    private int        _lastTimelineSec = -1;
    private readonly List<HitLogEntry> _hitLog = new();

    // ── Session start tracking ──
    private DateTime   _sessionStartUtc;

    // ── Network / performance monitors ──
    public PingMonitor Ping { get; } = new();

    // Cached last-frame data so we keep showing bars after session ends.
    private IReadOnlyList<DpsCanvas.PlayerRow>? _lastRows;
    private long   _lastTotal;
    private string _lastTimer = "";

    /// Per-actor running peak DPS this session (resets when session ends).
    private readonly Dictionary<int, long> _peakByActor = new();

    /// Fired at ~10 Hz with the latest snapshot data (for ImGui headless mode).
    public event Action<IReadOnlyList<DpsCanvas.PlayerRow>, long, string, MobTarget?, DpsCanvas.SessionSummary?>? DataPushed;

    /// Fired once when a new combat session starts (first hit recorded).
    public event Action? CombatStarted;

    public DpsPipeline(
        IPacketSource source,
        DpsMeter meter,
        PartyTracker party)
    {
        _source = source;
        _meter  = meter;
        _party  = party;

        _source.CombatHit       += OnCombatHit;
        _source.TargetChanged   += OnTargetChanged;
        _source.MobSpawned      += OnMobSpawned;
        _source.EntityRemoved   += OnEntityRemoved;
        _source.PartyMemberSeen += OnPartyMemberSeen;
        _source.PartyLeft       += () => _party.ClearPartyFlags();
        _source.DungeonChanged  += id => { _dungeonId = id > 0 ? id : (int?)null; _inDungeon = id > 0 && Dps.Protocol.SkillDatabase.Shared.IsDungeon(id); };
        _source.BuffEvent       += (eid, bid, type, dur, ts, casterId) => _buffTracker.OnBuff(eid, bid, type, dur, ts, casterId);
        _source.SegmentReceived += seg => Ping.Feed(seg);

        _pushTimer = new System.Threading.Timer(_ => Push(), null,
            System.Threading.Timeout.Infinite, PushIntervalMs);
    }

    public CombatHistory History => _history;
    public BuffTracker Buffs => _buffTracker;
    public void EnterHistoryView() => _viewingHistory = true;
    public void ExitHistoryView()  => _viewingHistory = false;

    /// Current countdown limit (0 = off).
    public int CountdownSeconds => _countdownSec;
    /// Whether the countdown has expired and DPS measurement is frozen.
    public bool CountdownExpired => _countdownExpired;

    /// Cycle countdown: off → 30 → 60 → 90 → 120 → 150 → 180 → off
    public void CycleCountdown()
    {
        if (_countdownSec >= 180) _countdownSec = 0;
        else _countdownSec += 30;
        _countdownExpired = false;
    }

    public void Start()
    {
        _source.Start();
        _pushTimer.Change(0, PushIntervalMs);
    }

    public void Stop()
    {
        _pushTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _source.Stop();
    }

    public void Reset()
    {
        _meter.Reset();
        _party.Clear();
        _buffTracker.Reset();
        _countdownExpired = false;
        _combatRecordSaved = false;
        _sessionId = null;
        _inDungeon = false;
        _dungeonId = null;
        _currentTargetId = 0;
        _currentTarget = null;
        _knownBosses.Clear();
        _removedEntities.Clear();
        _maxHpCorrected = false;
        _timeline.Clear();
        _lastTimelineSec = -1;
        _hitLog.Clear();
        _lastSummary = null;
        _lastRows = null;
        _sessionActive = false;
        _peakByActor.Clear();
        _peakDpsThisSess = 0;
    }

    private void OnPartyMemberSeen(PartyMember m)
    {
        _party.Upsert(m);

        // Trigger async skill level fetch from Plaync API (self + party only).
        if (!string.IsNullOrEmpty(m.Nickname) && m.ServerId > 0 && (m.IsSelf || m.IsPartyMember || m.IsPartyRequest))
            Api.SkillLevelCache.Instance.EnsureLoaded(m.Nickname, m.ServerId);

        // Detection triggers immediate refresh to show the new member row.

        // Zone change detection: entityId is per-zone — when it changes,
        // the player entered a new map.  Same entityId = stat update (buff etc.)
        // which must NOT disrupt the active session.
        // Exception: if the boss HP is below 100%, it's a phase transition within
        // the same fight (e.g., zone change mid-boss) — keep the session alive.
        if (m.IsSelf)
        {
            int eid = (int)m.CharacterId;
            if (_selfDetectedOnce && eid != _selfEntityId)
            {
                // Zone changed — always clear dungeon flag (re-established by
                // DungeonDetected if the new zone is still a dungeon instance).
                _inDungeon = false;

                // Keep session alive only if the boss is actively being fought
                // (alive + taken damage).  Dead boss (CurrentHp=0) must NOT
                // prevent cleanup — otherwise _inDungeon leaks to town.
                bool bossFightInProgress = _currentTarget is { IsBoss: true, MaxHp: > 0, CurrentHp: > 0 }
                    && _currentTarget.CurrentHp < _currentTarget.MaxHp;
                if (!bossFightInProgress)
                {
                    if (_sessionActive)
                        _sessionActive = false;
                    _currentTarget = null;
                    _currentTargetId = 0;
                }
            }
            _selfEntityId = eid;
            _selfDetectedOnce = true;
        }
    }

    /// Track boss/dummy spawns (matches A2Power _mobs dictionary).
    private void OnMobSpawned(MobTarget mob)
    {
        bool wasRemoved = _removedEntities.Remove(mob.EntityId);
        var existing = _knownBosses.GetValueOrDefault(mob.EntityId);
        // Reset encounter state for new or respawned mobs (A2Power ResetMobEncounterState).
        if (existing == null || wasRemoved || existing.DeathConfirmed)
        {
            mob.HasSelfParticipation = false;
            mob.LastSelfHitAt = DateTime.MinValue;
            mob.DeathConfirmed = false;
            mob.TotalDamageReceived = 0;
            mob.HpAtLastSample = 0;
            mob.DamageAtLastHpSample = 0;
            mob.FirstBossHpSet = false;
            mob.FirstBossHpSample = 0;
        }
        _knownBosses[mob.EntityId] = mob;
    }

    /// Boss entity removed from world → immediate session end.
    private void OnEntityRemoved(int entityId)
    {
        _removedEntities.Add(entityId);
        if (!_knownBosses.TryGetValue(entityId, out var mob)) return;
        mob.DeathConfirmed = true;
        mob.CurrentHp = 0;

        // Only end session if this was the active target.
        if (!_sessionActive || entityId != _currentTargetId) return;

        var snap = _meter.BuildTargetSnapshot(_currentTargetId);
        _lastSummary = BuildSummary(snap);
        SaveRecord(snap);
        _meter.Stop();
        _sessionActive = false;

        _lastRows = MapForCanvas(snap.Players, snap.ElapsedSeconds);
        _lastTotal = snap.TotalPartyDamage;
        _lastTimer = FormatTimer(snap.ElapsedSeconds);
    }

    /// Update target display. Does NOT drive hit filtering — that uses _knownBosses.
    private void OnTargetChanged(MobTarget? t)
    {
        _meter.SetTarget(t);
        _currentTarget = t;
        // Also register in _knownBosses so targets discovered via TargetChanged
        // (e.g., first boss surfaced by ProtocolPipeline before damage) are tracked.
        if (t != null && (t.IsBoss || IsDummy(t.Name)))
        {
            _knownBosses[t.EntityId] = t;
            // Remove from _removedEntities if HP is positive (boss HP re-received).
            if (t.CurrentHp > 0)
                _removedEntities.Remove(t.EntityId);
        }
    }

    private void OnCombatHit(CombatHitArgs e)
    {
        // ── Matches A2Power OnDamageCore → TryAccumulateDamage ──
        // A2Power applies ALL filters (target, dummy-self, target-switch) to
        // every hit including DoTs.  Only heals are accumulated separately.

        if (e.Damage <= 0 && !e.IsHeal) return;

        // Heals: accumulate on actor only (A2Power: orAdd.HealTotal += heal).
        // No target/self filter, no combat start trigger.
        if (e.IsHeal)
        {
            if (_sessionActive)
                _meter.RecordHit(e.ActorId, e.TargetId, e.Name, e.JobCode, e.Damage, e.HitFlags, true, e.Skill, e.ExtraHits, e.IsDot, e.Specs);
            return;
        }

        // ── Per-hit target check (A2Power: _mobs.TryGetValue(targetId)) ──
        if (!_knownBosses.TryGetValue(e.TargetId, out var hitMob))
            return; // Target not a known boss/dummy → drop.

        bool isDummy = IsDummy(hitMob.Name);
        if (!hitMob.IsBoss && !isDummy)
            return; // Not boss, not dummy → drop.

        // Dummy: self-only (A2Power: if (!flag || flag2))
        bool isSelf = e.ActorId == (_party.SelfEntityId ?? -1);
        if (isDummy && !isSelf)
            return;

        // Update per-mob self participation tracking (A2Power MobInfo).
        if (hitMob.IsBoss && isSelf)
        {
            hitMob.HasSelfParticipation = true;
            hitMob.LastSelfHitAt = DateTime.UtcNow;
        }

        // ── Target switching (A2Power _primaryTargetId logic) ──
        if (_currentTargetId == 0)
        {
            _currentTargetId = e.TargetId;
            if (hitMob.IsBoss)
                RequestMissingActorLookups();
        }
        else if (_currentTargetId != e.TargetId)
        {
            var prevMob = _knownBosses.GetValueOrDefault(_currentTargetId);
            bool prevRemoved = _removedEntities.Contains(_currentTargetId);
            bool prevDeathConfirmed = prevMob != null && prevMob.IsBoss && prevMob.DeathConfirmed && !IsDummy(prevMob.Name);
            bool prevInvalid = prevRemoved || prevDeathConfirmed;
            bool prevActive = prevMob != null && prevMob.IsBoss && !prevInvalid && prevMob.CurrentHp > 0 && !IsDummy(prevMob.Name);

            bool forceSelfNotParticipated = prevActive && isSelf && hitMob.IsBoss
                && prevMob != null && !prevMob.HasSelfParticipation;

            double prevIdleSec = (prevMob?.LastSelfHitAt == DateTime.MinValue)
                ? double.PositiveInfinity
                : (DateTime.UtcNow - prevMob!.LastSelfHitAt).TotalSeconds;
            bool forceSelfIdle = prevActive && isSelf && hitMob.IsBoss
                && prevMob != null && prevMob.LastSelfHitAt != DateTime.MinValue && prevIdleSec >= 10.0;

            if (prevActive && !forceSelfNotParticipated && !forceSelfIdle)
                return;

            bool prevInvalidated = prevInvalid || forceSelfNotParticipated || forceSelfIdle;
            if (!(!_sessionActive || prevInvalidated) || !(hitMob.IsBoss || isDummy))
                return;

            SaveRecord(_currentTargetId != 0
                ? _meter.BuildTargetSnapshot(_currentTargetId)
                : _meter.BuildCurrentSnapshot());
            ResetCombatStats();
            _currentTargetId = e.TargetId;
            if (hitMob.IsBoss)
                RequestMissingActorLookups();
        }

        // Countdown expired → freeze.
        if (_countdownExpired) return;

        // Don't start a new session on a dead/removed boss — prevents duplicate
        // saves when hits arrive after boss death confirmation.
        if (!_sessionActive && hitMob.IsBoss && (hitMob.DeathConfirmed || hitMob.CurrentHp <= 0))
            return;

        // ── Combat start ──
        if (!_sessionActive)
        {
            _sessionId = Guid.NewGuid().ToString();
            _combatRecordSaved = false;
            _party.PurgeNonParty();
            _buffTracker.Reset();
            _buffTracker.Start();
            if (_countdownSec > 0)
                _countdownStart = DateTime.UtcNow;
            _sessionActive = true;
            _sessionStartUtc = DateTime.UtcNow;
            CombatStarted?.Invoke();
        }

        _meter.RecordHit(e.ActorId, e.TargetId, e.Name, e.JobCode, e.Damage, e.HitFlags, false, e.Skill, e.ExtraHits, e.IsDot, e.Specs);
        _lastHitUtc = DateTime.UtcNow;

        // ── HitLog recording (A2Power _hitLog) ──
        double hitTime = _sessionActive ? (DateTime.UtcNow - _sessionStartUtc).TotalSeconds : 0;
        _hitLog.Add(new HitLogEntry
        {
            T = Math.Round(hitTime, 2),
            EntityId = e.ActorId,
            SkillName = e.Skill ?? "",
            Damage = e.Damage,
            Flags = e.HitFlags,
        });

        // ── Cumulative damage death detection (A2Power TryConfirmBossDeathByDamage) ──
        if (hitMob.IsBoss && TryConfirmBossDeathByDamage(hitMob))
        {
            // Death inferred from accumulated damage exceeding sampled HP.
        }

        // ── MaxHp correction (A2Power TryCorrectMaxHp) ──
        var primMob = _knownBosses.GetValueOrDefault(_currentTargetId);
        if (primMob is { IsBoss: true })
            TryCorrectMaxHp(primMob);
    }

    private void Push()
    {
        if (_viewingHistory) return;

        // Countdown and perf state is synced via DataPushed event.

        // When a boss is active, scope the canvas to that boss's damage only —
        // matches the original A2Power "기록 조회 중" view. Otherwise show the
        // party-wide roll-up.
        var snap = _currentTargetId != 0
            ? _meter.BuildTargetSnapshot(_currentTargetId)
            : _meter.BuildCurrentSnapshot();

        // Track per-session peak as the highest single-actor DPS we've observed.
        if (_sessionActive && snap.Players.Count > 0)
        {
            long top = snap.Players[0].Dps;
            if (top > _peakDpsThisSess) _peakDpsThisSess = top;
        }
        // Per-actor peak.
        foreach (var p in snap.Players)
        {
            if (!_peakByActor.TryGetValue(p.EntityId, out var cur) || p.Dps > cur)
                _peakByActor[p.EntityId] = p.Dps;
        }

        // Countdown timer expiry: freeze measurement when time is up.
        if (_sessionActive && _countdownSec > 0 && !_countdownExpired)
        {
            double elapsed = (DateTime.UtcNow - _countdownStart).TotalSeconds;
            if (elapsed >= _countdownSec)
            {
                _countdownExpired = true;
                _meter.Stop();
                _sessionActive = false;

                // Build final snapshot at countdown limit.
                var finalSnap = _currentTargetId != 0
                    ? _meter.BuildTargetSnapshot(_currentTargetId)
                    : _meter.BuildCurrentSnapshot();
                _lastRows = MapForCanvas(finalSnap.Players, _countdownSec);
                _lastTotal = finalSnap.TotalPartyDamage;
                _lastTimer = FormatTimer(_countdownSec);
                _lastSummary = BuildSummary(finalSnap);
                SaveRecord(finalSnap);
            }
        }

        // When countdown expired, show frozen data.
        if (_countdownExpired && _lastRows != null)
        {
            DataPushed?.Invoke(_lastRows, _lastTotal, _lastTimer, _currentTarget, _lastSummary);
            return;
        }

        // ── Timeline recording (A2Power Tick → _timeline) ──
        if (_sessionActive)
            CaptureTimelineSnapshot(snap);

        // End-of-session detection:
        // 1) Boss killed (HP=0) → stop immediately, no idle wait.
        // 2) 3s idle + (no target / not boss / dummy / removed entity) → stop.
        // 3) Party wipe (boss HP restored to full) → stop.
        var prim = _knownBosses.GetValueOrDefault(_currentTargetId);
        bool bossKilled = _sessionActive && prim is { IsBoss: true, MaxHp: > 0, CurrentHp: <= 0 };
        bool idleStop = _sessionActive && (DateTime.UtcNow - _lastHitUtc).TotalSeconds > SessionIdleSeconds
            && (prim == null || !prim.IsBoss || prim.CurrentHp <= 0
                || _removedEntities.Contains(_currentTargetId) || IsDummy(prim.Name));
        bool bossReset = _sessionActive && prim is { IsBoss: true, MaxHp: > 0 }
            && prim.CurrentHp >= prim.MaxHp
            && prim.HpAtLastSample > 0 && prim.HpAtLastSample < prim.MaxHp;

        if (bossKilled || idleStop || bossReset)
        {
            if (prim != null && !bossReset) prim.DeathConfirmed = true;
            _lastSummary = BuildSummary(snap);

            if (bossReset)
            {
                // Wipe — discard the short record, reset for next attempt.
                _lastRows = MapForCanvas(snap.Players, snap.ElapsedSeconds);
                _lastTotal = snap.TotalPartyDamage;
                _lastTimer = FormatTimer(snap.ElapsedSeconds);
                ResetCombatStats();
            }
            else
            {
                SaveRecord(snap);
                _lastRows = MapForCanvas(snap.Players, snap.ElapsedSeconds);
                _lastTotal = snap.TotalPartyDamage;
                _lastTimer = FormatTimer(snap.ElapsedSeconds);
                _meter.Stop();
                _sessionActive = false;
            }
        }

        // After session ends, keep displaying the last frame instead of empty.
        if (!_sessionActive && _lastRows != null)
        {
            DataPushed?.Invoke(_lastRows, _lastTotal, _lastTimer, _currentTarget, _lastSummary);
            return;
        }

        // When countdown active, show remaining time instead of elapsed.
        var rows = MapForCanvas(snap.Players, snap.ElapsedSeconds);
        string timer;
        if (_countdownSec > 0 && _sessionActive)
        {
            double remaining = Math.Max(0, _countdownSec - (DateTime.UtcNow - _countdownStart).TotalSeconds);
            timer = FormatTimer(remaining);
        }
        else
        {
            timer = FormatTimer(snap.ElapsedSeconds);
        }
        DataPushed?.Invoke(rows, snap.TotalPartyDamage, timer, _currentTarget, _lastSummary);
    }

    private DpsCanvas.SessionSummary BuildSummary(DpsSnapshot snap)
    {
        var top = snap.Players.Count > 0 ? snap.Players[0] : null;
        return new DpsCanvas.SessionSummary(
            DurationSec:    snap.ElapsedSeconds,
            TotalDamage:    snap.TotalPartyDamage,
            AverageDps:     snap.ElapsedSeconds > 0 ? (long)(snap.TotalPartyDamage / snap.ElapsedSeconds) : 0L,
            PeakDps:        _peakDpsThisSess,
            TopActorName:   top?.Name ?? "",
            TopActorDamage: top?.TotalDamage ?? 0,
            BossName:       _currentTarget?.Name);
    }

    private void CaptureTimelineSnapshot(DpsSnapshot snap, bool force = false)
    {
        if (snap.Players.Count == 0) return;
        double elapsed = snap.ElapsedSeconds > 0
            ? snap.ElapsedSeconds
            : (_sessionActive ? (DateTime.UtcNow - _sessionStartUtc).TotalSeconds : 0);
        int sec = Math.Max(1, (int)Math.Floor(elapsed));
        if (!force && sec <= _lastTimelineSec) return;

        var te = new TimelineEntry { T = sec };
        foreach (var p in snap.Players)
        {
            if (p.TotalDamage <= 0) continue;
            te.Players.Add(new TimelinePlayer
            {
                EntityId = p.EntityId,
                Damage = p.TotalDamage,
                Dps = elapsed > 0 ? (long)(p.TotalDamage / elapsed) : 0,
            });
        }
        if (te.Players.Count == 0) return;

        int existing = _timeline.FindIndex(t => t.T == sec);
        if (existing >= 0)
            _timeline[existing] = te;
        else
            _timeline.Add(te);
        if (sec > _lastTimelineSec)
            _lastTimelineSec = sec;
    }

    private void SaveRecord(DpsSnapshot snap)
    {
        if (snap.TotalPartyDamage <= 0) return;
        // Skip transient records (e.g. entity removal on zone entry before real fight).
        // Don't set _combatRecordSaved so the real kill record can still save later.
        if (snap.ElapsedSeconds < 2.0) return;
        if (_combatRecordSaved) return;
        _combatRecordSaved = true;
        CaptureTimelineSnapshot(snap, force: true);

        // Enrich snapshot players with CP/Score, skill levels, and server info before saving.
        foreach (var p in snap.Players)
        {
            string cleanName = StripServerSuffix(p.Name);
            int sid = p.ServerId;
            string sname = p.ServerName;
            int cp = p.CombatPower;
            int score = p.CombatScore;

            foreach (var pm in _party.SnapshotMembers())
            {
                if (string.Equals(StripServerSuffix(pm.Nickname), cleanName, StringComparison.Ordinal))
                {
                    if (cp == 0 && pm.CombatPower > 0) cp = pm.CombatPower;
                    if (pm.ServerId > 0 && sid == 0) { sid = pm.ServerId; sname = pm.ServerName; }
                    break;
                }
            }
            if (string.IsNullOrEmpty(sname) && sid > 0)
                sname = Protocol.ServerMap.GetName(sid);

            var apiData = Api.SkillLevelCache.Instance.Get(cleanName, sid);
            if (apiData != null)
            {
                if (cp == 0 && apiData.CombatPower > 0) cp = apiData.CombatPower;
                if (score == 0 && apiData.CombatScore > 0) score = apiData.CombatScore;
                if (apiData.SkillLevels is { Count: > 0 })
                    p.SkillLevels = apiData.SkillLevels;
            }

            p.CombatPower = cp;
            p.CombatScore = score;
            p.ServerId = sid;
            p.ServerName = sname;

            // Persist buff uptime into the snapshot so history replays show it.
            var buffs = _buffTracker.BuildSnapshot(p.EntityId, snap.ElapsedSeconds);
            if (buffs.Count > 0)
                p.Buffs = buffs.ConvertAll(b => new BuffUptimeDto { Name = b.Name, BuffId = b.BuffId, Uptime = b.Uptime, CasterEntityId = b.CasterEntityId });

            // Display name with server suffix for web upload.
            if (!string.IsNullOrEmpty(sname) && !p.Name.Contains('['))
                p.Name = $"{p.Name}[{sname}]";
        }

        // Mark the self player as the uploader so the server can build a stable
        // upload_key ("playerName:timestamp") instead of falling back to "unknown".
        int selfEntityId = _party.SelfEntityId ?? -1;
        foreach (var p in snap.Players)
            p.IsUploader = p.EntityId == selfEntityId;

        var record = new CombatRecord
        {
            Timestamp   = DateTime.Now,
            SessionId   = _sessionId,
            BossName    = _currentTarget?.Name,
            DurationSec = snap.ElapsedSeconds,
            TotalDamage = snap.TotalPartyDamage,
            // Average DPS = mean of party members' individual DPS (not total damage / time).
            AverageDps  = snap.Players.Count > 0 ? (long)snap.Players.Average(p => p.Dps) : 0,
            PeakDps     = _peakDpsThisSess,
            Snapshot    = snap,
            Timeline    = _timeline.Count > 0 ? new List<TimelineEntry>(_timeline) : null,
            HitLog      = _hitLog.Count > 0 ? new List<HitLogEntry>(_hitLog) : null,
            DungeonId   = IsDummy(_currentTarget?.Name) ? null : _dungeonId,
        };

        if (!_history.Save(record))
            return; // duplicate of previous record — skip upload too

        if (AppSettings.Instance.WebUploadEnabled)
        {
            _uploader.BaseUrl = AppSettings.Instance.WebUploadUrl;
            _uploader.UploadAsync(record);
        }
    }

    /// Matches A2Power ResetCombatStats(): clear damage but keep actor identities.
    private void ResetCombatStats()
    {
        if (_party.SelfEntityId is int selfId)
            _meter.ResetKeepSelf(selfId);
        else
            _meter.Reset();
        _currentTargetId = 0;
        _sessionActive = false;
        _combatRecordSaved = false;
        _sessionId = null;
        _peakDpsThisSess = 0;
        _peakByActor.Clear();
        _lastSummary = null;
        _lastRows = null;
        _buffTracker.Reset();
        _removedEntities.Clear();
        _maxHpCorrected = false;
        _timeline.Clear();
        _lastTimelineSec = -1;
        _hitLog.Clear();
        // Reset per-mob encounter state (A2Power: ResetMobEncounterState).
        foreach (var mob in _knownBosses.Values)
        {
            mob.HasSelfParticipation = false;
            mob.LastSelfHitAt = DateTime.MinValue;
            mob.DeathConfirmed = false;
            mob.TotalDamageReceived = 0;
            mob.HpAtLastSample = 0;
            mob.DamageAtLastHpSample = 0;
        }
    }

    /// Cumulative damage death detection (A2Power TryConfirmBossDeathByDamage).
    /// If accumulated damage since last HP sample exceeds the sampled HP, the
    /// boss is dead even without an explicit HP=0 packet.
    private static bool TryConfirmBossDeathByDamage(MobTarget mob)
    {
        if (mob.DeathConfirmed || mob.HpAtLastSample <= 0) return false;
        if (mob.TotalDamageReceived - mob.DamageAtLastHpSample < mob.HpAtLastSample) return false;
        mob.DeathConfirmed = true;
        mob.CurrentHp = 0;
        return true;
    }

    /// Correct MaxHp from first BossHp sample + accumulated damage (A2Power TryCorrectMaxHp).
    /// Called once per session when the first damage lands with a valid HP sample.
    private void TryCorrectMaxHp(MobTarget mob)
    {
        if (_maxHpCorrected || !mob.FirstBossHpSet) return;
        if (mob.TotalDamageReceived <= 0) return;
        _maxHpCorrected = true;
        long corrected = mob.FirstBossHpSample + mob.TotalDamageReceived;
        if (corrected != mob.MaxHp)
            mob.MaxHp = corrected;
    }

    /// Trigger API fetch for party members not yet enriched (A2Power RequestMissingActorLookups).
    private void RequestMissingActorLookups()
    {
        foreach (var pm in _party.SnapshotMembers())
        {
            if (!string.IsNullOrEmpty(pm.Nickname) && pm.ServerId > 0)
                Api.SkillLevelCache.Instance.EnsureLoaded(pm.Nickname, pm.ServerId);
        }
    }

    /// Public entry to map a stored snapshot for history display.
    public IReadOnlyList<DpsCanvas.PlayerRow> MapSnapshotForCanvas(DpsSnapshot snap)
        => MapForCanvas(snap.Players, snap.ElapsedSeconds, filterParty: false);

    private IReadOnlyList<DpsCanvas.PlayerRow> MapForCanvas(IReadOnlyList<ActorDps> players, double elapsedSec, bool filterParty = true)
    {
        var rows = new List<DpsCanvas.PlayerRow>(players.Count);
        foreach (var p in players)
        {
            // Skip unidentified actors (mobs/targets that received damage but never
            // had a UserInfo packet — they leak into the meter because we record by
            // entityId regardless of role).
            if (string.IsNullOrEmpty(p.Name) || p.Name.StartsWith('#') || p.JobCode < 0)
                continue;

            // Filter out non-self actors when no boss context exists.
            if (filterParty && _currentTarget is not { IsBoss: true, MaxHp: > 0 }
                && p.EntityId != (_party.SelfEntityId ?? -1))
                continue;

            IReadOnlyList<DpsCanvas.SkillBar>? skills = null;
            if (p.TopSkills is { Count: > 0 } && p.TotalDamage > 0)
            {
                var sb = new List<DpsCanvas.SkillBar>(p.TopSkills.Count);
                foreach (var s in p.TopSkills)
                {
                    if (s.Total <= 0) continue;
                    sb.Add(new DpsCanvas.SkillBar(
                        Name: s.Name,
                        Total: s.Total,
                        Hits: s.Hits,
                        CritRate: s.CritRate,
                        PercentOfActor: (double)s.Total / p.TotalDamage,
                        BackRate: s.BackRate,
                        StrongRate: s.StrongRate,
                        PerfectRate: s.PerfectRate,
                        MultiHitRate: s.MultiHitRate,
                        DodgeRate: s.DodgeRate,
                        BlockRate: s.BlockRate,
                        MaxHit: s.MaxHit,
                        Specs: s.Specs,
                        HitLog: s.HitLog));
                }
                skills = sb;
            }

            int cp = p.CombatPower;
            int score = p.CombatScore;
            int sid = p.ServerId;
            string sname = p.ServerName;
            string cleanName = StripServerSuffix(p.Name);
            // PartyTracker is keyed by CharacterId, not EntityId — look up by nickname.
            // Snapshot Values to avoid InvalidOperationException from concurrent Upsert.
            foreach (var pm in _party.SnapshotMembers())
            {
                if (string.Equals(StripServerSuffix(pm.Nickname), cleanName, StringComparison.Ordinal))
                {
                    if (cp == 0 && pm.CombatPower > 0) cp = pm.CombatPower;
                    if (pm.ServerId > 0 && sid == 0) { sid = pm.ServerId; sname = pm.ServerName; }
                    break;
                }
            }
            if (string.IsNullOrEmpty(sname) && sid > 0)
                sname = ServerMap.GetName(sid);

            // Enrich CP/Score from API cache if packet-derived values are 0.
            var apiData = Api.SkillLevelCache.Instance.Get(cleanName, sid);
            if (apiData != null)
            {
                if (cp == 0 && apiData.CombatPower > 0) cp = apiData.CombatPower;
                if (score == 0 && apiData.CombatScore > 0) score = apiData.CombatScore;
            }
            long peak = _peakByActor.TryGetValue(p.EntityId, out var pk) ? pk : p.Dps;
            long avg  = elapsedSec > 0 ? (long)(p.TotalDamage / elapsedSec) : p.Dps;

            // Contribution %: "boss" mode uses boss MaxHp as denominator, "party" uses total party damage.
            double pct = p.DamagePercent;
            if (string.Equals(Core.AppSettings.Instance.DpsPercentMode, "boss", StringComparison.OrdinalIgnoreCase)
                && _currentTarget is { IsBoss: true, MaxHp: > 0 })
            {
                pct = (double)p.TotalDamage / _currentTarget.MaxHp;
            }

            string displayName = !string.IsNullOrEmpty(sname) && !p.Name.Contains('[') ? $"{p.Name}[{sname}]" : p.Name;
            // Live tracker first; fall back to persisted snapshot (history replay).
            var buffs = _buffTracker.BuildSnapshot(p.EntityId, elapsedSec);
            if (buffs.Count == 0 && p.Buffs is { Count: > 0 })
                buffs = p.Buffs.ConvertAll(b => new BuffUptime(b.Name, b.BuffId, b.Uptime, b.CasterEntityId));
            rows.Add(new DpsCanvas.PlayerRow(
                Name:        displayName,
                JobIconKey:  JobCodeToKey(p.JobCode),
                Damage:      p.TotalDamage,
                Percent:     pct,
                DpsValue:    p.Dps,
                CritRate:    p.CritRate,
                HealTotal:   p.HealTotal,
                AccentColor: JobBarAccent(p.JobCode),
                IconColor:   JobAccent(p.JobCode),
                Skills:      skills,
                CombatPower: cp,
                CombatScore: score,
                PeakDps:     peak,
                AvgDps:      avg,
                DotDamage:   p.DotDamage,
                ServerId:    sid,
                ServerName:  sname,
                SkillLevels: p.SkillLevels,
                Buffs:       buffs.Count > 0 ? buffs : null));
        }

        // ── Merge rows with identical display name (same player, different entity IDs) ──
        if (rows.Count > 1)
        {
            var deduped = new List<DpsCanvas.PlayerRow>(rows.Count);
            var nameIdx = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (nameIdx.TryGetValue(row.Name, out int idx))
                {
                    var prev = deduped[idx];
                    bool pickNew = row.Damage > prev.Damage;
                    long dmg = prev.Damage + row.Damage;
                    deduped[idx] = prev with
                    {
                        Damage      = dmg,
                        Percent     = prev.Percent + row.Percent,
                        DpsValue    = elapsedSec > 0 ? (long)(dmg / elapsedSec) : prev.DpsValue + row.DpsValue,
                        AvgDps      = elapsedSec > 0 ? (long)(dmg / elapsedSec) : prev.AvgDps + row.AvgDps,
                        PeakDps     = Math.Max(prev.PeakDps, row.PeakDps),
                        HealTotal   = prev.HealTotal + row.HealTotal,
                        DotDamage   = prev.DotDamage + row.DotDamage,
                        CritRate    = pickNew ? row.CritRate : prev.CritRate,
                        CombatPower = Math.Max(prev.CombatPower, row.CombatPower),
                        CombatScore = Math.Max(prev.CombatScore, row.CombatScore),
                        Skills      = pickNew ? (row.Skills ?? prev.Skills) : (prev.Skills ?? row.Skills),
                        Buffs       = pickNew ? (row.Buffs ?? prev.Buffs) : (prev.Buffs ?? row.Buffs),
                        SkillLevels = pickNew ? (row.SkillLevels ?? prev.SkillLevels) : (prev.SkillLevels ?? row.SkillLevels),
                    };
                }
                else
                {
                    nameIdx[row.Name] = deduped.Count;
                    deduped.Add(row);
                }
            }
            rows = deduped;
        }

        // Append detected party members who have no combat data yet as placeholder rows.
        // existingNames uses clean nicknames (without [서버명]) to avoid mismatch.
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows) existingNames.Add(StripServerSuffix(r.Name));

        foreach (var pm in _party.SnapshotMembers())
        {
            if (string.IsNullOrEmpty(pm.Nickname)) continue;
            string cleanNick = StripServerSuffix(pm.Nickname);
            if (existingNames.Contains(cleanNick)) continue;
            if (!pm.IsSelf && !pm.IsPartyMember) continue;
            existingNames.Add(cleanNick);

            int pmCp = pm.CombatPower;
            int pmScore = 0;
            int pmSid = pm.ServerId;
            string pmSname = pm.ServerName;
            if (string.IsNullOrEmpty(pmSname) && pmSid > 0)
                pmSname = ServerMap.GetName(pmSid);

            var pmApi = Api.SkillLevelCache.Instance.Get(pm.Nickname, pmSid);
            if (pmApi != null)
            {
                if (pmCp == 0 && pmApi.CombatPower > 0) pmCp = pmApi.CombatPower;
                if (pmScore == 0 && pmApi.CombatScore > 0) pmScore = pmApi.CombatScore;
            }

            string pmDisplayName = !string.IsNullOrEmpty(pmSname) && !pm.Nickname.Contains('[') ? $"{pm.Nickname}[{pmSname}]" : pm.Nickname;
            rows.Add(new DpsCanvas.PlayerRow(
                Name:        pmDisplayName,
                JobIconKey:  JobCodeToKey(pm.JobCode),
                Damage:      0,
                Percent:     0,
                DpsValue:    0,
                CritRate:    0,
                HealTotal:   0,
                AccentColor: JobBarAccent(pm.JobCode),
                IconColor:   JobAccent(pm.JobCode),
                CombatPower: pmCp,
                CombatScore: pmScore,
                ServerId:    pmSid,
                ServerName:  pmSname));
        }

        return rows;
    }

    private static bool IsDummy(string? name)
        => name != null && (name.Contains("허수아비") || name.Contains("샌드백"));

    private static string FormatTimer(double seconds)
    {
        var s = (int)Math.Max(0, seconds);
        return $"{s / 60}:{s % 60:00}";
    }

    private static string StripServerSuffix(string name)
    {
        int idx = name.IndexOf('[');
        return idx > 0 ? name[..idx] : name;
    }

    private static string JobCodeToKey(int gameCode) => JobMapping.GameToJobName(gameCode);

    /// Color palette indexed by UI archetype (0..7 from JobMapping.GameToUi).
    /// Matches the original A2Viewer web overlay palette.
    private static readonly D2DColor[] UiAccents = new[]
    {
        new D2DColor(0.525f, 0.867f, 0.953f, 1f), // 0 검성   #86DDF3
        new D2DColor(0.384f, 0.694f, 0.561f, 1f), // 1 궁성   #62B18F
        new D2DColor(0.718f, 0.549f, 0.949f, 1f), // 2 마도성  #B78CF2
        new D2DColor(0.643f, 0.906f, 0.608f, 1f), // 3 살성   #A4E79B
        new D2DColor(0.490f, 0.627f, 0.976f, 1f), // 4 수호성  #7DA0F9
        new D2DColor(0.812f, 0.420f, 0.816f, 1f), // 5 정령성  #CF6BD0
        new D2DColor(0.906f, 0.812f, 0.490f, 1f), // 6 치유성  #E7CF7D
        new D2DColor(0.894f, 0.647f, 0.357f, 1f), // 7 호법성  #E4A55B
    };

    private static D2DColor JobAccent(int gameCode)
    {
        int ui = JobMapping.GameToUiIndex(gameCode);
        if (ui < 0 || ui >= UiAccents.Length) return new D2DColor(0.70f, 0.70f, 0.70f, 1f);
        return UiAccents[ui];
    }

    /// Bar fill color — reads user-configured hex from settings.
    private static D2DColor JobBarAccent(int gameCode)
    {
        string jobName = JobMapping.GameToJobName(gameCode);
        if (string.IsNullOrEmpty(jobName)) return new D2DColor(0.70f, 0.70f, 0.70f, 1f);
        string hex = Core.AppSettings.Instance.JobBarColors.GetHex(jobName);
        var c = Core.AppSettings.ThemeColors.ParseHex(hex);
        return new D2DColor(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }

    public void Dispose()
    {
        _pushTimer.Dispose();
        _source?.Dispose();
        _uploader.Dispose();
    }
}
