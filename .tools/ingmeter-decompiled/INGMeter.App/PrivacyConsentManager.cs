using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace INGMeter.App;

internal static class PrivacyConsentManager
{
	public const string CurrentConsentVersion = "2026-05-01";

	private const string AcceptedKey = "PrivacyConsentAccepted";

	private const string VersionKey = "PrivacyConsentVersion";

	private const string AcceptedAtKey = "PrivacyConsentAcceptedAtUtc";

	public static bool EnsureAccepted(Window? owner = null)
	{
		if (HasCurrentConsent())
		{
			return true;
		}
		PrivacyConsentWindow privacyConsentWindow = new PrivacyConsentWindow();
		if (owner != null && owner.IsVisible)
		{
			privacyConsentWindow.Owner = owner;
			privacyConsentWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		}
		int num;
		if (privacyConsentWindow.ShowDialog() == true)
		{
			num = (privacyConsentWindow.IsAccepted ? 1 : 0);
			if (num != 0)
			{
				SaveAccepted();
			}
		}
		else
		{
			num = 0;
		}
		return (byte)num != 0;
	}

	public static IReadOnlyList<string> ReadPersistedConsentLines()
	{
		Dictionary<string, string> values = ReadConfigValues();
		List<string> list = new List<string>();
		AddLineIfPresent(list, values, "PrivacyConsentAccepted");
		AddLineIfPresent(list, values, "PrivacyConsentVersion");
		AddLineIfPresent(list, values, "PrivacyConsentAcceptedAtUtc");
		return list;
	}

	private static bool HasCurrentConsent()
	{
		Dictionary<string, string> dictionary = ReadConfigValues();
		bool result = default(bool);
		if (dictionary.TryGetValue("PrivacyConsentAccepted", out var value) && bool.TryParse(value, out result) && result && dictionary.TryGetValue("PrivacyConsentVersion", out var value2))
		{
			return string.Equals(value2, "2026-05-01", StringComparison.Ordinal);
		}
		return false;
	}

	private static void SaveAccepted()
	{
		UpdateConfigValues(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["PrivacyConsentAccepted"] = bool.TrueString,
			["PrivacyConsentVersion"] = "2026-05-01",
			["PrivacyConsentAcceptedAtUtc"] = DateTime.UtcNow.ToString("O")
		});
	}

	private static Dictionary<string, string> ReadConfigValues()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string configFilePath = AppPaths.ConfigFilePath;
		if (!File.Exists(configFilePath))
		{
			return dictionary;
		}
		string[] array = File.ReadAllLines(configFilePath);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.Length != 0 && !text.StartsWith("#", StringComparison.Ordinal))
			{
				string[] array2 = text.Split('=', 2);
				if (array2.Length == 2)
				{
					dictionary[array2[0].Trim()] = array2[1].Trim();
				}
			}
		}
		return dictionary;
	}

	private static void UpdateConfigValues(IReadOnlyDictionary<string, string> updates)
	{
		string configFilePath = AppPaths.ConfigFilePath;
		List<string> list = (File.Exists(configFilePath) ? File.ReadAllLines(configFilePath).ToList() : new List<string> { "# INGMeter 설정 파일" });
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (int i = 0; i < list.Count; i++)
		{
			string text = list[i].Trim();
			if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal))
			{
				continue;
			}
			string[] array = text.Split('=', 2);
			if (array.Length != 2)
			{
				continue;
			}
			string key = array[0].Trim();
			if (TryGetUpdate(updates, key, out string canonicalKey, out string value))
			{
				if (hashSet.Add(canonicalKey))
				{
					list[i] = canonicalKey + "=" + value;
					continue;
				}
				list.RemoveAt(i);
				i--;
			}
		}
		foreach (KeyValuePair<string, string> update in updates)
		{
			if (!hashSet.Contains(update.Key))
			{
				list.Add(update.Key + "=" + update.Value);
			}
		}
		File.WriteAllLines(configFilePath, list);
	}

	private static bool TryGetUpdate(IReadOnlyDictionary<string, string> updates, string key, out string canonicalKey, out string value)
	{
		foreach (KeyValuePair<string, string> update in updates)
		{
			if (string.Equals(update.Key, key, StringComparison.OrdinalIgnoreCase))
			{
				canonicalKey = update.Key;
				value = update.Value;
				return true;
			}
		}
		canonicalKey = string.Empty;
		value = string.Empty;
		return false;
	}

	private static void AddLineIfPresent(List<string> lines, IReadOnlyDictionary<string, string> values, string key)
	{
		if (values.TryGetValue(key, out string value) && !string.IsNullOrWhiteSpace(value))
		{
			lines.Add(key + "=" + value);
		}
	}
}
