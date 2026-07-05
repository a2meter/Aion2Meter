using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace INGMeter.Core;

public sealed class DungeonContentMap
{
	private sealed record DungeonContentPayload
	{
		public int Version { get; init; }

		public List<DungeonContentInfo>? Dungeons { get; init; }
	}

	private const string ResourceName = "INGMeter.assets.dungeons.json";

	private readonly Dictionary<int, DungeonContentInfo> _byCode = new Dictionary<int, DungeonContentInfo>();

	public DungeonContentMap()
	{
		Load();
	}

	public bool TryGet(int code, out DungeonContentInfo info)
	{
		return _byCode.TryGetValue(code, out info);
	}

	private void Load()
	{
		_byCode.Clear();
		try
		{
			using Stream stream = OpenDungeonDataStream();
			if (stream == null)
			{
				return;
			}
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				PropertyNameCaseInsensitive = true
			};
			DungeonContentPayload dungeonContentPayload = JsonSerializer.Deserialize<DungeonContentPayload>(stream, options);
			if (dungeonContentPayload?.Dungeons == null)
			{
				return;
			}
			foreach (DungeonContentInfo dungeon in dungeonContentPayload.Dungeons)
			{
				if (dungeon.Code > 0 && !string.IsNullOrWhiteSpace(dungeon.Name))
				{
					_byCode[dungeon.Code] = dungeon;
				}
			}
		}
		catch
		{
			_byCode.Clear();
		}
	}

	private static Stream? OpenDungeonDataStream()
	{
		string path = Path.Combine(AppContext.BaseDirectory, "assets", "dungeons.json");
		if (File.Exists(path))
		{
			return File.OpenRead(path);
		}
		return typeof(DungeonContentMap).Assembly.GetManifestResourceStream("INGMeter.assets.dungeons.json");
	}
}
