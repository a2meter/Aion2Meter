using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace INGMeter.Core;

public sealed class MobNameMap
{
	private const string ResourceName = "INGMeter.assets.mobs.json";

	private readonly Dictionary<int, NpcData> _mobs = new Dictionary<int, NpcData>();

	public int Count => _mobs.Count;

	public MobNameMap()
	{
	}

	public MobNameMap(string filePath)
	{
		LoadFromFile(filePath);
	}

	public void LoadFromResource()
	{
		string text = Path.Combine(AppContext.BaseDirectory, "assets", "mobs.json");
		if (File.Exists(text))
		{
			LoadFromFile(text);
			return;
		}
		using Stream stream = typeof(MobNameMap).Assembly.GetManifestResourceStream("INGMeter.assets.mobs.json");
		if (stream == null)
		{
			Console.WriteLine("[MobNameMap] Resource not found: INGMeter.assets.mobs.json");
		}
		else
		{
			LoadFromStream(stream, "INGMeter.assets.mobs.json");
		}
	}

	public void LoadFromFile(string filePath)
	{
		try
		{
			if (!File.Exists(filePath))
			{
				Console.WriteLine("[MobNameMap] File not found: " + filePath);
				return;
			}
			using FileStream stream = File.OpenRead(filePath);
			LoadFromStream(stream, filePath);
		}
		catch (Exception ex)
		{
			Console.WriteLine("[MobNameMap] Error loading " + filePath + ": " + ex.Message);
		}
	}

	public void LoadFromStream(Stream stream, string sourceName = "stream")
	{
		try
		{
			_mobs.Clear();
			using JsonDocument jsonDocument = JsonDocument.Parse(stream);
			if (jsonDocument.RootElement.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
				{
					if (TryReadInt(item, "code", out var value) && value > 0)
					{
						_mobs[value] = new NpcData
						{
							name = ReadString(item, "name"),
							isBoss = (TryReadBool(item, "boss") || TryReadBool(item, "isBoss"))
						};
					}
				}
			}
			else if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object)
			{
				foreach (JsonProperty item2 in jsonDocument.RootElement.EnumerateObject())
				{
					if (int.TryParse(item2.Name, out var result) && result > 0 && item2.Value.ValueKind == JsonValueKind.Object)
					{
						_mobs[result] = new NpcData
						{
							name = ReadString(item2.Value, "name"),
							isBoss = (TryReadBool(item2.Value, "boss") || TryReadBool(item2.Value, "isBoss"))
						};
					}
				}
			}
			Console.WriteLine($"[MobNameMap] Loaded {_mobs.Count} mobs from {sourceName}");
		}
		catch (Exception ex)
		{
			_mobs.Clear();
			Console.WriteLine("[MobNameMap] Error loading " + sourceName + ": " + ex.Message);
		}
	}

	public bool ContainsExact(int mobCode)
	{
		return _mobs.ContainsKey(mobCode);
	}

	public string GetName(int mobCode)
	{
		if (!TryGetMob(mobCode, out NpcData data) || string.IsNullOrWhiteSpace(data.name))
		{
			return $"Mob_{mobCode}";
		}
		return data.name;
	}

	public bool IsBoss(int mobCode)
	{
		if (_mobs.TryGetValue(mobCode, out NpcData value))
		{
			return value.isBoss;
		}
		return false;
	}

	private bool TryGetMob(int mobCode, out NpcData data)
	{
		if (_mobs.TryGetValue(mobCode, out data))
		{
			return true;
		}
		int num = Math.Abs(mobCode);
		for (int num2 = 10; num2 <= 1000; num2 *= 10)
		{
			int num3 = num / num2 * num2;
			if (num3 > 0 && num3 != num && _mobs.TryGetValue(num3, out data))
			{
				return true;
			}
		}
		data = new NpcData();
		return false;
	}

	private static bool TryReadInt(JsonElement item, string propertyName, out int value)
	{
		value = 0;
		if (TryGetPropertyIgnoreCase(item, propertyName, out var value2))
		{
			if (value2.ValueKind != JsonValueKind.Number)
			{
				if (value2.ValueKind == JsonValueKind.String)
				{
					return int.TryParse(value2.GetString(), out value);
				}
				return false;
			}
			return value2.TryGetInt32(out value);
		}
		return false;
	}

	private static bool TryReadBool(JsonElement item, string propertyName)
	{
		if (TryGetPropertyIgnoreCase(item, propertyName, out var value))
		{
			bool result = default(bool);
			if (value.ValueKind != JsonValueKind.True && !(value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out result) && result))
			{
				if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var value2))
				{
					return value2 != 0;
				}
				return false;
			}
			return true;
		}
		return false;
	}

	private static string ReadString(JsonElement item, string propertyName)
	{
		if (!TryGetPropertyIgnoreCase(item, propertyName, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
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
