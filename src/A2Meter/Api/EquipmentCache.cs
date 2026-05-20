using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using A2Meter.Dps;
using A2Meter.Dps.Protocol;

namespace A2Meter.Api;

/// Caches equipment data parsed from CharacterLookup packets (0x4F 0x36).
/// When available, this data can be used BEFORE (or instead of) making
/// individual FetchItem API calls, providing instant sub-stat visibility.
internal sealed class EquipmentCache
{
    private static readonly Lazy<EquipmentCache> _instance = new(() => new());
    public static EquipmentCache Instance => _instance.Value;

    /// Key: entityId → list of parsed equipment items.
    private readonly ConcurrentDictionary<int, CachedEquipment> _byEntity = new();

    /// Key: "nickname:serverId" → list of parsed equipment items.
    private readonly ConcurrentDictionary<string, CachedEquipment> _byName = new();

    /// Store equipment data from a CharacterLookup packet.
    public void Store(int entityId, string? nickname, int serverId, List<EquipmentItem> items)
    {
        var cached = new CachedEquipment
        {
            EntityId = entityId,
            Items = items,
            Timestamp = DateTime.UtcNow,
        };
        _byEntity[entityId] = cached;
        if (!string.IsNullOrEmpty(nickname) && serverId > 0)
            _byName[BuildKey(nickname, serverId)] = cached;
    }

    /// Get cached equipment by nickname + serverId.
    public CachedEquipment? Get(string nickname, int serverId)
    {
        if (string.IsNullOrEmpty(nickname) || serverId <= 0) return null;
        return _byName.TryGetValue(BuildKey(nickname, serverId), out var c) ? c : null;
    }

    /// Get cached equipment by entityId.
    public CachedEquipment? GetByEntity(int entityId)
    {
        return _byEntity.TryGetValue(entityId, out var c) ? c : null;
    }

    /// Convert a packet EquipmentStat to the format expected by the calc engine.
    /// Returns an anonymous object matching AdaptStatArray's output shape:
    ///   { id: "AmplifyWeaponDamage", name: "AmplifyWeaponDamage", value: 12.57, extra: 0, minValue: 0, exceed: false }
    public static List<object> ToCalcSubStats(List<EquipmentStat>? stats)
    {
        var result = new List<object>();
        if (stats == null) return result;
        foreach (var s in stats)
        {
            string name = StatMapping.GetName(s.StatId) ?? $"Stat{s.StatId}";
            // Percentage stats (value > 500) stored as ×100. Calc engine expects the actual percentage.
            double value = s.Value > 500 ? s.Value / 100.0 : s.Value;
            result.Add(new
            {
                id = name,
                name = name,
                value = value,
                extra = 0,
                minValue = 0,
                exceed = false,
            });
        }
        return result;
    }

    /// Serialize the cached equipment for a character into the JSON format
    /// expected by CombatScore.BuildJsInput (array of item objects).
    /// Only sub_stats are populated from the packet; main_stats is empty
    /// (those are server-computed and require the API).
    public static string ToEquipmentJson(List<EquipmentItem> items)
    {
        var list = new List<object>();
        foreach (var item in items)
        {
            list.Add(new
            {
                slotPos = 0,  // unknown from packet
                name = $"Item{item.ItemId}",
                enchantLevel = item.EnchantLevel,
                enhance_level = item.EnchantLevel,
                exceedLevel = 0,
                exceed_level = 0,
                main_stats = new List<object>(), // not available from packet
                sub_stats = ToCalcSubStats(item.SubStats),
                magic_stone_stat = new List<object>(), // partial (count known, values unknown)
            });
        }
        return JsonSerializer.Serialize(list);
    }

    private static string BuildKey(string nickname, int serverId) => $"{nickname}:{serverId}";
}

internal sealed class CachedEquipment
{
    public int EntityId { get; set; }
    public List<EquipmentItem> Items { get; set; } = new();
    public DateTime Timestamp { get; set; }
}
