using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace INGMeter.App;

public static class MeterFontFamilies
{
	public const string Default = "Malgun Gothic";

	private static readonly Lazy<Dictionary<string, string>> InstalledFontFamilyNameMap = new Lazy<Dictionary<string, string>>(BuildInstalledFontFamilyNameMap);

	public static string Normalize(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "Malgun Gothic";
		}
		return value.Trim();
	}

	public static string NormalizeForStorage(string? value)
	{
		if (!TryGetInstalledFontFamilyName(Normalize(value), out string installedName))
		{
			return "Malgun Gothic";
		}
		return installedName;
	}

	public static FontFamily CreateFontFamily(string? value)
	{
		if (TryGetInstalledFontFamilyName(Normalize(value), out string installedName))
		{
			return new FontFamily(installedName);
		}
		return new FontFamily("Malgun Gothic");
	}

	private static Dictionary<string, string> BuildInstalledFontFamilyNameMap()
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (FontFamily systemFontFamily in Fonts.SystemFontFamilies)
		{
			string text = systemFontFamily.Source?.Trim() ?? "";
			if (string.IsNullOrWhiteSpace(text))
			{
				continue;
			}
			dictionary[text] = text;
			foreach (string value in systemFontFamily.FamilyNames.Values)
			{
				string text2 = value?.Trim() ?? "";
				if (!string.IsNullOrWhiteSpace(text2))
				{
					dictionary.TryAdd(text2, text);
				}
			}
		}
		return dictionary;
	}

	private static bool TryGetInstalledFontFamilyName(string name, out string installedName)
	{
		if (InstalledFontFamilyNameMap.Value.TryGetValue(name, out installedName))
		{
			return true;
		}
		installedName = name;
		return false;
	}
}
