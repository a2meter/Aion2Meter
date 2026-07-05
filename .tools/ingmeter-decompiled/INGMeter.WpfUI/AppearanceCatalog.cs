using System;
using System.Collections.Generic;

namespace INGMeter.WpfUI;

public static class AppearanceCatalog
{
	private static readonly AppearanceOption[] Options = new AppearanceOption[11]
	{
		new AppearanceOption("블루 미스트", new AppearanceSelection(MeterPalette.BlueMist, MeterSkin.Default)),
		new AppearanceOption("다크", new AppearanceSelection(MeterPalette.Dark, MeterSkin.Default)),
		new AppearanceOption("미드나잇", new AppearanceSelection(MeterPalette.Midnight, MeterSkin.Default)),
		new AppearanceOption("바이올렛", new AppearanceSelection(MeterPalette.Violet, MeterSkin.Default)),
		new AppearanceOption("에메랄드", new AppearanceSelection(MeterPalette.Emerald, MeterSkin.Default)),
		new AppearanceOption("로즈", new AppearanceSelection(MeterPalette.Rose, MeterSkin.Default)),
		new AppearanceOption("심연", new AppearanceSelection(MeterPalette.Dark, MeterSkin.Abyss)),
		new AppearanceOption("에테르 베일", new AppearanceSelection(MeterPalette.Dark, MeterSkin.AetherVeil)),
		new AppearanceOption("네온", new AppearanceSelection(MeterPalette.Dark, MeterSkin.Neon)),
		new AppearanceOption("블룸", new AppearanceSelection(MeterPalette.Dark, MeterSkin.Bloom)),
		new AppearanceOption("크레용 물감", new AppearanceSelection(MeterPalette.Dark, MeterSkin.CrayonSplash))
	};

	public static IReadOnlyList<AppearanceOption> ThemeOptions => Options;

	public static AppearanceSelection FromLegacyThemeName(string? themeName)
	{
		if (string.IsNullOrWhiteSpace(themeName))
		{
			return AppearanceSelection.Default;
		}
		string text = themeName.Trim();
		if (string.Equals(text, "Bonobono", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "RainbowDrift", StringComparison.OrdinalIgnoreCase))
		{
			return new AppearanceSelection(MeterPalette.Dark, MeterSkin.CrayonSplash);
		}
		if (Enum.TryParse<MeterPalette>(text, ignoreCase: true, out var result))
		{
			return new AppearanceSelection(result, MeterSkin.Default);
		}
		if (Enum.TryParse<MeterSkin>(text, ignoreCase: true, out var result2))
		{
			return new AppearanceSelection(MeterPalette.Dark, result2);
		}
		return AppearanceSelection.Default;
	}

	public static string NormalizeLegacyThemeName(string? themeName)
	{
		return FromLegacyThemeName(themeName).ResourceThemeName;
	}

	public static string GetResourceDictionaryPath(AppearanceSelection appearance)
	{
		string value = ((appearance.Skin == MeterSkin.Default) ? "Palettes" : "Skins");
		return $"Themes/{value}/{appearance.ResourceThemeName}Theme.xaml";
	}

	public static MeterSkinProfile GetSkinProfile(MeterSkin skin)
	{
		return skin switch
		{
			MeterSkin.Bloom => new MeterSkinProfile(skin, UsesBloomLayoutFamily: true, UsesSoftDecoration: true, UsesNeonDecoration: false), 
			MeterSkin.Abyss => new MeterSkinProfile(skin, UsesBloomLayoutFamily: true, UsesSoftDecoration: false, UsesNeonDecoration: false), 
			MeterSkin.AetherVeil => new MeterSkinProfile(skin, UsesBloomLayoutFamily: true, UsesSoftDecoration: false, UsesNeonDecoration: false), 
			MeterSkin.CrayonSplash => new MeterSkinProfile(skin, UsesBloomLayoutFamily: true, UsesSoftDecoration: true, UsesNeonDecoration: false), 
			MeterSkin.Neon => new MeterSkinProfile(skin, UsesBloomLayoutFamily: false, UsesSoftDecoration: false, UsesNeonDecoration: true), 
			_ => new MeterSkinProfile(MeterSkin.Default, UsesBloomLayoutFamily: false, UsesSoftDecoration: false, UsesNeonDecoration: false), 
		};
	}

	public static MeterUiMode ParseUiMode(string? value)
	{
		return MeterUiMode.Hud;
	}
}
