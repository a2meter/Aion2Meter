using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class BloomShareCapsuleOverlay : FrameworkElement
{
	public static readonly DependencyProperty RatioProperty = DependencyProperty.Register("Ratio", typeof(double), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty LeftPaddingProperty = DependencyProperty.Register("LeftPadding", typeof(double), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RightPaddingProperty = DependencyProperty.Register("RightPadding", typeof(double), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BorderProperty = DependencyProperty.Register("Border", typeof(Brush), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty DividerProperty = DependencyProperty.Register("Divider", typeof(Brush), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register("Highlight", typeof(Brush), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ShadowProperty = DependencyProperty.Register("Shadow", typeof(Brush), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty SideInsetProperty = DependencyProperty.Register("SideInset", typeof(double), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty VerticalInsetProperty = DependencyProperty.Register("VerticalInset", typeof(double), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty CompactVerticalInsetProperty = DependencyProperty.Register("CompactVerticalInset", typeof(double), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty DividerBleedProperty = DependencyProperty.Register("DividerBleed", typeof(double), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty CompactDividerBleedProperty = DependencyProperty.Register("CompactDividerBleed", typeof(double), typeof(BloomShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

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

	public Brush Divider
	{
		get
		{
			return (Brush)GetValue(DividerProperty);
		}
		set
		{
			SetValue(DividerProperty, value);
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

	public Brush Shadow
	{
		get
		{
			return (Brush)GetValue(ShadowProperty);
		}
		set
		{
			SetValue(ShadowProperty, value);
		}
	}

	public double SideInset
	{
		get
		{
			return (double)GetValue(SideInsetProperty);
		}
		set
		{
			SetValue(SideInsetProperty, value);
		}
	}

	public double VerticalInset
	{
		get
		{
			return (double)GetValue(VerticalInsetProperty);
		}
		set
		{
			SetValue(VerticalInsetProperty, value);
		}
	}

	public double CompactVerticalInset
	{
		get
		{
			return (double)GetValue(CompactVerticalInsetProperty);
		}
		set
		{
			SetValue(CompactVerticalInsetProperty, value);
		}
	}

	public double DividerBleed
	{
		get
		{
			return (double)GetValue(DividerBleedProperty);
		}
		set
		{
			SetValue(DividerBleedProperty, value);
		}
	}

	public double CompactDividerBleed
	{
		get
		{
			return (double)GetValue(CompactDividerBleedProperty);
		}
		set
		{
			SetValue(CompactDividerBleedProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (actualWidth <= 2.0 || actualHeight <= 2.0)
		{
			return;
		}
		bool flag = actualHeight <= 34.0;
		double num = ((SideInset > 0.0) ? Math.Clamp(SideInset, 0.0, 18.0) : Math.Clamp(actualHeight * 0.11, flag ? 3.0 : 4.0, flag ? 4.5 : 6.0));
		double value = ((!flag) ? ((VerticalInset > 0.0) ? VerticalInset : 2.5) : ((CompactVerticalInset > 0.0) ? CompactVerticalInset : 2.0));
		value = Math.Clamp(value, 0.0, Math.Max(0.0, actualHeight * 0.35));
		double num2 = Math.Max(num, LeftPadding + num);
		double num3 = Math.Max(num, RightPadding + num);
		if (num2 + num3 > actualWidth - 18.0)
		{
			double num4 = Math.Max(0.0, (actualWidth - 18.0) / Math.Max(1.0, num2 + num3));
			num2 *= num4;
			num3 *= num4;
		}
		double num5 = Math.Max(18.0, actualWidth - num2 - num3);
		double num6 = (flag ? Math.Max(12.0, actualHeight - value * 2.0) : Math.Clamp(actualHeight * 0.4, 17.0, Math.Max(17.0, actualHeight - value * 2.0)));
		double num7 = (flag ? Math.Max(2.0, (actualHeight - num6) * 0.5) : Math.Max(value, actualHeight - num6 - value - 1.0));
		double num8 = num6 / 2.0;
		Rect rect = new Rect(num2, num7, num5, num6);
		double num9 = Math.Clamp(num5 * Math.Clamp(Ratio, 0.0, 100.0) / 100.0, 0.0, num5);
		RectangleGeometry rectangleGeometry = new RectangleGeometry(rect, num8, num8);
		dc.DrawGeometry(Track, new Pen(Border, 1.35), rectangleGeometry);
		dc.DrawRoundedRectangle(Highlight, null, new Rect(num2 + 5.0, num7 + 2.0, Math.Max(0.0, num5 - 10.0), Math.Max(1.0, num6 * 0.18)), num8, num8);
		if (num9 > 0.4)
		{
			dc.PushClip(rectangleGeometry);
			dc.DrawRectangle(Fill, null, new Rect(num2, num7, num9, num6));
			dc.DrawRoundedRectangle(Highlight, null, new Rect(num2 + 4.0, num7 + 3.0, Math.Max(0.0, num9 - 8.0), Math.Max(1.0, num6 * 0.24)), num8, num8);
			dc.DrawRoundedRectangle(Shadow, null, new Rect(num2 + 4.0, num7 + num6 * 0.7, Math.Max(0.0, num9 - 8.0), Math.Max(1.0, num6 * 0.16)), num8, num8);
			dc.Pop();
		}
		dc.DrawRoundedRectangle(null, new Pen(Highlight, 0.7), new Rect(num2 + 2.2, num7 + 2.2, Math.Max(0.0, num5 - 4.4), Math.Max(1.0, num6 - 4.4)), Math.Max(1.0, num8 - 2.0), Math.Max(1.0, num8 - 2.0));
		dc.DrawRoundedRectangle(Shadow, null, new Rect(num2 + 5.0, num7 + num6 * 0.76, Math.Max(0.0, num5 - 10.0), Math.Max(1.0, num6 * 0.12)), num8, num8);
		if (!flag && num9 > 34.0)
		{
			dc.PushClip(rectangleGeometry);
			DrawGlimmer(dc, Highlight, num2 + Math.Min(num9 - 9.0, num5 * 0.18), num7 + num6 * 0.43, 2.3);
			if (num9 > 92.0)
			{
				DrawGlimmer(dc, Highlight, num2 + Math.Min(num9 - 13.0, num5 * 0.42), num7 + num6 * 0.56, 1.7);
			}
			dc.Pop();
		}
		if (num9 > 2.0 && num9 < num5 - 2.0)
		{
			double num10 = num2 + num9;
			double num11 = Math.Clamp(num6 * 0.12, 3.0, 5.0);
			double value2 = ((!flag) ? ((DividerBleed > 0.0) ? DividerBleed : 4.0) : ((CompactDividerBleed > 0.0) ? CompactDividerBleed : 2.0));
			value2 = Math.Clamp(value2, 0.0, 8.0);
			Rect rectangle = new Rect(num10 - num11 * 0.5, num7 - value2, num11, num6 + value2 * 2.0);
			dc.DrawRoundedRectangle(Divider, new Pen(Border, 0.85), rectangle, 1.2, 1.2);
			dc.DrawLine(new Pen(Highlight, 1.0), new Point(num10 - num11 * 0.26, rectangle.Top + 1.0), new Point(num10 - num11 * 0.26, rectangle.Bottom - 1.0));
		}
	}

	private static void DrawGlimmer(DrawingContext dc, Brush brush, double x, double y, double radius)
	{
		Pen pen = new Pen(brush, 0.85);
		dc.DrawLine(pen, new Point(x - radius, y), new Point(x + radius, y));
		dc.DrawLine(pen, new Point(x, y - radius), new Point(x, y + radius));
		dc.DrawEllipse(brush, null, new Point(x, y), radius * 0.18, radius * 0.18);
	}
}
