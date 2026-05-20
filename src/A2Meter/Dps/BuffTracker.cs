using System;
using System.Collections.Generic;
using System.Linq;
using A2Meter.Dps.Protocol;

namespace A2Meter.Dps;

/// Tracks buff uptime per entity across a combat session.
/// Uses duration-based expiration tracking (matching A2Power).
internal sealed class BuffTracker
{
    private readonly SkillDatabase _skills;

    /// Per-entity → per-buff tracking data.
    private readonly Dictionary<int, Dictionary<int, BuffInfo>> _buffs = new();
    private readonly object _lock = new();

    private const uint MAX_REASONABLE_DURATION_MS = 3_600_000; // 1 hour

    public BuffTracker(SkillDatabase? skills = null)
    {
        _skills = skills ?? SkillDatabase.Shared;
    }

    public void Reset()
    {
        lock (_lock) _buffs.Clear();
    }

    // Start/Stop are no-ops — tracking is always active once events arrive.
    public void Start() { }

    /// Record a buff event.
    public void OnBuff(int entityId, int buffId, int type, uint durationMs, long timestamp)
    {
        // Filter: permanent, zero-length, and unreasonably long buffs are ignored.
        if (durationMs == 0 || durationMs == uint.MaxValue || durationMs > MAX_REASONABLE_DURATION_MS)
            return;

        // Resolve buff to a known skill code.
        int resolved = ResolveSkillCode(buffId);
        if (resolved < 0) return;

        lock (_lock)
        {
            if (!_buffs.TryGetValue(entityId, out var entityBuffs))
            {
                entityBuffs = new Dictionary<int, BuffInfo>();
                _buffs[entityId] = entityBuffs;
            }

            var now = DateTime.UtcNow;
            double durationSec = durationMs / 1000.0;

            if (entityBuffs.TryGetValue(resolved, out var info))
            {
                // Buff reapplication: if the previous one expired, accumulate its duration.
                if (now >= info.ExpiresAt)
                {
                    info.AccumulatedSec += (info.ExpiresAt - info.StartedAt).TotalSeconds;
                    info.StartedAt = now;
                }
                // Extend (or refresh) expiration.
                info.ExpiresAt = now + TimeSpan.FromSeconds(durationSec);
            }
            else
            {
                entityBuffs[resolved] = new BuffInfo
                {
                    StartedAt = now,
                    ExpiresAt = now + TimeSpan.FromSeconds(durationSec),
                    AccumulatedSec = 0,
                };
            }
        }
    }

    /// Build uptime snapshot for a given entity.
    public List<BuffUptime> BuildSnapshot(int entityId, double elapsedSeconds)
    {
        var result = new List<BuffUptime>();
        if (elapsedSeconds <= 0) return result;

        KeyValuePair<int, BuffInfo>[] entries;
        lock (_lock)
        {
            if (!_buffs.TryGetValue(entityId, out var entityBuffs)) return result;
            entries = entityBuffs.ToArray();
        }

        var now = DateTime.UtcNow;

        foreach (var (buffId, info) in entries)
        {
            double sec = info.AccumulatedSec;
            // Add the current (possibly still active) window.
            if (now < info.ExpiresAt)
                sec += (now - info.StartedAt).TotalSeconds;
            else
                sec += (info.ExpiresAt - info.StartedAt).TotalSeconds;

            if (sec <= 0) continue;

            double uptime = Math.Min(1.0, sec / elapsedSeconds);
            string name = _skills.GetSkillName(buffId) ?? $"버프#{buffId}";

            // Merge entries with the same resolved name (keep highest uptime).
            bool merged = false;
            for (int i = 0; i < result.Count; i++)
            {
                if (result[i].Name == name)
                {
                    if (uptime > result[i].Uptime)
                        result[i] = new BuffUptime(name, buffId, uptime);
                    merged = true;
                    break;
                }
            }
            if (!merged)
                result.Add(new BuffUptime(name, buffId, uptime));
        }

        result.Sort((a, b) => b.Uptime.CompareTo(a.Uptime));
        return result;
    }

    /// Build uptime for all tracked entities.
    public Dictionary<int, List<BuffUptime>> BuildAllSnapshots(double elapsedSeconds)
    {
        int[] entityIds;
        lock (_lock) { entityIds = _buffs.Keys.ToArray(); }

        var result = new Dictionary<int, List<BuffUptime>>();
        foreach (var entityId in entityIds)
        {
            var snap = BuildSnapshot(entityId, elapsedSeconds);
            if (snap.Count > 0) result[entityId] = snap;
        }
        return result;
    }

    /// Resolve a raw buffId to a known skill code, matching A2Power logic:
    ///   buffId → buffId/10 → buffId/10000*10000
    private int ResolveSkillCode(int buffId)
    {
        if (_skills.GetSkillName(buffId) != null)
            return buffId;

        int stripped = (buffId >= 100_000_000 && buffId <= 999_999_999)
            ? buffId / 10
            : buffId;

        if (stripped != buffId && _skills.GetSkillName(stripped) != null)
            return stripped;

        int baseCode = stripped / 10000 * 10000;
        if (baseCode != stripped && _skills.GetSkillName(baseCode) != null)
            return baseCode;

        // Also check the buff database directly.
        if (_skills.IsKnownBuffCode(buffId))
            return buffId;

        return -1;
    }

    private sealed class BuffInfo
    {
        public DateTime StartedAt;
        public DateTime ExpiresAt;
        public double AccumulatedSec;
    }
}

internal readonly record struct BuffUptime(string Name, int BuffId, double Uptime);
