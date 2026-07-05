using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using INGMeter.Core;

namespace INGMeter.App;

public static class AppPaths
{
	public static string AppRootDirectory => RuntimePaths.AppRootDirectory;

	public static string AppDataDirectory => AppRootDirectory;

	public static string LogsDirectory => RuntimePaths.LogsDirectory;

	public static string ConfigFilePath => Path.Combine(AppRootDirectory, "config.ini");

	private static string ConfigMigrationMarkerPath => Path.Combine(AppRootDirectory, ".userdata_migrated");

	public static void MigrateUserDataFromAppDirectory()
	{
		MigrateConfigFromAppDirectory();
		MigrateEncounterLogsFromAppDirectory();
	}

	public static void MigrateConfigFromAppDirectory()
	{
		string configFilePath = ConfigFilePath;
		Directory.CreateDirectory(Path.GetDirectoryName(configFilePath) ?? AppRootDirectory);
		foreach (string legacyConfigCandidate in GetLegacyConfigCandidates())
		{
			if (!File.Exists(legacyConfigCandidate) || string.Equals(Path.GetFullPath(legacyConfigCandidate), Path.GetFullPath(configFilePath), StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			try
			{
				if (File.Exists(configFilePath))
				{
					MergeMissingConfigValues(configFilePath, legacyConfigCandidate);
				}
				else
				{
					File.Copy(legacyConfigCandidate, configFilePath, overwrite: false);
				}
				MarkConfigMigrationComplete("copied from " + legacyConfigCandidate);
				break;
			}
			catch
			{
			}
		}
	}

	private static void MigrateEncounterLogsFromAppDirectory()
	{
		string encounterLogsDirectory = RuntimePaths.EncounterLogsDirectory;
		foreach (string legacyEncounterLogDirectory in GetLegacyEncounterLogDirectories())
		{
			if (!Directory.Exists(legacyEncounterLogDirectory) || string.Equals(Path.GetFullPath(legacyEncounterLogDirectory), Path.GetFullPath(encounterLogsDirectory), StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			try
			{
				foreach (string item in Directory.EnumerateFiles(legacyEncounterLogDirectory, "*", SearchOption.TopDirectoryOnly))
				{
					string text = Path.Combine(encounterLogsDirectory, Path.GetFileName(item));
					if (!File.Exists(text))
					{
						File.Copy(item, text, overwrite: false);
					}
				}
			}
			catch
			{
			}
		}
	}

	private static void MergeMissingConfigValues(string targetPath, string sourcePath)
	{
		List<string> list = File.ReadAllLines(targetPath).ToList();
		HashSet<string> hashSet = ReadConfigKeys(list);
		bool flag = false;
		string[] array = File.ReadAllLines(sourcePath);
		foreach (string text in array)
		{
			if (TryGetConfigKey(text, out string key) && !hashSet.Contains(key))
			{
				list.Add(text);
				hashSet.Add(key);
				flag = true;
			}
		}
		if (flag)
		{
			File.WriteAllLines(targetPath, list);
		}
	}

	private static HashSet<string> ReadConfigKeys(IEnumerable<string> lines)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in lines)
		{
			if (TryGetConfigKey(line, out string key))
			{
				hashSet.Add(key);
			}
		}
		return hashSet;
	}

	private static bool TryGetConfigKey(string lineRaw, out string key)
	{
		key = "";
		string text = lineRaw.Trim();
		if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal))
		{
			return false;
		}
		string[] array = text.Split('=', 2);
		if (array.Length != 2)
		{
			return false;
		}
		key = array[0].Trim();
		return key.Length > 0;
	}

	private static void MarkConfigMigrationComplete(string reason)
	{
		try
		{
			if (!File.Exists(ConfigMigrationMarkerPath))
			{
				File.WriteAllText(ConfigMigrationMarkerPath, $"{DateTime.UtcNow:O} {reason}{Environment.NewLine}");
			}
		}
		catch
		{
		}
	}

	private static IEnumerable<string> GetLegacyConfigCandidates()
	{
		foreach (string legacyUserDataDirectory in GetLegacyUserDataDirectories())
		{
			yield return Path.Combine(legacyUserDataDirectory, "config.ini");
		}
	}

	private static IEnumerable<string> GetLegacyEncounterLogDirectories()
	{
		foreach (string legacyUserDataDirectory in GetLegacyUserDataDirectories())
		{
			yield return Path.Combine(legacyUserDataDirectory, "EncounterLogs");
		}
	}

	private static IEnumerable<string> GetLegacyUserDataDirectories()
	{
		List<string> list = new List<string>
		{
			RuntimePaths.ExecutableDirectory,
			Path.Combine(RuntimePaths.InstallRootDirectory, "current")
		};
		string path = Path.Combine(RuntimePaths.InstallRootDirectory, "packages", "VelopackTemp");
		if (Directory.Exists(path))
		{
			try
			{
				list.AddRange(from path2 in Directory.EnumerateDirectories(path)
					select new DirectoryInfo(path2) into dir
					orderby dir.LastWriteTimeUtc descending
					select dir.FullName);
			}
			catch
			{
			}
		}
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		string currentRoot = Path.GetFullPath(AppRootDirectory);
		foreach (string item in list)
		{
			string fullPath;
			try
			{
				fullPath = Path.GetFullPath(item);
			}
			catch
			{
				continue;
			}
			if (!string.Equals(fullPath, currentRoot, StringComparison.OrdinalIgnoreCase) && seen.Add(fullPath))
			{
				yield return fullPath;
			}
		}
	}

	public static void RemoveConfigKeys(params string[] keys)
	{
		if (keys.Length == 0)
		{
			return;
		}
		string configFilePath = ConfigFilePath;
		if (!File.Exists(configFilePath))
		{
			return;
		}
		try
		{
			HashSet<string> blockedKeys = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
			List<string> list = File.ReadAllLines(configFilePath).ToList();
			int count = list.Count;
			list.RemoveAll(delegate(string line)
			{
				string text = line.Trim();
				if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal))
				{
					return false;
				}
				string[] array = text.Split('=', 2);
				return array.Length == 2 && blockedKeys.Contains(array[0].Trim());
			});
			if (list.Count != count)
			{
				File.WriteAllLines(configFilePath, list);
			}
		}
		catch
		{
		}
	}
}
