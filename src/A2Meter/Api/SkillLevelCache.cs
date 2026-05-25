using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace A2Meter.Api;

/// Caches per-character skill level data fetched from the Plaync API.
/// Thread-safe singleton; lookups are non-blocking (returns cached or null).
internal sealed class SkillLevelCache
{
    private static readonly Lazy<SkillLevelCache> _instance = new(() => new());
    public static SkillLevelCache Instance => _instance.Value;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);
    private static readonly SemaphoreSlim FetchGate = new(2, 2);

    /// Key: "nickname:serverId" → fetched data.
    private readonly ConcurrentDictionary<string, CharacterSkillData> _cache = new();

    /// Prevents duplicate in-flight fetches for the same character.
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    /// Last attempted fetch time. Lets visible party rows retry without flooding the API.
    private readonly ConcurrentDictionary<string, DateTime> _lastAttempt = new();

    /// Try get cached skill data for a character.
    public CharacterSkillData? Get(string nickname, int serverId)
    {
        string key = BuildKey(StripServerSuffix(nickname), serverId);
        return _cache.TryGetValue(key, out var data) ? data : null;
    }

    /// Look up a specific skill level. Returns 0 if not cached or not found.
    public int GetSkillLevel(string nickname, int serverId, string skillName)
    {
        var data = Get(nickname, serverId);
        if (data?.SkillLevels == null) return 0;
        return data.SkillLevels.TryGetValue(skillName, out var lv) ? lv : 0;
    }

    /// Trigger an async fetch if not already cached or in-flight.
    /// Uses the full CombatScore calculation engine (same as original A2Viewer).
    public void EnsureLoaded(string nickname, int serverId)
    {
        if (serverId <= 0 || string.IsNullOrWhiteSpace(nickname)) return;

        // Strip [서버명] suffix if present (e.g. "남힐[네자칸]" → "남힐")
        string cleanName = StripServerSuffix(nickname);
        if (string.IsNullOrWhiteSpace(cleanName)) return;

        string key = BuildKey(cleanName, serverId);
        if (_cache.ContainsKey(key)) return;
        var now = DateTime.UtcNow;
        if (_lastAttempt.TryGetValue(key, out var lastAttempt) && now - lastAttempt < RetryDelay)
            return;
        if (!_pending.TryAdd(key, 0)) return; // already fetching
        _lastAttempt[key] = now;

        _ = Task.Run(async () =>
        {
            try
            {
                await FetchGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var scoreResult = await Calc.CombatScore.QueryCombatScore(serverId, cleanName).ConfigureAwait(false);
                    if (scoreResult != null)
                    {
                        _cache[key] = new CharacterSkillData
                        {
                            CombatPower = scoreResult.CombatPower,
                            CombatScore = scoreResult.Score,
                            SkillLevels = scoreResult.SkillLevels,
                        };
                        _lastAttempt.TryRemove(key, out _);
                    }
                }
                finally
                {
                    FetchGate.Release();
                }
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"[SkillCache] {cleanName}: {ex.Message}");
            }
            finally
            {
                _pending.TryRemove(key, out _);
            }
        });
    }

    private static string StripServerSuffix(string name)
    {
        int idx = name.IndexOf('[');
        return idx > 0 ? name[..idx] : name;
    }

    private static string BuildKey(string nickname, int serverId) => $"{nickname}:{serverId}";
}
