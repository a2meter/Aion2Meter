using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class AbyssShareBarOverlay : FrameworkElement
{
	public static readonly DependencyProperty RatioProperty = DependencyProperty.Register("Ratio", typeof(double), typeof(AbyssShareBarOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty LeftPaddingProperty = DependencyProperty.Register("LeftPadding", typeof(double), typeof(AbyssShareBarOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RightPaddingProperty = DependencyProperty.Register("RightPadding", typeof(double), typeof(AbyssShareBarOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(AbyssShareBarOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(AbyssShareBarOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BorderProperty = DependencyProperty.Register("Border", typeof(Brush), typeof(AbyssShareBarOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register("Highlight", typeof(Brush), typeof(AbyssShareBarOverlay), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

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

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (actualWidth <= 12.0 || actualHeight <= 10.0)
		{
			return;
		}
		bool num = actualHeight <= 34.0;
		double num2 = Math.Clamp(LeftPadding + 5.0, 5.0, Math.Max(5.0, actualWidth * 0.45));
		double num3 = Math.Clamp(RightPadding + 5.0, 5.0, Math.Max(5.0, actualWidth * 0.38));
		if (num2 + num3 > actualWidth - 28.0)
		{
			double num4 = (actualWidth - 28.0) / Math.Max(1.0, num2 + num3);
			num2 *= num4;
			num3 *= num4;
		}
		double width = Math.Max(12.0, actualWidth - num2 - num3);
		double num5 = (num ? Math.Clamp(actualHeight * 0.24, 7.8, 10.0) : Math.Clamp(actualHeight * 0.23, 8.6, 11.2));
		double y = (num ? Math.Max(2.0, actualHeight - num5 - 5.0) : Math.Max(4.0, actualHeight - num5 - 8.0));
		double num6 = Math.Clamp(num5 * 0.16, 1.5, 2.7);
		Rect rect = new Rect(num2, y, width, num5);
		double num7 = Math.Clamp(rect.Width * Math.Clamp(Ratio, 0.0, 100.0) / 100.0, 0.0, rect.Width);
		RectangleGeometry geometry = new RectangleGeometry(rect, num6, num6);
		Rect rectangle = new Rect(rect.Left + 0.9, rect.Top + 0.9, Math.Max(1.0, rect.Width - 1.8), Math.Max(1.0, rect.Height - 1.8));
		double num8 = Math.Max(1.0, num6 - 0.5);
		dc.DrawRoundedRectangle(null, new Pen(WithOpacity(Border, 0.38), 0.6), new Rect(rect.Left - 0.5, rect.Top - 0.5, rect.Width + 1.0, rect.Height + 1.0), num6 + 0.5, num6 + 0.5);
		dc.DrawGeometry(Track, new Pen(WithOpacity(Border, 0.96), 0.9), geometry);
		dc.DrawRoundedRectangle(CreateTrackShadeBrush(), null, rectangle, num8, num8);
		dc.DrawLine(new Pen(WithOpacity(Highlight, 0.18), 0.45), new Point(rectangle.Left + 3.0, rectangle.Top + 0.7), new Point(rectangle.Right - 3.0, rectangle.Top + 0.7));
		if (num7 > 0.6)
		{
			double num9 = Math.Clamp(num7 - 0.9, 0.0, rectangle.Width);
			Rect rect2 = new Rect(rectangle.Left, rectangle.Top, num9, rectangle.Height);
			RectangleGeometry clipGeometry = new RectangleGeometry(rect2, num8, num8);
			dc.PushClip(clipGeometry);
			dc.DrawRectangle(WithOpacity(Fill, 0.92), null, rect2);
			dc.DrawRectangle(CreateFillDepthBrush(), null, rect2);
			dc.DrawRectangle(CreateFillShadeBrush(), null, rect2);
			dc.DrawRectangle(CreateSurfaceHazeBrush(24), null, new Rect(rect2.Left + 2.0, rect2.Top + 0.7, Math.Max(0.0, rect2.Width - 4.0), Math.Max(1.0, rect2.Height * 0.46)));
			dc.DrawLine(new Pen(WithOpacity(Highlight, 0.44), 0.5), new Point(rect2.Left + 2.0, rect2.Top + 0.75), new Point(rect2.Right - 2.0, rect2.Top + 0.75));
			dc.Pop();
			if (num9 > 8.0 && num9 < rectangle.Width - 1.0)
			{
				double num10 = rectangle.Left + num9;
				dc.DrawLine(new Pen(WithOpacity(Highlight, 0.28), 0.65), new Point(num10, rectangle.Top + 0.8), new Point(num10, rectangle.Bottom - 0.8));
				dc.DrawLine(new Pen(WithOpacity(Border, 0.38), 0.65), new Point(num10 + 0.9, rectangle.Top + 1.1), new Point(num10 + 0.9, rectangle.Bottom - 1.1));
			}
		}
		dc.DrawRoundedRectangle(null, new Pen(WithOpacity(Highlight, 0.2), 0.45), rectangle, num8, num8);
	}

	private static Brush WithOpacity(Brush brush, double opacity)
	{
		Brush brush2 = brush.CloneCurrentValue();
		brush2.Opacity *= opacity;
		brush2.Freeze();
		return brush2;
	}

	private static Brush CreateFillShadeBrush()
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.0);
		linearGradientBrush.EndPoint = new Point(0.0, 1.0);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(42, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(18, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.28));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.58));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(22, 0, 0, 0), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private static Brush CreateFillDepthBrush()
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.5);
		linearGradientBrush.EndPoint = new Point(1.0, 0.5);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(24, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(5, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.2));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.58));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(32, 0, 0, 0), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private static Brush CreateTrackShadeBrush()
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.0);
		linearGradientBrush.EndPoint = new Point(0.0, 1.0);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(132, 0, 0, 0), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(70, 4, 10, 18), 0.42));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(104, 0, 0, 0), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private static Brush CreateSurfaceHazeBrush(byte peakAlpha)
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.0);
		linearGradientBrush.EndPoint = new Point(1.0, 1.0);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)((double)(int)peakAlpha * 0.45), byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.18));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(peakAlpha, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.34));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.5));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb((byte)((double)(int)peakAlpha * 0.35), 216, 248, byte.MaxValue), 0.72));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}
}
