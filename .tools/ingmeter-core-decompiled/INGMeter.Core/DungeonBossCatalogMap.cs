using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace INGMeter.Core;

public sealed class DungeonBossCatalogMap
{
	private const string BossMobCodeResourceName = "INGMeter.assets.dungeon_boss_mob_codes.json";

	private static readonly Regex SpaceRegex = new Regex("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

	private readonly Dictionary<int, int[]> _bossCodesByDungeonCode = new Dictionary<int, int[]>();

	private readonly Dictionary<string, int[]> _bossCodesByDungeonKey = new Dictionary<string, int[]>(StringComparer.Ordinal);

	private readonly Dictionary<int, List<DungeonBossCatalogEntry>> _dungeonsByBossCode = new Dictionary<int, List<DungeonBossCatalogEntry>>();

	public DungeonBossCatalogMap()
	{
		Load();
	}

	public bool TryGetBossCodes(DungeonContentInfo? content, out int[] bossCodes)
	{
		bossCodes = Array.Empty<int>();
		if (content == null)
		{
			return false;
		}
		if (content.Code > 0 && _bossCodesByDungeonCode.TryGetValue(content.Code, out int[] value) && value.Length != 0)
		{
			bossCodes = value;
			return true;
		}
		string text = CreateDungeonKey(content.Category, content.Difficulty, content.Stage, content.Name);
		if (text.Length > 0 && _bossCodesByDungeonKey.TryGetValue(text, out int[] value2) && value2.Length != 0)
		{
			bossCodes = value2;
			return true;
		}
		return false;
	}

	public IReadOnlyList<DungeonBossCatalogEntry> FindDungeonsByBossCode(int mobCode)
	{
		if (mobCode <= 0)
		{
			return Array.Empty<DungeonBossCatalogEntry>();
		}
		if (_dungeonsByBossCode.TryGetValue(mobCode, out List<DungeonBossCatalogEntry> value))
		{
			return value;
		}
		int num = Math.Abs(mobCode);
		for (int num2 = 10; num2 <= 1000; num2 *= 10)
		{
			int num3 = num / num2 * num2;
			if (num3 > 0 && num3 != num && _dungeonsByBossCode.TryGetValue(num3, out List<DungeonBossCatalogEntry> value2))
			{
				return value2;
			}
		}
		return Array.Empty<DungeonBossCatalogEntry>();
	}

	private void Load()
	{
		_bossCodesByDungeonCode.Clear();
		_bossCodesByDungeonKey.Clear();
		_dungeonsByBossCode.Clear();
		try
		{
			using Stream stream = OpenAssetStream("dungeon_boss_mob_codes.json", "INGMeter.assets.dungeon_boss_mob_codes.json");
			if (stream == null)
			{
				return;
			}
			using JsonDocument jsonDocument = JsonDocument.Parse(stream);
			if (!jsonDocument.RootElement.TryGetProperty("dungeons", out var value) || value.ValueKind != JsonValueKind.Array)
			{
				return;
			}
			foreach (JsonElement item2 in value.EnumerateArray())
			{
				if (!TryReadInt(item2, "dungeon_id", out var value2) || value2 <= 0)
				{
					continue;
				}
				int[] array = ReadBossCodes(item2);
				if (array.Length == 0)
				{
					continue;
				}
				string category = ReadJsonString(item2, "category");
				string difficulty = ReadJsonString(item2, "difficulty");
				int value3;
				int? stage = (TryReadInt(item2, "stage", out value3) ? new int?(value3) : ((int?)null));
				string name = ReadJsonString(item2, "name");
				_bossCodesByDungeonCode[value2] = array;
				string text = CreateDungeonKey(category, difficulty, stage, name);
				if (text.Length > 0)
				{
					_bossCodesByDungeonKey[text] = array;
				}
				DungeonBossCatalogEntry item = new DungeonBossCatalogEntry(value2, category, difficulty, stage, name);
				int[] array2 = array;
				foreach (int key in array2)
				{
					if (!_dungeonsByBossCode.TryGetValue(key, out List<DungeonBossCatalogEntry> value4))
					{
						value4 = new List<DungeonBossCatalogEntry>();
						_dungeonsByBossCode[key] = value4;
					}
					value4.Add(item);
				}
			}
		}
		catch
		{
			_bossCodesByDungeonCode.Clear();
			_bossCodesByDungeonKey.Clear();
			_dungeonsByBossCode.Clear();
		}
	}

	private static int[] ReadBossCodes(JsonElement dungeon)
	{
		if (!dungeon.TryGetProperty("bosses", out var value) || value.ValueKind != JsonValueKind.Array)
		{
			return Array.Empty<int>();
		}
		SortedSet<int> sortedSet = new SortedSet<int>();
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (TryReadInt(item, "mob_code", out var value2) && value2 > 0)
			{
				sortedSet.Add(value2);
			}
		}
		return sortedSet.ToArray();
	}

	private static Stream? OpenAssetStream(string fileName, string resourceName)
	{
		string path = Path.Combine(AppContext.BaseDirectory, "assets", fileName);
		if (File.Exists(path))
		{
			return File.OpenRead(path);
		}
		return typeof(DungeonBossCatalogMap).Assembly.GetManifestResourceStream(resourceName);
	}

	private static string CreateDungeonKey(string? category, string? difficulty, int? stage, string? name)
	{
		string text = Normalize(name);
		if (text.Length == 0)
		{
			return "";
		}
		InlineArray4<string> buffer = default(InlineArray4<string>);
		buffer[0] = Normalize(category);
		buffer[1] = Normalize(difficulty);
		buffer[2] = stage?.ToString(CultureInfo.InvariantCulture) ?? "";
		buffer[3] = text;
		return string.Join("|", (ReadOnlySpan<string?>)buffer);
	}

	private static string Normalize(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return SpaceRegex.Replace(value.Trim(), "");
		}
		return "";
	}

	private static string ReadJsonString(JsonElement item, string propertyName)
	{
		if (!TryGetPropertyIgnoreCase(item, propertyName, out var value) || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
		{
			return "";
		}
		if (value.ValueKind != JsonValueKind.String)
		{
			return value.ToString();
		}
		return value.GetString() ?? "";
	}

	private static bool TryReadInt(JsonElement item, string propertyName, out int value)
	{
		value = 0;
		if (!TryGetPropertyIgnoreCase(item, propertyName, out var value2))
		{
			return false;
		}
		return value2.ValueKind switch
		{
			JsonValueKind.Number => value2.TryGetInt32(out value), 
			JsonValueKind.String => int.TryParse(value2.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value), 
			_ => false, 
		};
	}

	private static bool TryGetPropertyIgnoreCase(JsonElement item, string propertyName, out JsonElement value)
	{
		if (item.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item2 in item.EnumerateObject())
			{
				if (string.Equals(item2.Name, propertyName, StringComparison.OrdinalIgnoreCase))
				{
					value = item2.Value;
					return true;
				}
			}
		}
		value = default(JsonElement);
		return false;
	}
}
