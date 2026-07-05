using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace INGMeter.Core;

public sealed class BuffNameMap
{
	private const string ResourceName = "INGMeter.assets.buffs_ko.json";

	private readonly Dictionary<int, BuffInfo> _buffs = new Dictionary<int, BuffInfo>();

	public int Count => _buffs.Count;

	public void LoadFromResource()
	{
		string text = Path.Combine(AppContext.BaseDirectory, "assets", "buffs_ko.json");
		if (File.Exists(text))
		{
			using (FileStream stream = File.OpenRead(text))
			{
				LoadFromStream(stream, text);
				return;
			}
		}
		using Stream stream2 = typeof(BuffNameMap).Assembly.GetManifestResourceStream("INGMeter.assets.buffs_ko.json");
		if (stream2 == null)
		{
			Console.WriteLine("[BuffNameMap] Resource not found: INGMeter.assets.buffs_ko.json");
		}
		else
		{
			LoadFromStream(stream2, "INGMeter.assets.buffs_ko.json");
		}
	}

	public void LoadFromStream(Stream stream, string sourceName = "stream")
	{
		try
		{
			_buffs.Clear();
			using JsonDocument jsonDocument = JsonDocument.Parse(stream);
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
			{
				return;
			}
			foreach (JsonProperty item in jsonDocument.RootElement.EnumerateObject())
			{
				if (int.TryParse(item.Name, out var result) && item.Value.ValueKind == JsonValueKind.Object)
				{
					JsonElement value = item.Value;
					_buffs[result] = new BuffInfo
					{
						Name = GetJsonString(value, "name"),
						Type = GetJsonString(value, "type"),
						Icon = GetJsonString(value, "icon"),
						IconView = (!value.TryGetProperty("icon_view", out var value2) || value2.ValueKind != JsonValueKind.False)
					};
				}
			}
			Console.WriteLine($"[BuffNameMap] Loaded {_buffs.Count} buffs from {sourceName}");
		}
		catch (Exception ex)
		{
			_buffs.Clear();
			Console.WriteLine("[BuffNameMap] Error loading " + sourceName + ": " + ex.Message);
		}
	}

	public bool TryGet(int buffId, out BuffInfo? info)
	{
		return _buffs.TryGetValue(buffId, out info);
	}

	private static string GetJsonString(JsonElement item, string name)
	{
		if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return "";
		}
		return value.GetString() ?? "";
	}
}
