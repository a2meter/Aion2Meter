using System;
using System.Windows;

namespace INGMeter.App;

internal static class MeterVisualScale
{
	public static double Font(double baseSize, double textScale, int fontSizeDelta)
	{
		return Math.Max(6.0, Math.Round(baseSize * textScale, MidpointRounding.AwayFromZero) + (double)fontSizeDelta);
	}

	public static double Dimension(double baseValue, double layoutScale)
	{
		return Math.Round(baseValue * layoutScale * 2.0, MidpointRounding.AwayFromZero) / 2.0;
	}

	public static double CardDensity(double textScale)
	{
		textScale = Math.Clamp(textScale, 0.72, 1.2);
		double num = (textScale - 0.72) / 0.48;
		return Math.Clamp(0.82 + num * 0.18, 0.82, 1.0);
	}

	public static double CardWidthDensity(double textScale)
	{
		textScale = Math.Clamp(textScale, 0.72, 1.2);
		double num = (textScale - 0.72) / 0.48;
		return Math.Clamp(0.9 + num * 0.1, 0.9, 1.0);
	}

	public static Thickness ScaleThickness(Thickness value, double layoutScale)
	{
		return new Thickness(Dimension(value.Left, layoutScale), Dimension(value.Top, layoutScale), Dimension(value.Right, layoutScale), Dimension(value.Bottom, layoutScale));
	}

	public static Thickness Thickness(double left, double top, double right, double bottom, double layoutScale)
	{
		return ScaleThickness(new Thickness(left, top, right, bottom), layoutScale);
	}

	public static CornerRadius Radius(double radius)
	{
		return new CornerRadius(Math.Max(0.0, radius));
	}
}
