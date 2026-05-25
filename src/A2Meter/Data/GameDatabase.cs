using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace A2Meter.Data;

/// Provides read-only access to the unified game_db.sqlite.
/// Replaces the JSON-based SkillDatabase for lookups and adds
/// item/enchant/daevanion queries needed by the calc engine.
internal sealed class GameDatabase : IDisposable
{
    private static readonly Lazy<GameDatabase> _instance = new(() => new GameDatabase());
    public static GameDatabase Instance => _instance.Value;

    private readonly SqliteConnection? _conn;

    private GameDatabase()
    {
        var path = DataManager.DatabasePath;
        if (!File.Exists(path)) return;

        _conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        _conn.Open();
    }

    public bool IsAvailable => _conn != null;

    public void Dispose()
    {
        _conn?.Dispose();
    }

    // ── Skills / Buffs / Mobs / Dungeons ────────────────────────────────────

    public string? GetSkillName(int code)
    {
        if (_conn == null) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM skills WHERE code = @c";
        cmd.Parameters.AddWithValue("@c", code);
        return cmd.ExecuteScalar() as string;
    }

    public string? GetBuffName(int code)
    {
        if (_conn == null) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM buffs WHERE code = @c";
        cmd.Parameters.AddWithValue("@c", code);
        return cmd.ExecuteScalar() as string;
    }

    public string? GetMobName(int code)
    {
        if (_conn == null) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM mobs WHERE code = @c";
        cmd.Parameters.AddWithValue("@c", code);
        return cmd.ExecuteScalar() as string;
    }

    public string? GetDungeonName(int code)
    {
        if (_conn == null) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM dungeons WHERE code = @c";
        cmd.Parameters.AddWithValue("@c", code);
        return cmd.ExecuteScalar() as string;
    }

    public DungeonInfo? GetDungeonInfo(int code)
    {
        var name = GetDungeonName(code);
        if (string.IsNullOrEmpty(name)) return null;
        return new DungeonInfo
        {
            Id = code,
            AssetName = name,
            BaseName = BaseNameOf(name),
            Tier = TierOfDungeonName(name),
        };
    }

    public List<DungeonBossInfo> GetDungeonBosses(int dungeonId)
    {
        var result = new List<DungeonBossInfo>();
        if (_conn == null) return result;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT db.ord, m.code, m.name, m.level, m.grade, m.is_final_boss
              FROM dungeon_bosses db
              JOIN mobs m ON m.code = db.mob_code
             WHERE db.dungeon_id = @d
             ORDER BY db.ord";
        cmd.Parameters.AddWithValue("@d", dungeonId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new DungeonBossInfo
            {
                Order = r.GetInt32(0),
                MobId = r.GetInt32(1),
                Name = r.GetString(2),
                Level = r.IsDBNull(3) ? null : r.GetInt32(3),
                Grade = r.IsDBNull(4) ? null : r.GetInt32(4),
                IsFinalBoss = r.GetInt32(5) != 0,
            });
        }
        return result;
    }

    public DungeonBossInfo? FindDungeonBossByName(int dungeonId, string bossName)
    {
        if (_conn == null || string.IsNullOrWhiteSpace(bossName)) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
            SELECT db.ord, m.code, m.name, m.level, m.grade, m.is_final_boss
              FROM dungeon_bosses db
              JOIN mobs m ON m.code = db.mob_code
             WHERE db.dungeon_id = @d AND m.name = @n
             LIMIT 1";
        cmd.Parameters.AddWithValue("@d", dungeonId);
        cmd.Parameters.AddWithValue("@n", bossName);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new DungeonBossInfo
        {
            Order = r.GetInt32(0),
            MobId = r.GetInt32(1),
            Name = r.GetString(2),
            Level = r.IsDBNull(3) ? null : r.GetInt32(3),
            Grade = r.IsDBNull(4) ? null : r.GetInt32(4),
            IsFinalBoss = r.GetInt32(5) != 0,
        };
    }

    private static string BaseNameOf(string name)
        => System.Text.RegularExpressions.Regex.Replace(name, @"_(G_\d+|Normal|Easy|Hard|\d+)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string TierOfDungeonName(string name)
    {
        if (name.EndsWith("_Easy", StringComparison.OrdinalIgnoreCase)) return "쉬움";
        if (name.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase)) return "보통";
        if (name.EndsWith("_Hard", StringComparison.OrdinalIgnoreCase)) return "어려움";
        var m = System.Text.RegularExpressions.Regex.Match(name, @"_G_(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return $"{int.Parse(m.Groups[1].Value)}단계";
        return "기본";
    }

    // ── Item Queries ────────────────────────────────────────────────────────

    /// Returns the raw_json for an item, which for Plaync-sourced items
    /// contains the exact JSON structure expected by CombatScore.AdaptItem.
    public string? GetItemRawJson(long itemId)
    {
        if (_conn == null) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT raw_json FROM items WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", itemId);
        return cmd.ExecuteScalar() as string;
    }

    /// Returns basic item info: name, category, class2_text, grade, magicstone_count.
    public ItemInfo? GetItemInfo(long itemId)
    {
        if (_conn == null) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT name, category, class2_text, grade, magicstone_count, item_level FROM items WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", itemId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ItemInfo
        {
            Id = itemId,
            Name = r.GetString(0),
            Category = r.GetString(1),
            Class2Text = r.IsDBNull(2) ? null : r.GetString(2),
            Grade = r.IsDBNull(3) ? 0 : r.GetInt32(3),
            MagicStoneCount = r.IsDBNull(4) ? 0 : r.GetInt32(4),
            ItemLevel = r.IsDBNull(5) ? 0 : r.GetInt32(5),
        };
    }

    /// Returns main stats for an item (base values, before enchant).
    public List<ItemMainStat> GetItemMainStats(long itemId)
    {
        var result = new List<ItemMainStat>();
        if (_conn == null) return result;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT stat_name, value_min, value_max, is_percent FROM item_main_stats WHERE item_id = @id";
        cmd.Parameters.AddWithValue("@id", itemId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new ItemMainStat
            {
                StatName = r.GetString(0),
                ValueMin = r.IsDBNull(1) ? 0 : r.GetDouble(1),
                ValueMax = r.IsDBNull(2) ? 0 : r.GetDouble(2),
                IsPercent = r.GetInt32(3) != 0,
            });
        }
        return result;
    }

    // ── Enchant Curves ──────────────────────────────────────────────────────

    /// Returns the cumulative enchant bonus for a given slot/grade/level.
    /// e.g., GetEnchantBonus("weapon", "영웅", "enhancement", 15) → extra flat attack at +15.
    public double GetEnchantBonus(string slotClass, string grade, string source, int level)
    {
        if (_conn == null) return 0;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT extra_flat FROM enchant_curves
            WHERE slot_class = @sc AND grade = @g AND source = @src AND level = @lv
            AND stat_name IN ('공격력', '방어력')
            LIMIT 1";
        cmd.Parameters.AddWithValue("@sc", slotClass);
        cmd.Parameters.AddWithValue("@g", grade);
        cmd.Parameters.AddWithValue("@src", source);
        cmd.Parameters.AddWithValue("@lv", level);
        var val = cmd.ExecuteScalar();
        return val is double d ? d : (val is long l ? l : 0);
    }

    /// Returns all enchant curve entries for a slot_class + grade + source.
    public List<EnchantCurveEntry> GetEnchantCurve(string slotClass, string grade, string source)
    {
        var result = new List<EnchantCurveEntry>();
        if (_conn == null) return result;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT stat_name, level, extra_flat, extra_pct FROM enchant_curves
            WHERE slot_class = @sc AND grade = @g AND source = @src ORDER BY stat_name, level";
        cmd.Parameters.AddWithValue("@sc", slotClass);
        cmd.Parameters.AddWithValue("@g", grade);
        cmd.Parameters.AddWithValue("@src", source);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new EnchantCurveEntry
            {
                StatName = r.GetString(0),
                Level = r.GetInt32(1),
                ExtraFlat = r.GetDouble(2),
                ExtraPct = r.GetDouble(3),
            });
        }
        return result;
    }

    /// Maps a class2_text (e.g., "대검", "상의") to its slot_class (weapon/armor/accessory/belt).
    public string? GetSlotClass(string class2Text)
    {
        if (_conn == null) return null;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT slot_class FROM slot_class_map WHERE class2_text = @t";
        cmd.Parameters.AddWithValue("@t", class2Text);
        return cmd.ExecuteScalar() as string;
    }

    // ── Daevanion ───────────────────────────────────────────────────────────

    /// Returns all node effects for a given board.
    public List<DaevanionEffect> GetDaevanionEffects(int boardCode)
    {
        var result = new List<DaevanionEffect>();
        if (_conn == null) return result;
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"SELECT n.code, n.name, e.effect_name, e.raw_value, e.value_pct, e.value_flat
            FROM daevanion_nodes n
            JOIN daevanion_node_effects e ON e.node_code = n.code
            WHERE n.board_code = @b";
        cmd.Parameters.AddWithValue("@b", boardCode);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            result.Add(new DaevanionEffect
            {
                NodeCode = r.GetInt32(0),
                NodeName = r.GetString(1),
                EffectName = r.GetString(2),
                RawValue = r.GetInt32(3),
                ValuePct = r.IsDBNull(4) ? null : r.GetDouble(4),
                ValueFlat = r.IsDBNull(5) ? null : r.GetDouble(5),
            });
        }
        return result;
    }
}

// ── DTOs ────────────────────────────────────────────────────────────────────

internal sealed class ItemInfo
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Class2Text { get; set; }
    public int Grade { get; set; }
    public int MagicStoneCount { get; set; }
    public int ItemLevel { get; set; }
}

internal sealed class DungeonInfo
{
    public int Id { get; set; }
    public string AssetName { get; set; } = "";
    public string BaseName { get; set; } = "";
    public string Tier { get; set; } = "";
}

internal sealed class DungeonBossInfo
{
    public int Order { get; set; }
    public int MobId { get; set; }
    public string Name { get; set; } = "";
    public int? Level { get; set; }
    public int? Grade { get; set; }
    public bool IsFinalBoss { get; set; }
}

internal sealed class ItemMainStat
{
    public string StatName { get; set; } = "";
    public double ValueMin { get; set; }
    public double ValueMax { get; set; }
    public bool IsPercent { get; set; }
}

internal sealed class EnchantCurveEntry
{
    public string StatName { get; set; } = "";
    public int Level { get; set; }
    public double ExtraFlat { get; set; }
    public double ExtraPct { get; set; }
}

internal sealed class DaevanionEffect
{
    public int NodeCode { get; set; }
    public string NodeName { get; set; } = "";
    public string EffectName { get; set; } = "";
    public int RawValue { get; set; }
    public double? ValuePct { get; set; }
    public double? ValueFlat { get; set; }
}
