using System;
using System.Collections.Generic;
using A2Meter.Api;

namespace A2Meter.Dps.Protocol;

/// Slim port of A2Viewer.Dps.SkillDatabase.
/// Loads skills, buffs, mobs, dungeons, and opcode config from A2Web at startup.
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

    private static readonly Dictionary<int, string> DungeonNameOverrides = new()
    {
        [620011] = "침식의 정화소",
        [620021] = "무스펠의 성배(어려움)",
        [620022] = "무스펠의 성배(보통)",
    };

    private readonly Dictionary<int, string> _skills = new();
    private readonly Dictionary<int, string> _buffs  = new();
    private readonly Dictionary<int, string> _mobNames = new();
    private readonly Dictionary<int, bool>   _mobIsBoss = new();
    private readonly Dictionary<int, string> _dungeons = new();

    public int LastRawSkillCode { get; private set; }

    public SkillDatabase()
    {
        LoadFromSnapshot(GameDataClient.Snapshot);
    }

    internal SkillDatabase(GameDataSnapshot snapshot)
    {
        LoadFromSnapshot(snapshot);
    }

    private void LoadFromSnapshot(GameDataSnapshot snapshot)
    {
        snapshot ??= new GameDataSnapshot();
        ProtocolOpcodeConfig.Configure(snapshot.Opcodes);

        foreach (var s in snapshot.Skills)
            if (s.Code != 0 && !string.IsNullOrWhiteSpace(s.Name))
                _skills[s.Code] = s.Name;

        foreach (var b in snapshot.Buffs)
            if (b.Code != 0 && !string.IsNullOrWhiteSpace(b.Name))
                _buffs[b.Code] = b.Name;

        foreach (var d in snapshot.Dungeons)
        {
            if (d.Id == 0) continue;
            var display = GetDungeonDisplayName(d);
            if (!string.IsNullOrWhiteSpace(display)) _dungeons[d.Id] = display;
        }

        foreach (var m in snapshot.Mobs)
        {
            if (m.Id == 0) continue;
            if (!string.IsNullOrWhiteSpace(m.Name)) _mobNames[m.Id] = m.Name;
            _mobIsBoss[m.Id] = m.IsBoss != 0;
        }

        foreach (var boss in snapshot.DungeonBosses)
        {
            var mobId = boss.MobId.GetValueOrDefault();
            if (mobId == 0) continue;

            _mobIsBoss[mobId] = true;
            if (!string.IsNullOrWhiteSpace(boss.BossName) &&
                (!_mobNames.TryGetValue(mobId, out var existing) || string.IsNullOrWhiteSpace(existing)))
            {
                _mobNames[mobId] = boss.BossName;
            }
        }
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
    public bool    IsDungeon(int id)        => _dungeons.ContainsKey(id);
    public bool IsKnownBuffCode(int code)   => _buffs.ContainsKey(code);

    private static int? ResolveSkillCodeFallback(int code, Func<int, bool> predicate)
    {
        if (predicate(code)) return code;
        int r10 = code / 10 * 10;
        if (r10 != code && predicate(r10)) return r10;
        int r10k = code / 10000 * 10000;
        if (r10k != code && predicate(r10k)) return r10k;
        return null;
    }

    private static string GetDungeonDisplayName(GameDungeonRow dungeon)
    {
        if (DungeonNameOverrides.TryGetValue(dungeon.Id, out var overrideName))
            return overrideName;

        var name = dungeon.Name?.Trim() ?? "";
        var baseName = dungeon.BaseName?.Trim() ?? "";
        var tier = dungeon.Tier?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(baseName) && !IsDefaultTier(tier) &&
            (string.IsNullOrWhiteSpace(name) || string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return FormatDungeonTierName(baseName, tier);
        }

        if (!string.IsNullOrWhiteSpace(name) && LooksLikeDisplayName(name))
            return name;

        if (!string.IsNullOrWhiteSpace(baseName))
        {
            if (IsDefaultTier(tier)) return baseName;
            return FormatDungeonTierName(baseName, tier);
        }

        return name;
    }

    private static string FormatDungeonTierName(string baseName, string tier)
        => LooksLikeDisplayName(baseName) || LooksLikeDisplayName(tier)
            ? $"{baseName}({tier})"
            : $"{baseName} {tier}".Trim();

    private static bool IsDefaultTier(string tier)
        => string.IsNullOrWhiteSpace(tier) || string.Equals(tier.Trim(), "기본", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeDisplayName(string value)
    {
        foreach (var ch in value)
        {
            if (ch >= '\uAC00' && ch <= '\uD7A3') return true;
        }
        return value.Contains('(') || value.Contains(')');
    }

    public static bool IsSkillCodeInRange(int code)
    {
        foreach (var (min, max) in SkillRanges)
            if ((uint)code >= min && (uint)code < max) return true;
        return false;
    }

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

        for (int i = 1; i < list.Count; i++)
            if (list[i] >= list[i - 1]) return null;

        list.Sort();
        return list.ToArray();
    }

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
