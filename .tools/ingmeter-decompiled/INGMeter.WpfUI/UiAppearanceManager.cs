using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace INGMeter.WpfUI;

public static class UiAppearanceManager
{
	public static void Apply(AppearanceSelection appearance)
	{
		if (!TryApply(appearance) && !appearance.Equals(AppearanceSelection.Default))
		{
			TryApply(AppearanceSelection.Default);
		}
	}

	private static bool TryApply(AppearanceSelection appearance)
	{
		_ = appearance.ResourceThemeName;
		try
		{
			string resourceDictionaryPath = AppearanceCatalog.GetResourceDictionaryPath(appearance);
			ResourceDictionary item = new ResourceDictionary
			{
				Source = new Uri("pack://application:,,,/INGMeter;component/" + resourceDictionaryPath, UriKind.Absolute)
			};
			Collection<ResourceDictionary> mergedDictionaries = Application.Current.Resources.MergedDictionaries;
			for (int num = mergedDictionaries.Count - 1; num >= 0; num--)
			{
				string text = mergedDictionaries[num].Source?.ToString();
				if (text != null && text.EndsWith("Theme.xaml", StringComparison.Ordinal))
				{
					mergedDictionaries.RemoveAt(num);
				}
			}
			mergedDictionaries.Add(item);
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static void ApplyLegacyTheme(string? themeName)
	{
		Apply(AppearanceCatalog.FromLegacyThemeName(themeName));
	}
}
