using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace A2Meter.Dps;

/// Aggregates damage events. Stored two ways:
///   * (target, actor) buckets — for boss-scoped views (matches the original
///     A2Power "single pull = single session" semantics)
///   * actor-only roll-up (sum across all targets) — for the legacy snapshot
///
/// Heals are tracked per-actor since they don't really belong to any single
/// target.
internal sealed class DpsMeter
{
    private readonly Dictionary<int, ActorAccum> _actors = new();
    private readonly Dictionary<int, Dictionary<int, ActorAccum>> _byTarget = new();
    private MobTarget? _target;

    // ── Elapsed time tracking (A2Power model: freezes at last hit) ──
    private bool     _isRunning;
    private DateTime _combatStart;
    private DateTime _lastDamageTime;
    private double   _accumulatedSec;   // accumulated across Stop/Start cycles
    private readonly Stopwatch _wallSw = Stopwatch.StartNew();

    public bool IsRunning => _isRunning;

    public void Start()
    {
        if (!_isRunning)
        {
            _isRunning = true;
            _combatStart = DateTime.UtcNow;
            _lastDamageTime = _combatStart;
        }
    }

    public void Stop()
    {
        if (_isRunning)
        {
            _accumulatedSec += (_lastDamageTime - _combatStart).TotalSeconds;
            _isRunning = false;
        }
        _wallSw.Stop();
    }

    public void Reset()
    {
        _actors.Clear();
        _byTarget.Clear();
        _target = null;
        _isRunning = false;
        _accumulatedSec = 0;
        _wallSw.Restart();
    }

    /// Reset but keep self actor's identity (name/jobCode) so the row persists.
    public void ResetKeepSelf(int selfEntityId)
    {
        ActorAccum? self = null;
        if (_actors.TryGetValue(selfEntityId, out var s))
            self = new ActorAccum { EntityId = selfEntityId, Name = s.Name, JobCode = s.JobCode };

        _actors.Clear();
        _byTarget.Clear();
        _target = null;
        _isRunning = false;
        _accumulatedSec = 0;
        _wallSw.Restart();

        if (self != null)
            _actors[selfEntityId] = self;
    }

    public void RecordHit(int actorId, int targetId, string name, int jobCode, long damage, uint hitFlags, bool isHeal,
                          string? skillName = null, int extraHits = 0, bool isDot = false, int[]? specs = null)
    {
        var actor = GetOrCreate(_actors, actorId, name, jobCode);
        UpdateIdentity(actor, name, jobCode);
        if (!_isRunning) Start();
        _lastDamageTime = DateTime.UtcNow;

        Apply(actor, damage, hitFlags, isHeal, skillName, extraHits, isDot, specs);

        // Per-target bucket for boss-scoped views. Skip for heals — heals don't
        // belong to a damage target. We still track them on the actor roll-up.
        if (!isHeal && targetId != 0)
        {
            if (!_byTarget.TryGetValue(targetId, out var perActor))
            {
                perActor = new Dictionary<int, ActorAccum>();
                _byTarget[targetId] = perActor;
            }
            var slot = GetOrCreate(perActor, actorId, name, jobCode);
            UpdateIdentity(slot, name, jobCode);
            Apply(slot, damage, hitFlags, isHeal: false, skillName, extraHits, isDot, specs);
        }
    }

    public void SetTarget(MobTarget? target) => _target = target;

    /// Roll-up across all targets (party-wide damage view).
    public DpsSnapshot BuildCurrentSnapshot()
        => Build(_actors.Values.ToArray(), target: _target);

    /// Damage scoped to one target id only — matches the original A2Power
    /// "boss bar" semantics. Returns an empty snapshot if no damage is
    /// recorded against that target yet.
    public DpsSnapshot BuildTargetSnapshot(int targetId)
    {
        if (targetId == 0 || !_byTarget.TryGetValue(targetId, out var perActor))
            return Build(Array.Empty<ActorAccum>(), _target);

        // Heal totals are pulled from the global actor roll-up so a healer's
        // contribution still shows on a boss-scoped row.
        // Snapshot to avoid concurrent modification from packet thread.
        var perActorSnap = perActor.ToArray();
        foreach (var (actorId, acc) in perActorSnap)
            acc.HealTotal = _actors.TryGetValue(actorId, out var g) ? g.HealTotal : 0;

        return Build(perActorSnap.Select(kv => kv.Value), _target);
    }

    private DpsSnapshot Build(IEnumerable<ActorAccum> actors, MobTarget? target)
    {
        // Elapsed freezes at last hit time (A2Power: _lastDamageTime - _combatStart).
        double elapsed = _isRunning
            ? _accumulatedSec + (_lastDamageTime - _combatStart).TotalSeconds
            : _accumulatedSec;
        double wallElapsed = _wallSw.Elapsed.TotalSeconds;

        var list = actors.ToList();
        long totalParty = list.Sum(a => a.TotalDamage);

        var players = list
            .Select(a => new ActorDps
            {
                EntityId      = a.EntityId,
                Name          = a.Name,
                JobCode       = a.JobCode,
                TotalDamage   = a.TotalDamage,
                HealTotal     = a.HealTotal,
                Dps           = (long)(a.TotalDamage / Math.Max(1.0, elapsed)),
                PartyDps      = (long)(a.TotalDamage / Math.Max(1.0, elapsed)),
                WallDps       = (long)(a.TotalDamage / Math.Max(1.0, wallElapsed)),
                DamagePercent = totalParty == 0 ? 0 : (double)a.TotalDamage / totalParty,
                CritRate      = a.Hits == 0 ? 0 : (double)a.Crits / a.Hits,
                Hits          = a.Hits,
                DotDamage     = a.DotDamage,
                TopSkills     = a.Skills.Values
                                 .OrderByDescending(s => s.Total)
                                 .Select(s => new SkillDps
                                 {
                                     Name         = s.Name,
                                     Total        = s.Total,
                                     Hits         = s.Hits,
                                     MaxHit       = s.MaxHit,
                                     CritRate     = s.Hits == 0 ? 0 : (double)s.Crits / s.Hits,
                                     BackRate     = s.Hits == 0 ? 0 : (double)s.Backs / s.Hits,
                                     StrongRate   = s.Hits == 0 ? 0 : (double)s.HardHits / s.Hits,
                                     PerfectRate  = s.Hits == 0 ? 0 : (double)s.Perfects / s.Hits,
                                     MultiHitRate = s.Hits == 0 ? 0 : (double)s.MultiHits / s.Hits,
                                     DodgeRate    = s.Hits == 0 ? 0 : (double)s.Evades / s.Hits,
                                     BlockRate    = s.Hits == 0 ? 0 : (double)s.Blocks / s.Hits,
                                     Specs        = s.Specs,
                                     HitLog       = s.HitLog,
                                 }).ToList(),
            })
            .OrderByDescending(p => p.TotalDamage)
            .ToList();

        return new DpsSnapshot
        {
            ElapsedSeconds     = elapsed,
            WallElapsedSeconds = wallElapsed,
            TotalPartyDamage   = totalParty,
            Target             = target,
            Players            = players,
        };
    }

    private static ActorAccum GetOrCreate(Dictionary<int, ActorAccum> map, int actorId, string name, int jobCode)
    {
        if (!map.TryGetValue(actorId, out var a))
        {
            a = new ActorAccum { EntityId = actorId, Name = name, JobCode = jobCode };
            map[actorId] = a;
        }
        return a;
    }

    private static void UpdateIdentity(ActorAccum a, string name, int jobCode)
    {
        if (!string.IsNullOrEmpty(name) && (string.IsNullOrEmpty(a.Name) || a.Name.StartsWith('#')))
        {
            a.Name = name;
            a.JobCode = jobCode;
        }
    }

    private static void Apply(ActorAccum a, long damage, uint hitFlags, bool isHeal, string? skillName, int extraHits, bool isDot, int[]? specs = null)
    {
        if (isHeal)
        {
            a.HealTotal += damage;
            return; // Original: heals don't enter TryAccumulateDamage → no hit/skill stats.
        }

        a.TotalDamage += damage;

        // DoT ticks: add damage only — don't count as hits or pollute stat rates.
        if (isDot)
        {
            a.DotDamage += damage;
            if (!string.IsNullOrEmpty(skillName))
            {
                if (!a.Skills.TryGetValue(skillName, out var sd))
                {
                    sd = new SkillAccum { Name = skillName };
                    a.Skills[skillName] = sd;
                }
                sd.Total += damage;
                sd.HitLog.Add(damage);
            }
            return;
        }

        // Per-event: always +1, matching original.
        a.Hits++;
        bool isCrit = (hitFlags & 0x100) != 0;
        if (isCrit) a.Crits++;

        if (!string.IsNullOrEmpty(skillName))
        {
            if (!a.Skills.TryGetValue(skillName, out var s))
            {
                s = new SkillAccum { Name = skillName };
                a.Skills[skillName] = s;
            }
            s.Total += damage;
            s.Hits++;
            s.HitLog.Add(damage);
            if (damage > s.MaxHit)       s.MaxHit = damage;
            if (isCrit)                  s.Crits++;
            if ((hitFlags & 0x01) != 0)  s.Backs++;
            if ((hitFlags & 0x10) != 0)  s.HardHits++;
            if ((hitFlags & 0x08) != 0)  s.Perfects++;
            if ((hitFlags & 0x06) != 0)  s.Blocks++;
            if (damage == 0)             s.Evades++;
            if (extraHits >= 1)          s.MultiHits++;
            if (specs != null && (s.Specs == null || specs.Length > s.Specs.Length))
                s.Specs = specs;
        }
    }

    private sealed class ActorAccum
    {
        public int    EntityId;
        public string Name = "";
        public int    JobCode = -1;
        public long   TotalDamage;
        public long   DotDamage;
        public long   HealTotal;
        public long   Hits;
        public long   Crits;
        public Dictionary<string, SkillAccum> Skills = new();
    }

    private sealed class SkillAccum
    {
        public string Name = "";
        public long Total;
        public long Hits;
        public long Crits;
        public long Backs;
        public long HardHits;
        public long Perfects;
        public long Blocks;
        public long Evades;
        public long MultiHits;
        public long MaxHit;
        public int[]? Specs;
        public List<long> HitLog = new();
    }
}
