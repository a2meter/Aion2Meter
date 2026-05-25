using System;
using System.Collections.Generic;

namespace A2Meter.Dps;

internal static class BuffUptimeFilter
{
    private static readonly string[] ExcludedNames = { "긴급회피", "강인함", "둔화" };

    public static List<BuffUptime> Filter(IEnumerable<BuffUptime> buffs, ISet<int> allowedCasterIds)
    {
        var result = new List<BuffUptime>();
        if (allowedCasterIds.Count == 0) return result;

        foreach (var buff in buffs)
        {
            if (IsExcludedName(buff.Name)) continue;
            if (buff.CasterEntityId <= 0) continue;
            if (!allowedCasterIds.Contains(buff.CasterEntityId)) continue;
            result.Add(buff);
        }

        return result;
    }

    private static bool IsExcludedName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var normalized = RemoveWhitespace(name);
        foreach (var excluded in ExcludedNames)
        {
            if (normalized.Contains(excluded, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static string RemoveWhitespace(string value)
    {
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        var n = 0;
        foreach (var ch in value)
        {
            if (!char.IsWhiteSpace(ch))
                buffer[n++] = ch;
        }
        return new string(buffer[..n]);
    }
}
