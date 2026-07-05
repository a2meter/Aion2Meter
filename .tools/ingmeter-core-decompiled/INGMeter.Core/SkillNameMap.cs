using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace INGMeter.Core;

public sealed class SkillNameMap
{
	private const string ResourceName = "INGMeter.assets.skills_ko.json";

	private static readonly Regex NameCleanRegex = new Regex("\\s*(?:[-–]\\s+)?(?:\\d+단계|MAX|Level\\s*\\d+|Lv\\.\\s*\\d+|第\\d+階段)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

	private readonly Dictionary<int, string> _names = new Dictionary<int, string>();

	private readonly Dictionary<int, int[]> _idsByBaseCode = new Dictionary<int, int[]>();

	private readonly ConcurrentDictionary<int, string> _resolvedNames = new ConcurrentDictionary<int, string>();

	public int Count => _names.Count;

	public void LoadFromResource()
	{
		string text = Path.Combine(AppContext.BaseDirectory, "assets", "skills_ko.json");
		if (File.Exists(text))
		{
			LoadFromFile(text);
			return;
		}
		using Stream stream = typeof(SkillNameMap).Assembly.GetManifestResourceStream("INGMeter.assets.skills_ko.json");
		if (stream != null)
		{
			LoadFromStream(stream);
		}
	}

	public void LoadFromFile(string filePath)
	{
		if (!File.Exists(filePath))
		{
			_names.Clear();
			_idsByBaseCode.Clear();
			_resolvedNames.Clear();
			return;
		}
		using FileStream stream = File.OpenRead(filePath);
		LoadFromStream(stream);
	}

	public void LoadFromStream(Stream stream)
	{
		_names.Clear();
		_idsByBaseCode.Clear();
		_resolvedNames.Clear();
		using JsonDocument jsonDocument = JsonDocument.Parse(stream);
		if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
		{
			return;
		}
		foreach (JsonProperty item in jsonDocument.RootElement.EnumerateObject())
		{
			if (item.Value.ValueKind != JsonValueKind.Object)
			{
				continue;
			}
			int value = 0;
			if (item.Value.TryGetProperty("skill_id", out var value2))
			{
				value2.TryGetInt32(out value);
			}
			if (value <= 0)
			{
				int.TryParse(item.Name, out value);
			}
			if (value > 0 && item.Value.TryGetProperty("name", out var value3) && value3.ValueKind == JsonValueKind.String)
			{
				string text = value3.GetString();
				if (!string.IsNullOrWhiteSpace(text) && !_names.ContainsKey(value))
				{
					_names[value] = CleanSkillName(text);
				}
			}
		}
		RebuildBaseCodeIndex();
	}

	public IEnumerable<int> GetRegisteredIds()
	{
		return _names.Keys;
	}

	public bool HasExactId(int skillCode)
	{
		return _names.ContainsKey(skillCode);
	}

	public bool HasKnownIdOrBase(int skillCode)
	{
		int num = Math.Abs(skillCode);
		if (num <= 0)
		{
			return false;
		}
		if (_names.ContainsKey(num))
		{
			return true;
		}
		if (num >= 100000000)
		{
			int num2 = num / 10;
			if (num2 > 0 && _names.ContainsKey(num2))
			{
				return true;
			}
			int baseSkillCode = GetBaseSkillCode(num2);
			if (baseSkillCode > 0 && _names.ContainsKey(baseSkillCode))
			{
				return true;
			}
		}
		int baseSkillCode2 = GetBaseSkillCode(num);
		if (baseSkillCode2 > 0)
		{
			return _names.ContainsKey(baseSkillCode2);
		}
		return false;
	}

	public int GetDisplayGroupCode(int skillCode)
	{
		int num = Math.Abs(skillCode);
		if (num <= 0)
		{
			return skillCode;
		}
		int preferredBaseSkillCode = GetPreferredBaseSkillCode(num);
		if (preferredBaseSkillCode <= 0 || !HasKnownIdOrBase(num))
		{
			return num;
		}
		return preferredBaseSkillCode;
	}

	public string GetNameOrCode(int skillCode)
	{
		if (_resolvedNames.TryGetValue(skillCode, out string value))
		{
			return value;
		}
		int num = Math.Abs(skillCode);
		if (num <= 0)
		{
			return skillCode.ToString();
		}
		if (_names.TryGetValue(num, out string value2))
		{
			return CacheResolvedName(skillCode, value2);
		}
		int skillCode2 = num;
		if (num >= 100000000)
		{
			int num2 = num / 10;
			if (num2 > 0)
			{
				skillCode2 = num2;
				if (_names.TryGetValue(num2, out string value3))
				{
					return CacheResolvedName(skillCode, value3);
				}
				int baseSkillCode = GetBaseSkillCode(num2);
				if (baseSkillCode > 0 && _names.TryGetValue(baseSkillCode, out string value4))
				{
					return CacheResolvedName(skillCode, value4);
				}
			}
		}
		int baseSkillCode2 = GetBaseSkillCode(num);
		if (baseSkillCode2 > 0 && _names.TryGetValue(baseSkillCode2, out string value5))
		{
			return CacheResolvedName(skillCode, value5);
		}
		baseSkillCode2 = GetPreferredBaseSkillCode(num);
		if (baseSkillCode2 > 0 && _idsByBaseCode.TryGetValue(baseSkillCode2, out int[] value6) && TryFindClosestLowerId(value6, skillCode2, out var id) && _names.TryGetValue(id, out string value7))
		{
			return CacheResolvedName(skillCode, value7);
		}
		return CacheResolvedName(skillCode, skillCode.ToString());
	}

	private string CacheResolvedName(int skillCode, string name)
	{
		_resolvedNames[skillCode] = name;
		return name;
	}

	private void RebuildBaseCodeIndex()
	{
		Dictionary<int, List<int>> dictionary = new Dictionary<int, List<int>>();
		foreach (int key in _names.Keys)
		{
			int baseSkillCode = GetBaseSkillCode(key);
			if (baseSkillCode > 0)
			{
				if (!dictionary.TryGetValue(baseSkillCode, out var value))
				{
					value = (dictionary[baseSkillCode] = new List<int>());
				}
				value.Add(key);
			}
		}
		foreach (KeyValuePair<int, List<int>> item in dictionary)
		{
			List<int> value2 = item.Value;
			value2.Sort();
			_idsByBaseCode[item.Key] = value2.ToArray();
		}
	}

	private static bool TryFindClosestLowerId(int[] ids, int skillCode, out int id)
	{
		int num = Array.BinarySearch(ids, skillCode);
		num = ((num >= 0) ? (num - 1) : (~num - 1));
		if (num >= 0)
		{
			id = ids[num];
			return true;
		}
		id = 0;
		return false;
	}

	private static int GetPreferredBaseSkillCode(int skillCode)
	{
		int num = Math.Abs(skillCode);
		if (num >= 100000000)
		{
			int baseSkillCode = GetBaseSkillCode(num / 10);
			if (baseSkillCode > 0)
			{
				return baseSkillCode;
			}
		}
		return GetBaseSkillCode(num);
	}

	private static int GetBaseSkillCode(int skillCode)
	{
		int num = Math.Abs(skillCode);
		if (num < 10000000)
		{
			return 0;
		}
		return num / 10000 * 10000;
	}

	private static string CleanSkillName(string name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			return NameCleanRegex.Replace(name, "").Trim();
		}
		return name;
	}
}
