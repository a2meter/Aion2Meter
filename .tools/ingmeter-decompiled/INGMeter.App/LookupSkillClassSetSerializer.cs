using System;
using System.Collections.Generic;
using System.Linq;

namespace INGMeter.App;

public static class LookupSkillClassSetSerializer
{
	public static HashSet<string> Parse(string? value)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(value))
		{
			return hashSet;
		}
		string[] array = value.Split('|', 44, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		foreach (string text in array)
		{
			if (!string.IsNullOrWhiteSpace(text))
			{
				hashSet.Add(text.Trim());
			}
		}
		return hashSet;
	}

	public static string Serialize(IReadOnlySet<string> keys)
	{
		if (keys.Count == 0)
		{
			return "";
		}
		return string.Join("|", keys.Where((string key) => !string.IsNullOrWhiteSpace(key)).OrderBy<string, string>((string key) => key, StringComparer.OrdinalIgnoreCase));
	}

	public static HashSet<string> Clone(IEnumerable<string> keys)
	{
		return (from key in keys
			where !string.IsNullOrWhiteSpace(key)
			select key.Trim()).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	public static bool AreEqual(IReadOnlySet<string> left, IReadOnlySet<string> right)
	{
		return left.SetEquals(right);
	}
}
