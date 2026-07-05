using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class AetherVeilDpsCardFrame : FrameworkElement
{
	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(AetherVeilDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(AetherVeilDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty InnerStrokeProperty = DependencyProperty.Register("InnerStroke", typeof(Brush), typeof(AetherVeilDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Brush), typeof(AetherVeilDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty HazeProperty = DependencyProperty.Register("Haze", typeof(Brush), typeof(AetherVeilDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GrainProperty = DependencyProperty.Register("Grain", typeof(Brush), typeof(AetherVeilDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public Brush Fill
	{
		get
		{
			return (Brush)GetValue(FillProperty);
		}
		set
		{
			SetValue(FillProperty, value);
		}
	}

	public Brush Stroke
	{
		get
		{
			return (Brush)GetValue(StrokeProperty);
		}
		set
		{
			SetValue(StrokeProperty, value);
		}
	}

	public Brush InnerStroke
	{
		get
		{
			return (Brush)GetValue(InnerStrokeProperty);
		}
		set
		{
			SetValue(InnerStrokeProperty, value);
		}
	}

	public Brush Glow
	{
		get
		{
			return (Brush)GetValue(GlowProperty);
		}
		set
		{
			SetValue(GlowProperty, value);
		}
	}

	public Brush Haze
	{
		get
		{
			return (Brush)GetValue(HazeProperty);
		}
		set
		{
			SetValue(HazeProperty, value);
		}
	}

	public Brush Grain
	{
		get
		{
			return (Brush)GetValue(GrainProperty);
		}
		set
		{
			SetValue(GrainProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 4.0) && !(actualHeight <= 4.0))
		{
			double num = Math.Clamp(actualHeight * 0.18, 5.0, 12.0);
			Rect rect = Pixel(new Rect(0.7, 0.7, actualWidth - 1.4, actualHeight - 1.4));
			Rect rectangle = Pixel(new Rect(3.2, 3.0, Math.Max(1.0, actualWidth - 6.4), Math.Max(1.0, actualHeight - 6.0)));
			dc.DrawRoundedRectangle(Fill, null, rect, num, num);
			dc.PushClip(new RectangleGeometry(rect, num, num));
			DrawClassMist(dc, rect);
			DrawSurfaceDepth(dc, rect);
			DrawSoftFog(dc, rect);
			DrawPaperGrain(dc, rect);
			dc.Pop();
			dc.DrawRoundedRectangle(null, new Pen(WithOpacity(Stroke, 0.48), 0.65), rect, num, num);
			dc.DrawRoundedRectangle(null, new Pen(WithOpacity(InnerStroke, 0.07), 0.4), rectangle, Math.Max(1.0, num - 3.0), Math.Max(1.0, num - 3.0));
		}
	}

	private void DrawClassMist(DrawingContext dc, Rect rect)
	{
		dc.PushOpacityMask(CreateRadialMask(new Point(0.08, 0.36), new Point(0.0, 0.18), 0.58, 0.92));
		dc.DrawRectangle(WithOpacity(Glow, 0.16), null, rect);
		dc.Pop();
		dc.PushOpacityMask(CreateRadialMask(new Point(0.4, 0.42), new Point(0.22, 0.28), 0.78, 0.86));
		dc.DrawRectangle(WithOpacity(Glow, 0.12), null, rect);
		dc.Pop();
		dc.PushOpacityMask(CreateLinearMask(new Point(0.0, 0.0), new Point(1.0, 1.0), 0.78, 0.2));
		dc.DrawRectangle(WithOpacity(Glow, 0.055), null, rect);
		dc.Pop();
	}

	private static void DrawSurfaceDepth(DrawingContext dc, Rect rect)
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.0),
			EndPoint = new Point(0.0, 1.0)
		};
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(8, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(3, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.22));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.48));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(16, 0, 0, 0), 1.0));
		linearGradientBrush.Freeze();
		dc.DrawRectangle(linearGradientBrush, null, rect);
	}

	private void DrawSoftFog(DrawingContext dc, Rect rect)
	{
		DrawFogPatch(dc, rect, new Point(0.22, 0.24), new Point(0.06, 0.12), 0.58, 0.68, 0.055);
		DrawFogPatch(dc, rect, new Point(0.58, 0.5), new Point(0.42, 0.42), 0.54, 0.58, 0.045);
		DrawFogPatch(dc, rect, new Point(0.82, 0.18), new Point(0.72, 0.12), 0.34, 0.42, 0.03);
	}

	private void DrawFogPatch(DrawingContext dc, Rect rect, Point center, Point origin, double radiusX, double radiusY, double opacity)
	{
		dc.PushOpacityMask(CreateRadialMask(center, origin, radiusX, radiusY));
		dc.DrawRectangle(WithOpacity(Haze, opacity), null, rect);
		dc.Pop();
	}

	private void DrawPaperGrain(DrawingContext dc, Rect rect)
	{
		Pen pen = new Pen(WithOpacity(Grain, 0.018), 0.34)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		Pen pen2 = new Pen(WithOpacity(Grain, 0.014), 0.28)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		int num = Math.Clamp((int)(rect.Width / 46.0), 4, 10);
		for (int i = 0; i < num; i++)
		{
			double num2 = (double)i * 12.9898 + rect.Height * 0.31;
			double num3 = rect.Left + 8.0 + Pseudo(num2) * Math.Max(1.0, rect.Width - 16.0);
			double num4 = rect.Top + 5.0 + Pseudo(num2 + 31.7) * Math.Max(1.0, rect.Height - 10.0);
			double num5 = 2.5 + Pseudo(num2 + 7.3) * Math.Min(14.0, rect.Width * 0.035);
			double num6 = (Pseudo(num2 + 17.1) - 0.5) * 0.8;
			dc.DrawLine((i % 3 == 0) ? pen : pen2, new Point(num3, num4), new Point(Math.Min(rect.Right - 4.0, num3 + num5), num4 + num6));
		}
		Brush brush = WithOpacity(Grain, 0.028);
		int num7 = Math.Clamp((int)(rect.Width / 30.0), 8, 20);
		for (int j = 0; j < num7; j++)
		{
			double num8 = (double)j * 21.721 + rect.Width * 0.17;
			double x = rect.Left + 5.0 + Pseudo(num8) * Math.Max(1.0, rect.Width - 10.0);
			double y = rect.Top + 4.0 + Pseudo(num8 + 19.5) * Math.Max(1.0, rect.Height - 8.0);
			double num9 = 0.35 + Pseudo(num8 + 4.1) * 0.55;
			dc.DrawEllipse(brush, null, new Point(x, y), num9, num9);
		}
	}

	internal static Brush CreateRadialMask(Point center, Point origin, double radiusX, double radiusY)
	{
		RadialGradientBrush radialGradientBrush = new RadialGradientBrush();
		radialGradientBrush.Center = center;
		radialGradientBrush.GradientOrigin = origin;
		radialGradientBrush.RadiusX = radiusX;
		radialGradientBrush.RadiusY = radiusY;
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(160, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.22));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(40, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.56));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0));
		radialGradientBrush.Freeze();
		return radialGradientBrush;
	}

	private static Brush CreateLinearMask(Point start, Point end, double peak, double tail)
	{
		byte a = (byte)Math.Clamp((int)Math.Round(peak * 255.0), 0, 255);
		byte a2 = (byte)Math.Clamp((int)Math.Round(tail * 255.0), 0, 255);
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = start;
		linearGradientBrush.EndPoint = end;
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(a, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(a2, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.18));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.72));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private static Rect Pixel(Rect rect)
	{
		return new Rect(Math.Round(rect.Left, MidpointRounding.AwayFromZero), Math.Round(rect.Top, MidpointRounding.AwayFromZero), Math.Round(rect.Width, MidpointRounding.AwayFromZero), Math.Round(rect.Height, MidpointRounding.AwayFromZero));
	}

	private static double Pseudo(double seed)
	{
		double num = Math.Sin(seed) * 43758.5453123;
		return num - Math.Floor(num);
	}

	private static Brush WithOpacity(Brush brush, double opacity)
	{
		Brush brush2 = brush.CloneCurrentValue();
		brush2.Opacity *= opacity;
		brush2.Freeze();
		return brush2;
	}
}
