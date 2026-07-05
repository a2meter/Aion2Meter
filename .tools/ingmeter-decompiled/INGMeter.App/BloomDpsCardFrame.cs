using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class BloomDpsCardFrame : FrameworkElement
{
	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(BloomDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BorderProperty = DependencyProperty.Register("Border", typeof(Brush), typeof(BloomDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty InnerBorderProperty = DependencyProperty.Register("InnerBorder", typeof(Brush), typeof(BloomDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty SparkleProperty = DependencyProperty.Register("Sparkle", typeof(Brush), typeof(BloomDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ShowInteriorProperty = DependencyProperty.Register("ShowInterior", typeof(bool), typeof(BloomDpsCardFrame), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ShowBorderProperty = DependencyProperty.Register("ShowBorder", typeof(bool), typeof(BloomDpsCardFrame), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ShowCenterOrnamentProperty = DependencyProperty.Register("ShowCenterOrnament", typeof(bool), typeof(BloomDpsCardFrame), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

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

	public Brush InnerBorder
	{
		get
		{
			return (Brush)GetValue(InnerBorderProperty);
		}
		set
		{
			SetValue(InnerBorderProperty, value);
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

	public bool ShowInterior
	{
		get
		{
			return (bool)GetValue(ShowInteriorProperty);
		}
		set
		{
			SetValue(ShowInteriorProperty, value);
		}
	}

	public bool ShowBorder
	{
		get
		{
			return (bool)GetValue(ShowBorderProperty);
		}
		set
		{
			SetValue(ShowBorderProperty, value);
		}
	}

	public bool ShowCenterOrnament
	{
		get
		{
			return (bool)GetValue(ShowCenterOrnamentProperty);
		}
		set
		{
			SetValue(ShowCenterOrnamentProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 2.0) && !(actualHeight <= 2.0))
		{
			double num = Math.Min(actualHeight * 0.46, 18.0);
			Rect rect = new Rect(1.0, 1.0, actualWidth - 2.0, actualHeight - 2.0);
			Rect rectangle = new Rect(4.0, 4.0, Math.Max(1.0, actualWidth - 8.0), Math.Max(1.0, actualHeight - 8.0));
			dc.DrawRoundedRectangle(Fill, ShowBorder ? new Pen(Border, 1.25) : null, rect, num, num);
			if (ShowInterior)
			{
				dc.DrawRoundedRectangle(null, new Pen(InnerBorder, 0.85), rectangle, Math.Max(1.0, num - 3.0), Math.Max(1.0, num - 3.0));
			}
			double size = Math.Clamp(actualHeight * 0.22, 6.0, 12.0);
			DrawCornerOrnament(dc, Sparkle, rect.Left + 6.0, rect.Top + 5.0, size, flip: false);
			DrawCornerOrnament(dc, Sparkle, rect.Right - 6.0, rect.Bottom - 5.0, size, flip: true);
			if (ShowInterior)
			{
				DrawPearls(dc, Sparkle, rect, actualHeight);
				DrawSparkle(dc, Sparkle, actualWidth - Math.Min(34.0, actualWidth * 0.12), Math.Max(7.0, actualHeight * 0.22), Math.Clamp(actualHeight * 0.07, 2.0, 4.0));
				DrawSparkle(dc, Sparkle, Math.Min(34.0, actualWidth * 0.12), actualHeight - Math.Max(7.0, actualHeight * 0.22), Math.Clamp(actualHeight * 0.055, 1.5, 3.2));
			}
			if (ShowCenterOrnament && actualWidth >= 240.0 && actualHeight >= 140.0)
			{
				double num2 = actualHeight - 10.0;
				DrawHeart(dc, Sparkle, actualWidth * 0.5, num2, 7.0);
				dc.DrawLine(new Pen(Sparkle, 0.7), new Point(actualWidth * 0.5 - 34.0, num2 + 1.0), new Point(actualWidth * 0.5 - 10.0, num2 + 1.0));
				dc.DrawLine(new Pen(Sparkle, 0.7), new Point(actualWidth * 0.5 + 10.0, num2 + 1.0), new Point(actualWidth * 0.5 + 34.0, num2 + 1.0));
				DrawSparkle(dc, Sparkle, actualWidth * 0.7, actualHeight * 0.16, 2.8);
				DrawSparkle(dc, Sparkle, actualWidth * 0.36, actualHeight * 0.23, 2.2);
			}
		}
	}

	private static void DrawCornerOrnament(DrawingContext dc, Brush brush, double x, double y, double size, bool flip)
	{
		Pen pen = new Pen(brush, 0.75)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		double num = (flip ? (-1.0) : 1.0);
		double num2 = (flip ? (-1.0) : 1.0);
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(x, y + num2 * size * 0.55), isFilled: false, isClosed: false);
			streamGeometryContext.BezierTo(new Point(x + num * size * 0.28, y + num2 * size * 0.12), new Point(x + num * size * 0.78, y + num2 * size * 0.1), new Point(x + num * size, y), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.BeginFigure(new Point(x + num * size * 0.42, y + num2 * size * 0.7), isFilled: false, isClosed: false);
			streamGeometryContext.BezierTo(new Point(x + num * size * 0.44, y + num2 * size * 0.42), new Point(x + num * size * 0.18, y + num2 * size * 0.36), new Point(x + num * size * 0.2, y + num2 * size * 0.58), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		dc.DrawGeometry(null, pen, streamGeometry);
	}

	private static void DrawPearls(DrawingContext dc, Brush brush, Rect outer, double height)
	{
		double num = Math.Clamp(height * 0.024, 0.9, 1.8);
		double num2 = Math.Clamp(height * 0.18, 5.0, 10.0);
		dc.DrawEllipse(brush, null, new Point(outer.Left + num2, outer.Top + num2), num, num);
		dc.DrawEllipse(brush, null, new Point(outer.Right - num2, outer.Top + num2), num, num);
		dc.DrawEllipse(brush, null, new Point(outer.Left + num2, outer.Bottom - num2), num, num);
		dc.DrawEllipse(brush, null, new Point(outer.Right - num2, outer.Bottom - num2), num, num);
	}

	private static void DrawSparkle(DrawingContext dc, Brush brush, double x, double y, double r)
	{
		Pen pen = new Pen(brush, 0.8);
		dc.DrawLine(pen, new Point(x - r, y), new Point(x + r, y));
		dc.DrawLine(pen, new Point(x, y - r), new Point(x, y + r));
		dc.DrawEllipse(brush, null, new Point(x, y), r * 0.22, r * 0.22);
	}

	private static void DrawHeart(DrawingContext dc, Brush brush, double x, double y, double size)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(x, y + size * 0.55), isFilled: true, isClosed: true);
			streamGeometryContext.BezierTo(new Point(x - size, y), new Point(x - size * 0.55, y - size * 0.7), new Point(x, y - size * 0.18), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.BezierTo(new Point(x + size * 0.55, y - size * 0.7), new Point(x + size, y), new Point(x, y + size * 0.55), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		dc.DrawGeometry(brush, null, streamGeometry);
	}
}
