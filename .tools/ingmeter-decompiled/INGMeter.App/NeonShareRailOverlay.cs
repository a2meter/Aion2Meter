using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class NeonShareRailOverlay : FrameworkElement
{
	public static readonly DependencyProperty RatioProperty = DependencyProperty.Register("Ratio", typeof(double), typeof(NeonShareRailOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty LeftPaddingProperty = DependencyProperty.Register("LeftPadding", typeof(double), typeof(NeonShareRailOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RightPaddingProperty = DependencyProperty.Register("RightPadding", typeof(double), typeof(NeonShareRailOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(NeonShareRailOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(NeonShareRailOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BorderProperty = DependencyProperty.Register("Border", typeof(Brush), typeof(NeonShareRailOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Brush), typeof(NeonShareRailOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register("Highlight", typeof(Brush), typeof(NeonShareRailOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty SparkleProperty = DependencyProperty.Register("Sparkle", typeof(Brush), typeof(NeonShareRailOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public double Ratio
	{
		get
		{
			return (double)GetValue(RatioProperty);
		}
		set
		{
			SetValue(RatioProperty, value);
		}
	}

	public double LeftPadding
	{
		get
		{
			return (double)GetValue(LeftPaddingProperty);
		}
		set
		{
			SetValue(LeftPaddingProperty, value);
		}
	}

	public double RightPadding
	{
		get
		{
			return (double)GetValue(RightPaddingProperty);
		}
		set
		{
			SetValue(RightPaddingProperty, value);
		}
	}

	public Brush Track
	{
		get
		{
			return (Brush)GetValue(TrackProperty);
		}
		set
		{
			SetValue(TrackProperty, value);
		}
	}

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

	public Brush Border
	{
		get
		{
			return (Brush)GetValue(BorderProperty);
		}
		set
		{
			SetValue(BorderProperty, value);
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

	public Brush Highlight
	{
		get
		{
			return (Brush)GetValue(HighlightProperty);
		}
		set
		{
			SetValue(HighlightProperty, value);
		}
	}

	public Brush Sparkle
	{
		get
		{
			return (Brush)GetValue(SparkleProperty);
		}
		set
		{
			SetValue(SparkleProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 8.0) && !(actualHeight <= 8.0))
		{
			bool flag = actualHeight <= 34.0;
			double num = Math.Max(5.0, LeftPadding + (flag ? 4.0 : 7.0));
			double num2 = Math.Max(7.0, RightPadding + (flag ? 5.0 : 9.0));
			if (num + num2 > actualWidth - 24.0)
			{
				double num3 = Math.Max(0.0, (actualWidth - 24.0) / Math.Max(1.0, num + num2));
				num *= num3;
				num2 *= num3;
			}
			double num4 = Math.Max(18.0, actualWidth - num - num2);
			double num5 = (flag ? Math.Clamp(actualHeight * 0.28, 8.0, 10.5) : Math.Clamp(actualHeight * 0.3, 9.5, 13.0));
			double num6 = Math.Clamp(actualHeight * 0.58, 4.5, Math.Max(4.5, actualHeight - num5 - 2.0));
			StreamGeometry geometry = CreateRail(num, num6, num4, num5);
			double num7 = Math.Clamp(num5 * 0.62, 4.5, 7.0);
			double num8 = Math.Clamp(num5 * 0.24, 1.7, 2.5);
			double num9 = num + num7;
			double num10 = num6 + num8;
			double num11 = Math.Max(3.0, num4 - num7 * 2.0);
			double num12 = Math.Max(2.0, num5 - num8 * 2.0);
			double num13 = Math.Clamp(num11 * Math.Clamp(Ratio, 0.0, 100.0) / 100.0, 0.0, num11);
			StreamGeometry streamGeometry = CreateRail(num9, num10, num11, num12);
			dc.DrawGeometry(null, new Pen(WithOpacity(Glow, 0.22), 1.6), geometry);
			dc.DrawGeometry(Track, new Pen(CreateRailStroke(), 0.42), geometry);
			dc.DrawGeometry(null, new Pen(WithOpacity(Highlight, 0.22), 0.2), streamGeometry);
			dc.PushClip(streamGeometry);
			if (num13 > 0.5)
			{
				dc.DrawRectangle(WithOpacity(Glow, 0.32), null, new Rect(num9, num10 - 1.0, num13, num12 + 2.0));
				dc.DrawRectangle(Fill, null, new Rect(num9, num10, num13, num12));
				dc.DrawRectangle(WithOpacity(Highlight, 0.88), null, new Rect(num9 + 5.0, num10 + Math.Max(0.7, num12 * 0.18), Math.Max(0.0, num13 - 10.0), Math.Max(0.8, num12 * 0.18)));
				dc.DrawRectangle(WithOpacity(Glow, 0.32), null, new Rect(num9 + 5.0, num10 + num12 * 0.68, Math.Max(0.0, num13 - 10.0), Math.Max(0.8, num12 * 0.14)));
			}
			DrawChevrons(dc, num9, num10, num11, num12, num13);
			DrawParticles(dc, num9, num10, num11, num12, num13);
			dc.Pop();
			if (num13 > 2.0 && num13 < num11 - 2.0)
			{
				DrawCursor(dc, num9 + num13, num10, num12);
			}
		}
	}

	private static StreamGeometry CreateRail(double left, double top, double width, double height)
	{
		double num = left + width;
		double num2 = top + height;
		double num3 = Math.Clamp(height * 0.34, 2.2, 4.5);
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(left + num3, top), isFilled: true, isClosed: true);
			streamGeometryContext.LineTo(new Point(num - num3, top), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(num, top + num3), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(num, num2 - num3), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(num - num3, num2), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(left + num3, num2), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(left, num2 - num3), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(left, top + num3), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}

	private Brush CreateRailStroke()
	{
		Color color = ColorFromBrush(Border, Color.FromRgb(0, 240, byte.MaxValue));
		Color color2 = ColorFromBrush(Glow, color);
		Color color3 = ColorFromBrush(Highlight, Colors.White);
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.5);
		linearGradientBrush.EndPoint = new Point(1.0, 0.5);
		linearGradientBrush.MappingMode = BrushMappingMode.RelativeToBoundingBox;
		linearGradientBrush.GradientStops.Add(new GradientStop(WithAlpha(color, 0.72), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(WithAlpha(color3, 0.3), 0.46));
		linearGradientBrush.GradientStops.Add(new GradientStop(WithAlpha(color2, 0.72), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private static Color ColorFromBrush(Brush brush, Color fallback)
	{
		if (!(brush is SolidColorBrush { Color: var color }))
		{
			if (brush is LinearGradientBrush linearGradientBrush && linearGradientBrush.GradientStops.Count > 0)
			{
				GradientStopCollection gradientStops = linearGradientBrush.GradientStops;
				return gradientStops[gradientStops.Count - 1].Color;
			}
			return fallback;
		}
		return color;
	}

	private static Color WithAlpha(Color color, double opacity)
	{
		color.A = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255.0);
		return color;
	}

	private void DrawChevrons(DrawingContext dc, double left, double top, double width, double height, double fillWidth)
	{
		double num = Math.Clamp(height * 1.2, 8.0, 12.0);
		double num2 = Math.Clamp(height * 0.32, 2.1, 3.8);
		for (double num3 = left + num * 1.1; num3 < left + width - num * 0.7; num3 += num)
		{
			bool flag = num3 <= left + fillWidth + 1.0;
			Pen pen = new Pen(flag ? WithOpacity(Highlight, 0.42) : WithOpacity(Border, 0.24), flag ? 0.6 : 0.45);
			double num4 = top + height * 0.5;
			dc.DrawLine(pen, new Point(num3 - num2 * 0.55, num4 - num2), new Point(num3 + num2 * 0.35, num4));
			dc.DrawLine(pen, new Point(num3 + num2 * 0.35, num4), new Point(num3 - num2 * 0.55, num4 + num2));
		}
	}

	private void DrawParticles(DrawingContext dc, double left, double top, double width, double height, double fillWidth)
	{
		double num = left + Math.Max(18.0, fillWidth * 0.42);
		double num2 = left + Math.Max(fillWidth, width * 0.28);
		if (!(num2 - num < 10.0))
		{
			Brush brush = WithOpacity(Sparkle, 0.7);
			for (double num3 = num; num3 < Math.Min(left + width - 12.0, num2); num3 += 17.0)
			{
				double num4 = Math.Sin(num3 * 0.13) * height * 0.18;
				double num5 = 0.45 + Math.Abs(Math.Sin(num3 * 0.07)) * 0.36;
				dc.DrawEllipse(brush, null, new Point(num3, top + height * 0.5 + num4), num5, num5);
			}
		}
	}

	private void DrawCursor(DrawingContext dc, double x, double top, double height)
	{
		double num = Math.Clamp(height * 0.2, 2.0, 3.3);
		Rect rectangle = new Rect(x - num * 0.5, top - 1.7, num, height + 3.4);
		dc.DrawRoundedRectangle(WithOpacity(Glow, 0.48), null, new Rect(rectangle.Left - 1.4, rectangle.Top - 0.4, rectangle.Width + 2.8, rectangle.Height + 0.8), 1.2, 1.2);
		dc.DrawRoundedRectangle(Fill, new Pen(Highlight, 0.7), rectangle, 0.9, 0.9);
	}

	private static Brush WithOpacity(Brush brush, double opacity)
	{
		Brush brush2 = brush.CloneCurrentValue();
		brush2.Opacity *= opacity;
		brush2.Freeze();
		return brush2;
	}
}
