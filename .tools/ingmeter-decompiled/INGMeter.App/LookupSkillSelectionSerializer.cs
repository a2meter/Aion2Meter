using System;
using System.Collections.Generic;
using System.Linq;

namespace INGMeter.App;

public static class LookupSkillSelectionSerializer
{
	public static Dictionary<string, HashSet<int>> Parse(string? value)
	{
		Dictionary<string, HashSet<int>> dictionary = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrWhiteSpace(value))
		{
			return dictionary;
		}
		string[] array = value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string[] array2 = array[i].Split(':', 2, StringSplitOptions.TrimEntries);
			if (array2.Length == 2 && !string.IsNullOrWhiteSpace(array2[0]))
			{
				HashSet<int> hashSet = (from token in array2[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					select int.TryParse(token, out var result) ? result : 0 into id
					where id > 0
					select id).ToHashSet();
				if (hashSet.Count > 0)
				{
					dictionary[array2[0]] = hashSet;
				}
			}
		}
		return dictionary;
	}

	public static string Serialize(IReadOnlyDictionary<string, HashSet<int>> selections)
	{
		if (selections.Count == 0)
		{
			return "";
		}
		return string.Join("|", from pair in selections.Where<KeyValuePair<string, HashSet<int>>>((KeyValuePair<string, HashSet<int>> pair) => pair.Value.Count > 0).OrderBy<KeyValuePair<string, HashSet<int>>, string>((KeyValuePair<string, HashSet<int>> pair) => pair.Key, StringComparer.OrdinalIgnoreCase)
			select pair.Key + ":" + string.Join(",", pair.Value.OrderBy((int id) => id)));
	}

	public static Dictionary<string, HashSet<int>> Clone(IReadOnlyDictionary<string, HashSet<int>> selections)
	{
		return selections.ToDictionary<KeyValuePair<string, HashSet<int>>, string, HashSet<int>>((KeyValuePair<string, HashSet<int>> pair) => pair.Key, (KeyValuePair<string, HashSet<int>> pair) => pair.Value.ToHashSet(), StringComparer.OrdinalIgnoreCase);
	}

	public static bool AreEqual(IReadOnlyDictionary<string, HashSet<int>> left, IReadOnlyDictionary<string, HashSet<int>> right)
	{
		if (left.Count != right.Count)
		{
			return false;
		}
		foreach (KeyValuePair<string, HashSet<int>> item in left)
		{
			if (!right.TryGetValue(item.Key, out HashSet<int> value) || !item.Value.SetEquals(value))
			{
				return false;
			}
		}
		return true;
	}
}
