using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class AetherVeilShareOverlay : FrameworkElement
{
	public static readonly DependencyProperty RatioProperty = DependencyProperty.Register("Ratio", typeof(double), typeof(AetherVeilShareOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty LeftPaddingProperty = DependencyProperty.Register("LeftPadding", typeof(double), typeof(AetherVeilShareOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RightPaddingProperty = DependencyProperty.Register("RightPadding", typeof(double), typeof(AetherVeilShareOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(AetherVeilShareOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(AetherVeilShareOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BorderProperty = DependencyProperty.Register("Border", typeof(Brush), typeof(AetherVeilShareOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register("Highlight", typeof(Brush), typeof(AetherVeilShareOverlay), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty HazeProperty = DependencyProperty.Register("Haze", typeof(Brush), typeof(AetherVeilShareOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

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

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 12.0) && !(actualHeight <= 10.0))
		{
			double ratio = Math.Clamp(Ratio, 0.0, 100.0) / 100.0;
			if (actualHeight <= 34.0)
			{
				DrawCompact(dc, actualWidth, actualHeight, ratio);
			}
			else
			{
				DrawStandard(dc, actualWidth, actualHeight, ratio);
			}
		}
	}

	private void DrawCompact(DrawingContext dc, double width, double height, double ratio)
	{
		double num = Math.Clamp(height * 0.22, 4.0, 7.0);
		Rect rect = new Rect(0.9, 1.2, Math.Max(1.0, width - 1.8), Math.Max(1.0, height - 2.4));
		double num2 = Math.Clamp(rect.Width * ratio, 0.0, rect.Width);
		if (!(num2 <= 0.5))
		{
			Rect rectangle = new Rect(rect.Left, rect.Top, num2, rect.Height);
			dc.PushClip(new RectangleGeometry(rect, num, num));
			dc.DrawRectangle(WithOpacity(Fill, 0.3), null, rectangle);
			dc.Pop();
		}
	}

	private void DrawStandard(DrawingContext dc, double width, double height, double ratio)
	{
		double num = Math.Clamp(LeftPadding + 2.0, 8.0, Math.Max(8.0, width * 0.58));
		double num2 = Math.Clamp(RightPadding + 6.0, 18.0, Math.Max(18.0, width * 0.45));
		if (num + num2 > width - 32.0)
		{
			double num3 = (width - 32.0) / Math.Max(1.0, num + num2);
			num *= num3;
			num2 *= num3;
		}
		double width2 = Math.Max(12.0, width - num - num2);
		double num4 = Math.Clamp(height * 0.19, 7.0, 10.0);
		double y = Math.Max(4.0, height - num4 - Math.Clamp(height * 0.14, 7.0, 11.0));
		double num5 = Math.Clamp(num4 * 0.2, 1.7, 3.0);
		Rect rectangle = new Rect(num, y, width2, num4);
		Rect rect = new Rect(rectangle.Left + 0.7, rectangle.Top + 0.7, Math.Max(1.0, rectangle.Width - 1.4), Math.Max(1.0, rectangle.Height - 1.4));
		double num6 = Math.Clamp(rect.Width * ratio, 0.0, rect.Width);
		dc.DrawRoundedRectangle(WithOpacity(Track, 0.2), null, rectangle, num5, num5);
		if (!(num6 <= 0.6))
		{
			Rect rect2 = new Rect(rect.Left, rect.Top, num6, rect.Height);
			double num7 = ((num6 >= rect.Width - 0.8) ? Math.Max(1.0, num5 - 0.4) : Math.Max(1.0, num5 - 0.7));
			dc.PushClip(new RectangleGeometry(rect2, num7, num7));
			dc.DrawRectangle(WithOpacity(Fill, 0.84), null, rect2);
			dc.Pop();
		}
	}

	private static Brush WithOpacity(Brush brush, double opacity)
	{
		Brush brush2 = brush.CloneCurrentValue();
		brush2.Opacity *= opacity;
		brush2.Freeze();
		return brush2;
	}
}
