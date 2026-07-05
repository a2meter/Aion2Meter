using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class CrayonShareCapsuleOverlay : FrameworkElement
{
	public static readonly DependencyProperty RatioProperty = DependencyProperty.Register("Ratio", typeof(double), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty LeftPaddingProperty = DependencyProperty.Register("LeftPadding", typeof(double), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RightPaddingProperty = DependencyProperty.Register("RightPadding", typeof(double), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BorderProperty = DependencyProperty.Register("Border", typeof(Brush), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register("Highlight", typeof(Brush), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ShadowProperty = DependencyProperty.Register("Shadow", typeof(Brush), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty SideInsetProperty = DependencyProperty.Register("SideInset", typeof(double), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty VerticalInsetProperty = DependencyProperty.Register("VerticalInset", typeof(double), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty SketchPhaseProperty = DependencyProperty.Register("SketchPhase", typeof(double), typeof(CrayonShareCapsuleOverlay), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

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

	public double SketchPhase
	{
		get
		{
			return (double)GetValue(SketchPhaseProperty);
		}
		set
		{
			SetValue(SketchPhaseProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 4.0) && !(actualHeight <= 4.0))
		{
			bool flag = actualHeight <= 34.0;
			double num = ((SideInset > 0.0) ? SideInset : (flag ? 3.0 : 5.0));
			double num2 = ((VerticalInset > 0.0) ? VerticalInset : (flag ? 2.0 : 4.0));
			double num3 = Math.Max(num, LeftPadding + num);
			double num4 = Math.Max(num, RightPadding + num);
			if (num3 + num4 > actualWidth - 18.0)
			{
				double num5 = Math.Max(0.0, (actualWidth - 18.0) / Math.Max(1.0, num3 + num4));
				num3 *= num5;
				num4 *= num5;
			}
			double num6 = Math.Max(18.0, actualWidth - num3 - num4);
			double num7 = (flag ? Math.Max(12.0, actualHeight - num2 * 2.0) : Math.Clamp(actualHeight * 0.42, 18.0, Math.Max(18.0, actualHeight - num2 * 2.0)));
			double num8 = (flag ? Math.Max(2.0, (actualHeight - num7) * 0.5) : Math.Max(num2, actualHeight - num7 - num2));
			double num9 = num7 * 0.5;
			Rect rect = new Rect(num3, num8, num6, num7);
			double num10 = Math.Clamp(num6 * Math.Clamp(Ratio, 0.0, 100.0) / 100.0, 0.0, num6);
			Pen pen = new Pen(Border, 1.45)
			{
				StartLineCap = PenLineCap.Round,
				EndLineCap = PenLineCap.Round,
				LineJoin = PenLineJoin.Round
			};
			RectangleGeometry rectangleGeometry = new RectangleGeometry(rect, num9, num9);
			dc.DrawGeometry(Track, pen, rectangleGeometry);
			if (num10 > 0.5)
			{
				dc.PushClip(rectangleGeometry);
				dc.DrawRoundedRectangle(Fill, null, new Rect(num3, num8, num10, num7), num9, num9);
				DrawGlimmer(dc, Highlight, num3 + 8.0, num8 + num7 * 0.32, Math.Max(0.0, num10 - 16.0), num7 * 0.16);
				DrawScribbles(dc, Highlight, num3 + 7.0, num8 + num7 * 0.52, Math.Max(0.0, num10 - 13.0), num7 * 0.18, flag ? 2 : 4, SketchPhase);
				dc.Pop();
			}
			DrawLooseCapsuleOutline(dc, pen, rect, num9, SketchPhase);
			dc.DrawRoundedRectangle(Shadow, null, new Rect(num3 + 4.0, num8 + num7 * 0.76, Math.Max(0.0, num6 - 8.0), Math.Max(1.0, num7 * 0.11)), num9, num9);
			if (num10 > 3.0 && num10 < num6 - 3.0)
			{
				DrawDivider(dc, num3 + num10, num8, num7, pen);
			}
		}
	}

	private static void DrawLooseCapsuleOutline(DrawingContext dc, Pen pen, Rect bar, double radius, double phase)
	{
		CrayonDoodleFrame.DrawSketchLine(dc, pen, new Point(bar.Left + radius, bar.Top + 1.0), new Point(bar.Right - radius, bar.Top), 8, phase + 0.2);
		CrayonDoodleFrame.DrawSketchLine(dc, pen, new Point(bar.Right - radius * 0.34, bar.Top + radius * 0.26), new Point(bar.Right - radius * 0.3, bar.Bottom - radius * 0.22), 4, phase + 1.2);
		CrayonDoodleFrame.DrawSketchLine(dc, pen, new Point(bar.Right - radius, bar.Bottom), new Point(bar.Left + radius, bar.Bottom - 0.5), 8, phase + 2.2);
		CrayonDoodleFrame.DrawSketchLine(dc, pen, new Point(bar.Left + radius * 0.3, bar.Bottom - radius * 0.25), new Point(bar.Left + radius * 0.3, bar.Top + radius * 0.22), 4, phase + 3.0);
	}

	private static void DrawGlimmer(DrawingContext dc, Brush brush, double left, double top, double width, double height)
	{
		if (!(width <= 2.0))
		{
			dc.DrawRoundedRectangle(brush, null, new Rect(left, top, width, Math.Max(1.0, height)), height, height);
		}
	}

	private static void DrawScribbles(DrawingContext dc, Brush brush, double left, double top, double width, double height, int count, double phase)
	{
		if (!(width <= 6.0))
		{
			Pen pen = new Pen(brush, 0.75)
			{
				StartLineCap = PenLineCap.Round,
				EndLineCap = PenLineCap.Round
			};
			for (int i = 0; i < count; i++)
			{
				double num = top + height * ((double)i + 0.45) / (double)count;
				CrayonDoodleFrame.DrawSketchLine(dc, pen, new Point(left, num), new Point(left + width, num + Math.Sin((double)i * 1.3 + phase) * 1.5), 9, phase + (double)i * 0.5);
			}
		}
	}

	private static void DrawDivider(DrawingContext dc, double x, double top, double height, Pen outline)
	{
		double num = Math.Clamp(height * 0.16, 3.0, 5.0);
		dc.DrawRoundedRectangle(rectangle: new Rect(x - num * 0.5, top - 3.0, num, height + 6.0), brush: outline.Brush, pen: outline, radiusX: 2.0, radiusY: 2.0);
		dc.DrawEllipse(Brushes.White, outline, new Point(x, top + 1.5), num * 0.75, num * 0.75);
		dc.DrawEllipse(Brushes.White, outline, new Point(x, top + height - 1.5), num * 0.75, num * 0.75);
	}
}
