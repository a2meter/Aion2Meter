using System;

namespace INGMeter.App;

public static class MeterScaleOptions
{
	public const double MinUiScale = 0.75;

	public const double MaxUiScale = 1.3;

	public const double DefaultUiScale = 0.96;

	public const double MinTextScale = 0.6;

	public const double MaxTextScale = 1.4;

	public const double DefaultTextScale = 1.1;

	public static double NormalizeUiScale(double value)
	{
		return Normalize(value, 0.75, 1.3);
	}

	public static double NormalizeTextScale(double value)
	{
		return Normalize(value, 0.6, 1.4);
	}

	public static double UiScaleForLegacyMode(MeterFontSizeMode mode)
	{
		return mode switch
		{
			MeterFontSizeMode.ExtraSmall => 0.82, 
			MeterFontSizeMode.Small => 0.9, 
			MeterFontSizeMode.Normal => 0.96, 
			MeterFontSizeMode.Large => 1.0, 
			MeterFontSizeMode.ExtraLarge => 1.12, 
			_ => 0.96, 
		};
	}

	public static double TextScaleForLegacyMode(MeterFontSizeMode mode)
	{
		return mode switch
		{
			MeterFontSizeMode.ExtraSmall => 0.98, 
			MeterFontSizeMode.Small => 1.04, 
			MeterFontSizeMode.Normal => 1.1, 
			MeterFontSizeMode.Large => 1.22, 
			MeterFontSizeMode.ExtraLarge => 1.32, 
			_ => 1.1, 
		};
	}

	private static double Normalize(double value, double min, double max)
	{
		return Math.Clamp(Math.Round(value * 100.0, MidpointRounding.AwayFromZero) / 100.0, min, max);
	}
}
