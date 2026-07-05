using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace INGMeter.Core;

public sealed class EncounterLogStore
{
	public const string RecordExtension = ".inglog";

	private static string? _rootDirectory;

	private const string IndexFileName = "encounters.index.jsonl";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	public static string RootDirectory
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(_rootDirectory) && Directory.Exists(_rootDirectory))
			{
				return _rootDirectory;
			}
			_rootDirectory = ResolveRootDirectory();
			return _rootDirectory;
		}
	}

	private static string ResolveRootDirectory()
	{
		return RuntimePaths.EncounterLogsDirectory;
	}

	public string SaveCsv(string bossName, DateTime firstHitUtc, string csv)
	{
		if (string.IsNullOrWhiteSpace(csv))
		{
			throw new ArgumentException("Encounter log is empty.", "csv");
		}
		string value = SanitizeFileName(bossName);
		if (string.IsNullOrWhiteSpace(value))
		{
			value = "UnknownBoss";
		}
		DateTime value2 = ((firstHitUtc.Kind == DateTimeKind.Utc) ? firstHitUtc.ToLocalTime() : firstHitUtc);
		string text = $"{value2:yyyyMMdd_HHmmss}_{value}";
		string text2 = Path.Combine(RootDirectory, text + ".csv");
		int num = 2;
		while (File.Exists(text2))
		{
			text2 = Path.Combine(RootDirectory, $"{text}_{num}.csv");
			num++;
		}
		File.WriteAllText(text2, csv, Encoding.UTF8);
		return text2;
	}

	public string SaveRecord(EncounterLogRecordMeta meta, string compactLogJson)
	{
		if (string.IsNullOrWhiteSpace(compactLogJson))
		{
			throw new ArgumentException("Encounter log is empty.", "compactLogJson");
		}
		string value = SanitizeFileName(meta.BossName);
		if (string.IsNullOrWhiteSpace(value))
		{
			value = "UnknownBoss";
		}
		DateTime value2 = ((meta.StartUtc.Kind == DateTimeKind.Utc) ? meta.StartUtc.ToLocalTime() : meta.StartUtc);
		string text = $"{value2:yyyyMMdd_HHmmss}_{value}";
		string text2 = Path.Combine(RootDirectory, text + ".inglog");
		int num = 2;
		while (File.Exists(text2))
		{
			text2 = Path.Combine(RootDirectory, $"{text}_{num}{".inglog"}");
			num++;
		}
		string s = BuildRecordJson(meta, compactLogJson);
		byte[] bytes = Encoding.UTF8.GetBytes(s);
		byte[] array = Compress(bytes);
		File.WriteAllBytes(text2, array);
		AppendIndex(EncounterLogIndexItem.FromMeta(meta, Path.GetFileName(text2), bytes.Length, array.Length));
		return text2;
	}

	public IReadOnlyList<EncounterLogIndexItem> ListRecords()
	{
		Dictionary<string, EncounterLogIndexItem> dictionary = new Dictionary<string, EncounterLogIndexItem>(StringComparer.OrdinalIgnoreCase);
		foreach (EncounterLogIndexItem item2 in ReadIndexItems())
		{
			if (!string.IsNullOrWhiteSpace(item2.FileName) && File.Exists(ResolveRecordPath(item2.FileName)))
			{
				dictionary[item2.FileName] = item2;
			}
		}
		foreach (string item3 in Directory.EnumerateFiles(RootDirectory, "*.inglog", SearchOption.TopDirectoryOnly))
		{
			string fileName = Path.GetFileName(item3);
			if (!dictionary.ContainsKey(fileName) && TryReadIndexItemFromRecord(item3, out EncounterLogIndexItem item))
			{
				dictionary[fileName] = item with
				{
					FileName = fileName
				};
			}
		}
		return (from x in dictionary.Values
			orderby x.StartUtc descending, x.EndUtc descending
			select x).ToList();
	}

	public string ResolveRecordPath(string fileName)
	{
		return Path.Combine(RootDirectory, Path.GetFileName(fileName));
	}

	public static bool IsRecordFile(string path)
	{
		return string.Equals(Path.GetExtension(path), ".inglog", StringComparison.OrdinalIgnoreCase);
	}

	public static string ReadRecordJson(string path)
	{
		using MemoryStream stream = new MemoryStream(File.ReadAllBytes(path));
		using GZipStream gZipStream = new GZipStream(stream, CompressionMode.Decompress);
		using MemoryStream memoryStream = new MemoryStream();
		gZipStream.CopyTo(memoryStream);
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	private static string BuildRecordJson(EncounterLogRecordMeta meta, string compactLogJson)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(compactLogJson);
		using MemoryStream memoryStream = new MemoryStream();
		using (Utf8JsonWriter utf8JsonWriter = new Utf8JsonWriter((Stream)memoryStream, default(JsonWriterOptions)))
		{
			utf8JsonWriter.WriteStartObject();
			utf8JsonWriter.WriteNumber("v", 1);
			utf8JsonWriter.WriteString("fmt", "ingmeter-encounter-record-v1");
			utf8JsonWriter.WritePropertyName("meta");
			JsonSerializer.Serialize(utf8JsonWriter, meta, JsonOptions);
			utf8JsonWriter.WritePropertyName("log");
			jsonDocument.RootElement.WriteTo(utf8JsonWriter);
			utf8JsonWriter.WriteEndObject();
		}
		return Encoding.UTF8.GetString(memoryStream.ToArray());
	}

	private static byte[] Compress(byte[] rawBytes)
	{
		using MemoryStream memoryStream = new MemoryStream();
		using (GZipStream gZipStream = new GZipStream(memoryStream, CompressionLevel.SmallestSize, leaveOpen: true))
		{
			gZipStream.Write(rawBytes, 0, rawBytes.Length);
		}
		return memoryStream.ToArray();
	}

	private void AppendIndex(EncounterLogIndexItem item)
	{
		File.AppendAllText(Path.Combine(RootDirectory, "encounters.index.jsonl"), JsonSerializer.Serialize(item, JsonOptions) + Environment.NewLine, Encoding.UTF8);
	}

	private IEnumerable<EncounterLogIndexItem> ReadIndexItems()
	{
		string path = Path.Combine(RootDirectory, "encounters.index.jsonl");
		if (!File.Exists(path))
		{
			yield break;
		}
		foreach (string item in File.ReadLines(path, Encoding.UTF8))
		{
			if (!string.IsNullOrWhiteSpace(item))
			{
				EncounterLogIndexItem encounterLogIndexItem = null;
				try
				{
					encounterLogIndexItem = JsonSerializer.Deserialize<EncounterLogIndexItem>(item, JsonOptions);
				}
				catch
				{
				}
				if (encounterLogIndexItem != null)
				{
					yield return encounterLogIndexItem;
				}
			}
		}
	}

	private static bool TryReadIndexItemFromRecord(string path, out EncounterLogIndexItem item)
	{
		item = new EncounterLogIndexItem();
		try
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(ReadRecordJson(path));
			if (!jsonDocument.RootElement.TryGetProperty("meta", out var value))
			{
				return false;
			}
			EncounterLogRecordMeta encounterLogRecordMeta = value.Deserialize<EncounterLogRecordMeta>(JsonOptions);
			if (encounterLogRecordMeta == null)
			{
				return false;
			}
			FileInfo fileInfo = new FileInfo(path);
			item = EncounterLogIndexItem.FromMeta(encounterLogRecordMeta, fileInfo.Name, 0L, fileInfo.Length);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static string SanitizeFileName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		string text = value.Trim();
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char oldChar in invalidFileNameChars)
		{
			text = text.Replace(oldChar, '_');
		}
		while (text.Contains("__", StringComparison.Ordinal))
		{
			text = text.Replace("__", "_");
		}
		return text.Trim(new char[3] { ' ', '.', '_' });
	}
}
