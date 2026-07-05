using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class BloomHeartBadge : FrameworkElement
{
	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(BloomHeartBadge), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

	public Brush Accent
	{
		get
		{
			return (Brush)GetValue(AccentProperty);
		}
		set
		{
			SetValue(AccentProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 2.0) && !(actualHeight <= 2.0))
		{
			double num = Math.Min(actualWidth, actualHeight);
			Point center = new Point(actualWidth * 0.5, actualHeight * 0.5);
			double radius = num * 0.42;
			DrawBadgeBase(dc, center, radius, num);
			StreamGeometry heart = CreateHeartGeometry(center, num * 0.29);
			DrawHeart(dc, heart, num);
			DrawSparkles(dc, center, radius, num);
		}
	}

	private static void DrawBadgeBase(DrawingContext dc, Point center, double radius, double size)
	{
		dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(48, byte.MaxValue, 140, 188)), null, center, radius + size * 0.04, radius + size * 0.04);
		dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(byte.MaxValue, 247, 251)), new Pen(new SolidColorBrush(Color.FromRgb(217, 180, 106)), size * 0.055), center, radius, radius);
		dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(byte.MaxValue, 239, 163)), size * 0.024), center, radius - size * 0.075, radius - size * 0.075);
		dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(114, byte.MaxValue, byte.MaxValue, byte.MaxValue)), null, new Point(center.X - size * 0.13, center.Y - size * 0.21), size * 0.19, size * 0.09);
	}

	private static void DrawHeart(DrawingContext dc, Geometry heart, double size)
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			StartPoint = new Point(0.25, 0.05),
			EndPoint = new Point(0.75, 1.0)
		};
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(byte.MaxValue, 183, 207), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(245, 102, 164), 0.48));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromRgb(216, 47, 126), 1.0));
		linearGradientBrush.Freeze();
		dc.DrawGeometry(linearGradientBrush, new Pen(new SolidColorBrush(Color.FromRgb(201, 139, 61)), Math.Max(1.1, size * 0.038)), heart);
		dc.PushClip(heart);
		Pen pen = new Pen(new SolidColorBrush(Color.FromArgb(136, byte.MaxValue, byte.MaxValue, byte.MaxValue)), Math.Max(0.7, size * 0.015));
		dc.DrawLine(pen, new Point(size * 0.5, size * 0.28), new Point(size * 0.5, size * 0.73));
		dc.DrawLine(pen, new Point(size * 0.31, size * 0.4), new Point(size * 0.5, size * 0.57));
		dc.DrawLine(pen, new Point(size * 0.69, size * 0.4), new Point(size * 0.5, size * 0.57));
		dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(111, byte.MaxValue, 240, 247)), Math.Max(1.0, size * 0.02)), new Point(size * 0.37, size * 0.34), new Point(size * 0.58, size * 0.31));
		dc.Pop();
	}

	private void DrawSparkles(DrawingContext dc, Point center, double radius, double size)
	{
		DrawSparkle(dc, Accent, center.X - radius * 0.7, center.Y - radius * 0.42, size * 0.045);
		DrawSparkle(dc, Accent, center.X + radius * 0.7, center.Y - radius * 0.28, size * 0.036);
		DrawHeartCharm(dc, Accent, center.X + radius * 0.7, center.Y + radius * 0.7, size * 0.058);
	}

	private static StreamGeometry CreateHeartGeometry(Point center, double size)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(center.X, center.Y + size * 0.88), isFilled: true, isClosed: true);
			streamGeometryContext.BezierTo(new Point(center.X - size * 1.32, center.Y + size * 0.2), new Point(center.X - size * 0.92, center.Y - size * 0.96), new Point(center.X, center.Y - size * 0.34), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.BezierTo(new Point(center.X + size * 0.92, center.Y - size * 0.96), new Point(center.X + size * 1.32, center.Y + size * 0.2), new Point(center.X, center.Y + size * 0.88), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}

	private static void DrawSparkle(DrawingContext dc, Brush brush, double x, double y, double radius)
	{
		Pen pen = new Pen(brush, 0.8);
		dc.DrawLine(pen, new Point(x - radius, y), new Point(x + radius, y));
		dc.DrawLine(pen, new Point(x, y - radius), new Point(x, y + radius));
		dc.DrawEllipse(brush, null, new Point(x, y), radius * 0.18, radius * 0.18);
	}

	private static void DrawHeartCharm(DrawingContext dc, Brush brush, double x, double y, double size)
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
