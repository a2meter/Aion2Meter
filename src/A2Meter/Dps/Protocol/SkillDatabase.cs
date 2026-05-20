using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace A2Meter.Dps.Protocol;

/// Slim port of A2Viewer.Dps.SkillDatabase.
/// Loads game_db.json (skills + buffs + mobs + dungeons) on construction.
/// Exposes the lookup surface the PacketDispatcher needs: ContainsSkillCode,
/// GetSkillName, IsMobBoss, IsKnownBuffCode, IsSkillCodeInRange,
/// and the multi-attempt ResolveFromPacketBytes used during damage parsing.
internal sealed class SkillDatabase
{
    private static readonly Lazy<SkillDatabase> _shared = new(() => new SkillDatabase());
    public static SkillDatabase Shared => _shared.Value;

    private static readonly (uint Min, uint Max)[] SkillRanges = new (uint, uint)[]
    {
        ( 11_000_000u, 20_000_000u),
        (  1_000_000u, 10_000_000u),
        (    100_000u,    200_000u),
        ( 29_000_000u, 31_000_000u),
    };

    private readonly Dictionary<int, string> _skills = new();
    private readonly Dictionary<int, string> _buffs  = new();
    private readonly Dictionary<int, string> _mobNames = new();
    private readonly Dictionary<int, bool>   _mobIsBoss = new();
    private readonly Dictionary<int, string> _dungeons = new();

    public int LastRawSkillCode { get; private set; }

    public SkillDatabase()
    {
        // Prefer SQLite database from AppData (downloaded from CDN).
        if (Data.GameDatabase.Instance.IsAvailable)
            LoadFromSqlite();
        else
        {
            // Legacy fallback: bundled JSON (if present).
            var path = Path.Combine(AppContext.BaseDirectory, "Data", "game_db.json");
            if (File.Exists(path)) LoadFromJson(path);
        }
    }

    private void LoadFromSqlite()
    {
        try
        {
            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={Data.DataManager.DatabasePath};Mode=ReadOnly");
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT code, name, job_code FROM skills";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int code = r.GetInt32(0);
                    string name = r.GetString(1);
                    _skills[code] = name;
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT code, name FROM buffs";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    _buffs[r.GetInt32(0)] = r.GetString(1);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT code, name FROM dungeons";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    _dungeons[r.GetInt32(0)] = r.GetString(1);
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT code, name, is_boss FROM mobs";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int code = r.GetInt32(0);
                    _mobNames[code] = r.GetString(1);
                    _mobIsBoss[code] = r.GetInt32(2) != 0;
                }
            }
        }
        catch
        {
            // Fall through; dictionaries may be partially loaded — that's acceptable.
        }
    }

    private void LoadFromJson(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (root.TryGetProperty("skills", out var sk))
                foreach (var prop in sk.EnumerateObject())
                    if (int.TryParse(prop.Name, out int id))
                    {
                        var name = prop.Value.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                        _skills[id] = name;
                    }

            if (root.TryGetProperty("buffs", out var bf))
                foreach (var prop in bf.EnumerateObject())
                    if (int.TryParse(prop.Name, out int id))
                    {
                        var name = prop.Value.TryGetProperty("name", out var n) ? (n.GetString() ?? "") : "";
                        _buffs[id] = name;
                    }

            if (root.TryGetProperty("dungeons", out var dg))
                foreach (var prop in dg.EnumerateObject())
                    if (int.TryParse(prop.Name, out int id) && prop.Value.TryGetProperty("name", out var n))
                    {
                        var s = n.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) _dungeons[id] = s!;
                    }

            if (root.TryGetProperty("mobs", out var mb))
                foreach (var prop in mb.EnumerateObject())
                    if (int.TryParse(prop.Name, out int id))
                    {
                        bool isBoss = prop.Value.TryGetProperty("isBoss", out var b) && b.GetBoolean();
                        _mobIsBoss[id] = isBoss;
                        if (prop.Value.TryGetProperty("name", out var n))
                        {
                            var s = n.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) _mobNames[id] = s!;
                        }
                    }
        }
        catch { /* missing/corrupt DB just means parsers fall back to empty lookups */ }
    }

    public bool ContainsSkillCode(int code) => _skills.ContainsKey(code) || _buffs.ContainsKey(code);

    public string? GetSkillName(int code)
    {
        int? resolved = ResolveSkillCodeFallback(code, c => _skills.ContainsKey(c));
        if (resolved.HasValue) return _skills[resolved.Value];
        return _buffs.TryGetValue(code, out var bn) ? bn : null;
    }

    public bool IsMobBoss(int code)         => _mobIsBoss.TryGetValue(code, out var b) && b;
    public string? GetMobName(int code)     => _mobNames.TryGetValue(code, out var n) ? n : null;
    public string  GetDungeonName(int id)   => _dungeons.TryGetValue(id, out var n) ? n : $"#{id}";
    public bool    IsDungeon(int id)       => _dungeons.ContainsKey(id);
    public bool IsKnownBuffCode(int code)   => _buffs.ContainsKey(code);

    /// Tries exact → /10*10 → /10000*10000 fallback chain (matches original A2Viewer).
    private static int? ResolveSkillCodeFallback(int code, Func<int, bool> predicate)
    {
        if (predicate(code)) return code;
        int r10 = code / 10 * 10;
        if (r10 != code && predicate(r10)) return r10;
        int r10k = code / 10000 * 10000;
        if (r10k != code && predicate(r10k)) return r10k;
        return null;
    }

    public static bool IsSkillCodeInRange(int code)
    {
        foreach (var (min, max) in SkillRanges)
            if ((uint)code >= min && (uint)code < max) return true;
        return false;
    }

    /// Decode specialization tiers from the delta between raw and base skill codes.
    /// Returns sorted ascending tier indices (1-based), or null if no specs.
    public static int[]? DecodeSpecializations(int rawCode, int baseCode)
    {
        int num = (rawCode - baseCode) / 10;
        if (num <= 0 || num > 999) return null;

        var list = new List<int>(3);
        while (num > 0)
        {
            int digit = num % 10;
            if (digit < 1 || digit > 5) return null;
            list.Add(digit);
            num /= 10;
        }
        if (list.Count == 0) return null;

        // Original validates descending order before sort.
        for (int i = 1; i < list.Count; i++)
            if (list[i] >= list[i - 1]) return null;

        list.Sort();
        return list.ToArray();
    }

    /// Walks a few candidate offsets at `pos` looking for a 4-byte little-endian
    /// integer that resolves to a known skill, possibly via *10/+1 packing.
    /// On success advances `pos` past the consumed bytes and returns the
    /// normalized base skill code; on failure returns 0.
    public int ResolveFromPacketBytes(byte[] data, ref int pos, int end)
    {
        LastRawSkillCode = 0;
        for (int i = 0; i < 7 && pos + i + 4 <= end; i++)
        {
            int raw = BitConverter.ToInt32(data, pos + i);
            if ((uint)raw >= 0x80000000) continue;

            int resolved = ResolveRawSkillValue(raw);
            if (resolved != 0)
            {
                pos += i + 5;
                return resolved;
            }
            if (raw > 0 && raw % 100 == 0)
            {
                resolved = ResolveRawSkillValue(raw / 100);
                if (resolved != 0)
                {
                    pos += i + 5;
                    return resolved;
                }
            }
        }
        return 0;
    }

    private int ResolveRawSkillValue(int baseVal)
    {
        if (baseVal <= 0) return 0;

        long n1 = (long)baseVal * 10L + 1;
        if (n1 > 0 && n1 < 0x80000000 && ContainsSkillCode((int)n1))
        {
            LastRawSkillCode = (int)n1;
            int norm = NormalizeToBaseSkill((int)n1);
            if (IsSkillCodeInRange(norm)) return norm;
        }

        long n2 = (long)baseVal * 10L;
        if (n2 > 0 && n2 < 0x80000000 && ContainsSkillCode((int)n2))
        {
            LastRawSkillCode = (int)n2;
            int norm = NormalizeToBaseSkill((int)n2);
            if (IsSkillCodeInRange(norm)) return norm;
        }

        int direct = NormalizeToBaseSkill(baseVal);
        if (IsSkillCodeInRange(direct))
        {
            if (LastRawSkillCode == 0) LastRawSkillCode = baseVal;
            return direct;
        }
        return 0;
    }

    private int NormalizeToBaseSkill(int code)
    {
        if (code < 29_000_000 || code >= 30_000_000)
        {
            int floor = code / 10000 * 10000;
            if (floor != code && ContainsSkillCode(floor))
            {
                if (code - floor < 10000) return floor;
                if (!ContainsSkillCode(code)) return floor;
                var nameFloor = GetSkillName(floor);
                var nameCode  = GetSkillName(code);
                if (nameFloor != null && nameCode != null && nameFloor == nameCode) return floor;
            }
        }
        return code;
    }
}
