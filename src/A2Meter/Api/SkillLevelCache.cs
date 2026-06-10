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
    private static readonly SemaphoreSlim FetchGate = new(1, 1);

    public event Action? DataUpdated;

    /// Key: "nickname:serverId" → fetched data.
    private readonly ConcurrentDictionary<string, CharacterSkillData> _cache = new();

    /// Prevents duplicate in-flight fetches for the same character.
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    /// Last attempted fetch time. Lets visible party rows retry without flooding the API.
    private readonly ConcurrentDictionary<string, DateTime> _lastAttempt = new();

    /// Try get cached skill data for a character.
    public CharacterSkillData? Get(string nickname, int serverId)
    {
        var identity = PlayncClient.NormalizeCharacterQuery(nickname, serverId);
        if (identity.ServerId <= 0 || string.IsNullOrWhiteSpace(identity.Name)) return null;
        string key = BuildKey(identity.Name, identity.ServerId);
        return _cache.TryGetValue(key, out var data) ? data : null;
    }

    /// Look up a specific skill level. Returns 0 if not cached or not found.
    public int GetSkillLevel(string nickname, int serverId, string skillName)
    {
        var data = Get(nickname, serverId);
        if (data?.SkillLevels == null) return 0;
        return data.SkillLevels.TryGetValue(skillName, out var lv) ? lv : 0;
    }

    public void Store(string nickname, int serverId, CharacterSkillData data)
    {
        if (data == null || string.IsNullOrWhiteSpace(nickname)) return;
        int sid = data.ServerId > 0 ? data.ServerId : serverId;
        var identity = PlayncClient.NormalizeCharacterQuery(nickname, sid, data.ServerName);
        if (identity.ServerId <= 0 || string.IsNullOrWhiteSpace(identity.Name)) return;
        StoreUnderKey(identity.Name, identity.ServerId, data);

        var requested = PlayncClient.NormalizeCharacterQuery(nickname, serverId, data.ServerName);
        if (requested.ServerId > 0 && !string.IsNullOrWhiteSpace(requested.Name))
            StoreUnderKey(requested.Name, requested.ServerId, data);

        RaiseDataUpdated();
    }

    /// Trigger an async fetch if not already cached or in-flight.
    /// Uses profile data as an immediate fallback, then the full score engine.
    public void EnsureLoaded(string nickname, int serverId, bool immediateFallback = false)
    {
        if (string.IsNullOrWhiteSpace(nickname)) return;

        var identity = PlayncClient.NormalizeCharacterQuery(nickname, serverId);
        if (identity.ServerId <= 0) return;
        string cleanName = identity.Name;
        if (string.IsNullOrWhiteSpace(cleanName)) return;

        string key = BuildKey(cleanName, identity.ServerId);
        if (_cache.ContainsKey(key)) return;
        var now = DateTime.UtcNow;
        if (!immediateFallback && _lastAttempt.TryGetValue(key, out var lastAttempt) && now - lastAttempt < RetryDelay)
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
                    if (immediateFallback)
                    {
                        var fallback = await PlayncClient.FetchCharacterData(cleanName, identity.ServerId).ConfigureAwait(false);
                        if (fallback is { CombatScore: > 0 })
                        {
                            Store(cleanName, identity.ServerId, fallback);
                            return;
                        }
                        if (fallback != null && IsUsable(fallback))
                            Store(cleanName, identity.ServerId, fallback);
                    }

                    var scoreResult = await Calc.CombatScore.QueryCombatScore(identity.ServerId, cleanName).ConfigureAwait(false);
                    if (scoreResult != null && scoreResult.Score > 0)
                    {
                        Store(cleanName, identity.ServerId, new CharacterSkillData
                        {
                            CharacterId = scoreResult.CharacterId,
                            ServerId = scoreResult.ServerId,
                            ServerName = scoreResult.ServerName,
                            CombatPower = scoreResult.CombatPower,
                            CombatScore = scoreResult.Score,
                            SkillLevels = scoreResult.SkillLevels,
                            DpSkills = scoreResult.DpSkills,
                        });
                    }
                    else
                    {
                        var fallback = await PlayncClient.FetchCharacterData(cleanName, identity.ServerId).ConfigureAwait(false);
                        if (fallback != null && IsUsable(fallback))
                            Store(cleanName, identity.ServerId, fallback);
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

    private void RaiseDataUpdated()
    {
        try { DataUpdated?.Invoke(); }
        catch { }
    }

    private void StoreUnderKey(string nickname, int serverId, CharacterSkillData data)
    {
        string key = BuildKey(nickname, serverId);
        _cache[key] = data;
        _lastAttempt.TryRemove(key, out _);
        _pending.TryRemove(key, out _);
    }

    private static bool IsUsable(CharacterSkillData? data)
        => data != null
           && (data.CombatScore > 0
               || data.CombatPower > 0
               || data.SkillLevels.Count > 0
               || data.DpSkills.Count > 0);

    private static string BuildKey(string nickname, int serverId) => $"{nickname}:{serverId}";
}
