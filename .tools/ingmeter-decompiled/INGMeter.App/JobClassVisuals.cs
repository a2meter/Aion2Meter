using System;
using System.Windows;
using System.Windows.Media;
using INGMeter.Core;

namespace INGMeter.App;

internal static class JobClassVisuals
{
	private static readonly LinearGradientBrush GladiatorAccentBrush = FrozenGradient(48, 203, byte.MaxValue);

	private static readonly SolidColorBrush GladiatorBorderBrush = Frozen(41, 133, 168);

	private static readonly LinearGradientBrush TemplarAccentBrush = FrozenGradient(56, 181, byte.MaxValue);

	private static readonly SolidColorBrush TemplarBorderBrush = Frozen(43, 115, 164);

	private static readonly LinearGradientBrush AssassinAccentBrush = FrozenGradient(103, 232, 95);

	private static readonly SolidColorBrush AssassinBorderBrush = Frozen(68, 145, 62);

	private static readonly LinearGradientBrush RangerAccentBrush = FrozenGradient(45, 200, 93);

	private static readonly SolidColorBrush RangerBorderBrush = Frozen(34, 116, 57);

	private static readonly LinearGradientBrush SorcererAccentBrush = FrozenGradient(209, 82, byte.MaxValue);

	private static readonly SolidColorBrush SorcererBorderBrush = Frozen(132, 48, 160);

	private static readonly LinearGradientBrush SpiritmasterAccentBrush = FrozenGradient(239, 95, byte.MaxValue);

	private static readonly SolidColorBrush SpiritmasterBorderBrush = Frozen(146, 57, 151);

	private static readonly LinearGradientBrush ClericAccentBrush = FrozenGradient(240, 216, 90);

	private static readonly SolidColorBrush ClericBorderBrush = Frozen(146, 128, 61);

	private static readonly LinearGradientBrush ChanterAccentBrush = FrozenGradient(240, 170, 63);

	private static readonly SolidColorBrush ChanterBorderBrush = Frozen(139, 99, 43);

	private static readonly LinearGradientBrush FallbackAccentBrush = FrozenGradient(0, 229, byte.MaxValue);

	private static readonly SolidColorBrush FallbackBorderBrush = Frozen(0, 160, 179);

	private static readonly SolidColorBrush GladiatorAetherVeilTextBrush = FrozenAetherVeilText(41, 133, 168);

	private static readonly SolidColorBrush TemplarAetherVeilTextBrush = FrozenAetherVeilText(43, 115, 164);

	private static readonly SolidColorBrush AssassinAetherVeilTextBrush = FrozenAetherVeilText(68, 145, 62);

	private static readonly SolidColorBrush RangerAetherVeilTextBrush = FrozenAetherVeilText(34, 116, 57);

	private static readonly SolidColorBrush SorcererAetherVeilTextBrush = FrozenAetherVeilText(132, 48, 160);

	private static readonly SolidColorBrush SpiritmasterAetherVeilTextBrush = FrozenAetherVeilText(146, 57, 151);

	private static readonly SolidColorBrush ClericAetherVeilTextBrush = FrozenAetherVeilText(146, 128, 61);

	private static readonly SolidColorBrush ChanterAetherVeilTextBrush = FrozenAetherVeilText(139, 99, 43);

	private static readonly SolidColorBrush FallbackAetherVeilTextBrush = FrozenAetherVeilText(0, 160, 179);

	private static readonly LinearGradientBrush GladiatorBloomBrush = FrozenBloomGradient(48, 203, byte.MaxValue);

	private static readonly SolidColorBrush GladiatorBloomBorderBrush = FrozenBloomBorder(41, 133, 168);

	private static readonly LinearGradientBrush TemplarBloomBrush = FrozenBloomGradient(56, 181, byte.MaxValue);

	private static readonly SolidColorBrush TemplarBloomBorderBrush = FrozenBloomBorder(43, 115, 164);

	private static readonly LinearGradientBrush AssassinBloomBrush = FrozenBloomGradient(103, 232, 95);

	private static readonly SolidColorBrush AssassinBloomBorderBrush = FrozenBloomBorder(68, 145, 62);

	private static readonly LinearGradientBrush RangerBloomBrush = FrozenBloomGradient(45, 200, 93);

	private static readonly SolidColorBrush RangerBloomBorderBrush = FrozenBloomBorder(34, 116, 57);

	private static readonly LinearGradientBrush SorcererBloomBrush = FrozenBloomGradient(209, 82, byte.MaxValue);

	private static readonly SolidColorBrush SorcererBloomBorderBrush = FrozenBloomBorder(132, 48, 160);

	private static readonly LinearGradientBrush SpiritmasterBloomBrush = FrozenBloomGradient(239, 95, byte.MaxValue);

	private static readonly SolidColorBrush SpiritmasterBloomBorderBrush = FrozenBloomBorder(146, 57, 151);

	private static readonly LinearGradientBrush ClericBloomBrush = FrozenBloomGradient(240, 216, 90);

	private static readonly SolidColorBrush ClericBloomBorderBrush = FrozenBloomBorder(146, 128, 61);

	private static readonly LinearGradientBrush ChanterBloomBrush = FrozenBloomGradient(240, 170, 63);

	private static readonly SolidColorBrush ChanterBloomBorderBrush = FrozenBloomBorder(139, 99, 43);

	private static readonly LinearGradientBrush FallbackBloomBrush = FrozenBloomGradient(0, 229, byte.MaxValue);

	private static readonly SolidColorBrush FallbackBloomBorderBrush = FrozenBloomBorder(0, 160, 179);

	private static readonly LinearGradientBrush GladiatorNeonBrush = FrozenNeonGradient(0, 240, byte.MaxValue);

	private static readonly SolidColorBrush GladiatorNeonBorderBrush = FrozenNeonBorder(0, 240, byte.MaxValue);

	private static readonly LinearGradientBrush TemplarNeonBrush = FrozenNeonGradient(53, 184, byte.MaxValue);

	private static readonly SolidColorBrush TemplarNeonBorderBrush = FrozenNeonBorder(53, 184, byte.MaxValue);

	private static readonly LinearGradientBrush AssassinNeonBrush = FrozenNeonGradient(100, byte.MaxValue, 98);

	private static readonly SolidColorBrush AssassinNeonBorderBrush = FrozenNeonBorder(100, byte.MaxValue, 98);

	private static readonly LinearGradientBrush RangerNeonBrush = FrozenNeonGradient(40, byte.MaxValue, 122);

	private static readonly SolidColorBrush RangerNeonBorderBrush = FrozenNeonBorder(40, byte.MaxValue, 122);

	private static readonly LinearGradientBrush SorcererNeonBrush = FrozenNeonGradient(243, 63, byte.MaxValue);

	private static readonly SolidColorBrush SorcererNeonBorderBrush = FrozenNeonBorder(243, 63, byte.MaxValue);

	private static readonly LinearGradientBrush SpiritmasterNeonBrush = FrozenNeonGradient(byte.MaxValue, 79, 216);

	private static readonly SolidColorBrush SpiritmasterNeonBorderBrush = FrozenNeonBorder(byte.MaxValue, 79, 216);

	private static readonly LinearGradientBrush ClericNeonBrush = FrozenNeonGradient(byte.MaxValue, 232, 74);

	private static readonly SolidColorBrush ClericNeonBorderBrush = FrozenNeonBorder(byte.MaxValue, 232, 74);

	private static readonly LinearGradientBrush ChanterNeonBrush = FrozenNeonGradient(byte.MaxValue, 157, 63);

	private static readonly SolidColorBrush ChanterNeonBorderBrush = FrozenNeonBorder(byte.MaxValue, 157, 63);

	private static readonly LinearGradientBrush FallbackNeonBrush = FrozenNeonGradient(0, 240, byte.MaxValue);

	private static readonly SolidColorBrush FallbackNeonBorderBrush = FrozenNeonBorder(0, 240, byte.MaxValue);

	private static readonly LinearGradientBrush GladiatorPastelBrush = FrozenPastelGradient(148, 216, byte.MaxValue);

	private static readonly SolidColorBrush GladiatorPastelBorderBrush = FrozenPastelBorder(148, 216, byte.MaxValue);

	private static readonly LinearGradientBrush TemplarPastelBrush = FrozenPastelGradient(167, 216, byte.MaxValue);

	private static readonly SolidColorBrush TemplarPastelBorderBrush = FrozenPastelBorder(167, 216, byte.MaxValue);

	private static readonly LinearGradientBrush AssassinPastelBrush = FrozenPastelGradient(154, 239, 189);

	private static readonly SolidColorBrush AssassinPastelBorderBrush = FrozenPastelBorder(154, 239, 189);

	private static readonly LinearGradientBrush RangerPastelBrush = FrozenPastelGradient(120, 229, 167);

	private static readonly SolidColorBrush RangerPastelBorderBrush = FrozenPastelBorder(120, 229, 167);

	private static readonly LinearGradientBrush SorcererPastelBrush = FrozenPastelGradient(215, 170, byte.MaxValue);

	private static readonly SolidColorBrush SorcererPastelBorderBrush = FrozenPastelBorder(215, 170, byte.MaxValue);

	private static readonly LinearGradientBrush SpiritmasterPastelBrush = FrozenPastelGradient(236, 171, byte.MaxValue);

	private static readonly SolidColorBrush SpiritmasterPastelBorderBrush = FrozenPastelBorder(236, 171, byte.MaxValue);

	private static readonly LinearGradientBrush ClericPastelBrush = FrozenPastelGradient(244, 223, 166);

	private static readonly SolidColorBrush ClericPastelBorderBrush = FrozenPastelBorder(244, 223, 166);

	private static readonly LinearGradientBrush ChanterPastelBrush = FrozenPastelGradient(byte.MaxValue, 196, 154);

	private static readonly SolidColorBrush ChanterPastelBorderBrush = FrozenPastelBorder(byte.MaxValue, 196, 154);

	private static readonly LinearGradientBrush FallbackPastelBrush = FrozenPastelGradient(157, 231, 242);

	private static readonly SolidColorBrush FallbackPastelBorderBrush = FrozenPastelBorder(157, 231, 242);

	public static Brush PastelBrushFor(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => GladiatorPastelBrush, 
			JobClass.Templar => TemplarPastelBrush, 
			JobClass.Assassin => AssassinPastelBrush, 
			JobClass.Ranger => RangerPastelBrush, 
			JobClass.Sorcerer => SorcererPastelBrush, 
			JobClass.Spiritmaster => SpiritmasterPastelBrush, 
			JobClass.Cleric => ClericPastelBrush, 
			JobClass.Chanter => ChanterPastelBrush, 
			_ => FallbackPastelBrush, 
		};
	}

	public static SolidColorBrush PastelBorderBrushFor(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => GladiatorPastelBorderBrush, 
			JobClass.Templar => TemplarPastelBorderBrush, 
			JobClass.Assassin => AssassinPastelBorderBrush, 
			JobClass.Ranger => RangerPastelBorderBrush, 
			JobClass.Sorcerer => SorcererPastelBorderBrush, 
			JobClass.Spiritmaster => SpiritmasterPastelBorderBrush, 
			JobClass.Cleric => ClericPastelBorderBrush, 
			JobClass.Chanter => ChanterPastelBorderBrush, 
			_ => FallbackPastelBorderBrush, 
		};
	}

	public static Brush AccentBrushFor(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => GladiatorAccentBrush, 
			JobClass.Templar => TemplarAccentBrush, 
			JobClass.Assassin => AssassinAccentBrush, 
			JobClass.Ranger => RangerAccentBrush, 
			JobClass.Sorcerer => SorcererAccentBrush, 
			JobClass.Spiritmaster => SpiritmasterAccentBrush, 
			JobClass.Cleric => ClericAccentBrush, 
			JobClass.Chanter => ChanterAccentBrush, 
			_ => FallbackAccentBrush, 
		};
	}

	public static SolidColorBrush BorderBrushFor(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => GladiatorBorderBrush, 
			JobClass.Templar => TemplarBorderBrush, 
			JobClass.Assassin => AssassinBorderBrush, 
			JobClass.Ranger => RangerBorderBrush, 
			JobClass.Sorcerer => SorcererBorderBrush, 
			JobClass.Spiritmaster => SpiritmasterBorderBrush, 
			JobClass.Cleric => ClericBorderBrush, 
			JobClass.Chanter => ChanterBorderBrush, 
			_ => FallbackBorderBrush, 
		};
	}

	public static SolidColorBrush AetherVeilTextBrushFor(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => GladiatorAetherVeilTextBrush, 
			JobClass.Templar => TemplarAetherVeilTextBrush, 
			JobClass.Assassin => AssassinAetherVeilTextBrush, 
			JobClass.Ranger => RangerAetherVeilTextBrush, 
			JobClass.Sorcerer => SorcererAetherVeilTextBrush, 
			JobClass.Spiritmaster => SpiritmasterAetherVeilTextBrush, 
			JobClass.Cleric => ClericAetherVeilTextBrush, 
			JobClass.Chanter => ChanterAetherVeilTextBrush, 
			_ => FallbackAetherVeilTextBrush, 
		};
	}

	public static Brush BloomBrushFor(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => GladiatorBloomBrush, 
			JobClass.Templar => TemplarBloomBrush, 
			JobClass.Assassin => AssassinBloomBrush, 
			JobClass.Ranger => RangerBloomBrush, 
			JobClass.Sorcerer => SorcererBloomBrush, 
			JobClass.Spiritmaster => SpiritmasterBloomBrush, 
			JobClass.Cleric => ClericBloomBrush, 
			JobClass.Chanter => ChanterBloomBrush, 
			_ => FallbackBloomBrush, 
		};
	}

	public static SolidColorBrush BloomBorderBrushFor(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => GladiatorBloomBorderBrush, 
			JobClass.Templar => TemplarBloomBorderBrush, 
			JobClass.Assassin => AssassinBloomBorderBrush, 
			JobClass.Ranger => RangerBloomBorderBrush, 
			JobClass.Sorcerer => SorcererBloomBorderBrush, 
			JobClass.Spiritmaster => SpiritmasterBloomBorderBrush, 
			JobClass.Cleric => ClericBloomBorderBrush, 
			JobClass.Chanter => ChanterBloomBorderBrush, 
			_ => FallbackBloomBorderBrush, 
		};
	}

	public static Brush NeonBrushFor(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => GladiatorNeonBrush, 
			JobClass.Templar => TemplarNeonBrush, 
			JobClass.Assassin => AssassinNeonBrush, 
			JobClass.Ranger => RangerNeonBrush, 
			JobClass.Sorcerer => SorcererNeonBrush, 
			JobClass.Spiritmaster => SpiritmasterNeonBrush, 
			JobClass.Cleric => ClericNeonBrush, 
			JobClass.Chanter => ChanterNeonBrush, 
			_ => FallbackNeonBrush, 
		};
	}

	public static SolidColorBrush NeonBorderBrushFor(JobClass job)
	{
		return job switch
		{
			JobClass.Gladiator => GladiatorNeonBorderBrush, 
			JobClass.Templar => TemplarNeonBorderBrush, 
			JobClass.Assassin => AssassinNeonBorderBrush, 
			JobClass.Ranger => RangerNeonBorderBrush, 
			JobClass.Sorcerer => SorcererNeonBorderBrush, 
			JobClass.Spiritmaster => SpiritmasterNeonBorderBrush, 
			JobClass.Cleric => ClericNeonBorderBrush, 
			JobClass.Chanter => ChanterNeonBorderBrush, 
			_ => FallbackNeonBorderBrush, 
		};
	}

	private static SolidColorBrush Frozen(byte r, byte g, byte b)
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromRgb(r, g, b));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static SolidColorBrush FrozenAetherVeilText(byte r, byte g, byte b)
	{
		Color color = Tint(Color.FromRgb(r, g, b), 0.2);
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static LinearGradientBrush FrozenGradient(byte r, byte g, byte b)
	{
		Color color = Color.FromRgb(r, g, b);
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.5);
		linearGradientBrush.EndPoint = new Point(1.0, 0.5);
		linearGradientBrush.GradientStops.Add(new GradientStop(Shade(color, 0.5), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Shade(color, 0.68), 0.56));
		linearGradientBrush.GradientStops.Add(new GradientStop(Tint(Shade(color, 0.56), 0.12), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private static SolidColorBrush FrozenBloomBorder(byte r, byte g, byte b)
	{
		Color color = Blend(Tint(Color.FromRgb(r, g, b), 0.52), Color.FromRgb(234, 142, 184), 0.36);
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromArgb(232, color.R, color.G, color.B));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static LinearGradientBrush FrozenBloomGradient(byte r, byte g, byte b)
	{
		Color color = Color.FromRgb(r, g, b);
		Color to = Color.FromRgb(byte.MaxValue, 143, 193);
		Color to2 = Color.FromRgb(byte.MaxValue, 210, 181);
		Color to3 = Color.FromRgb(217, 184, byte.MaxValue);
		Color color2 = Blend(Tint(color, 0.76), to, 0.34);
		Color color3 = Blend(Tint(color, 0.56), to2, 0.24);
		Color color4 = Blend(Tint(color, 0.7), to3, 0.34);
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.5);
		linearGradientBrush.EndPoint = new Point(1.0, 0.5);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(228, color2.R, color2.G, color2.B), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(244, color3.R, color3.G, color3.B), 0.48));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(220, color4.R, color4.G, color4.B), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private static SolidColorBrush FrozenPastelBorder(byte r, byte g, byte b)
	{
		Color color = Blend(Color.FromRgb(r, g, b), Color.FromRgb(183, 204, byte.MaxValue), 0.25);
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromArgb(240, color.R, color.G, color.B));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static LinearGradientBrush FrozenPastelGradient(byte r, byte g, byte b)
	{
		Color color = Color.FromRgb(r, g, b);
		Color color2 = Tint(color, 0.36);
		Color color3 = Tint(color, 0.18);
		Color color4 = Blend(Tint(color, 0.28), Color.FromRgb(184, 199, byte.MaxValue), 0.16);
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.5);
		linearGradientBrush.EndPoint = new Point(1.0, 0.5);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(238, color2.R, color2.G, color2.B), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(248, color3.R, color3.G, color3.B), 0.55));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(232, color4.R, color4.G, color4.B), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private static SolidColorBrush FrozenNeonBorder(byte r, byte g, byte b)
	{
		Color color = Tint(Color.FromRgb(r, g, b), 0.18);
		SolidColorBrush solidColorBrush = new SolidColorBrush(Color.FromArgb(byte.MaxValue, color.R, color.G, color.B));
		solidColorBrush.Freeze();
		return solidColorBrush;
	}

	private static LinearGradientBrush FrozenNeonGradient(byte r, byte g, byte b)
	{
		Color color = Color.FromRgb(r, g, b);
		Color color2 = Tint(color, 0.38);
		Color color3 = Tint(color, 0.62);
		Color color4 = Tint(color, 0.18);
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.5);
		linearGradientBrush.EndPoint = new Point(1.0, 0.5);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(242, color2.R, color2.G, color2.B), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(byte.MaxValue, color3.R, color3.G, color3.B), 0.42));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(246, color.R, color.G, color.B), 0.68));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(223, color4.R, color4.G, color4.B), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private static Color Shade(Color color, double factor)
	{
		return Color.FromRgb(ClampColor((double)(int)color.R * factor), ClampColor((double)(int)color.G * factor), ClampColor((double)(int)color.B * factor));
	}

	private static Color Tint(Color color, double amount)
	{
		return Color.FromRgb(ClampColor((double)(int)color.R + (double)(255 - color.R) * amount), ClampColor((double)(int)color.G + (double)(255 - color.G) * amount), ClampColor((double)(int)color.B + (double)(255 - color.B) * amount));
	}

	private static Color Blend(Color from, Color to, double amount)
	{
		return Color.FromRgb(ClampColor((double)(int)from.R + (double)(to.R - from.R) * amount), ClampColor((double)(int)from.G + (double)(to.G - from.G) * amount), ClampColor((double)(int)from.B + (double)(to.B - from.B) * amount));
	}

	private static byte ClampColor(double value)
	{
		return (byte)Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
	}
}
