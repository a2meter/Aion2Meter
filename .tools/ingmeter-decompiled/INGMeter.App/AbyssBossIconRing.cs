using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class AbyssBossIconRing : FrameworkElement
{
	public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(ImageSource), typeof(AbyssBossIconRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RingProperty = DependencyProperty.Register("Ring", typeof(Brush), typeof(AbyssBossIconRing), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Brush), typeof(AbyssBossIconRing), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(AbyssBossIconRing), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public ImageSource? Source
	{
		get
		{
			return (ImageSource)GetValue(SourceProperty);
		}
		set
		{
			SetValue(SourceProperty, value);
		}
	}

	public Brush Ring
	{
		get
		{
			return (Brush)GetValue(RingProperty);
		}
		set
		{
			SetValue(RingProperty, value);
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
		if (!(actualWidth <= 5.0) && !(actualHeight <= 5.0))
		{
			double num = Math.Min(actualWidth, actualHeight);
			Point center = new Point(actualWidth * 0.5, actualHeight * 0.5);
			double num2 = num * 0.42;
			double num3 = num * 0.37;
			dc.DrawEllipse(WithOpacity(Glow, 0.1), null, center, num2 + num * 0.05, num2 + num * 0.05);
			dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(244, 5, 8, 16)), null, center, num2 - num * 0.018, num2 - num * 0.018);
			if (Source != null)
			{
				EllipseGeometry clipGeometry = new EllipseGeometry(center, num3 - num * 0.006, num3 - num * 0.006);
				dc.PushClip(clipGeometry);
				double num4 = num * 0.72;
				double num5 = 0.0;
				dc.DrawImage(Source, new Rect(center.X - num4 * 0.5 - num5, center.Y - num4 * 0.5 - num5, num4, num4));
				dc.DrawRectangle(CreateIconSheenBrush(), null, new Rect(center.X - num3, center.Y - num3, num3 * 2.0, num3 * 2.0));
				dc.Pop();
			}
			dc.DrawEllipse(WithOpacity(Accent, 0.06), null, center, num3, num3);
			dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(136, 1, 3, 8)), Math.Max(0.65, num * 0.022)), center, num2 + num * 0.005, num2 + num * 0.005);
			dc.DrawEllipse(null, new Pen(WithOpacity(Ring, 0.98), Math.Max(0.65, num * 0.018)), center, num2, num2);
			dc.DrawEllipse(null, new Pen(WithOpacity(Accent, 0.86), Math.Max(0.45, num * 0.009)), center, num2 - num * 0.038, num2 - num * 0.038);
			dc.DrawEllipse(null, new Pen(WithOpacity(Ring, 0.28), Math.Max(0.35, num * 0.007)), center, num3, num3);
			DrawGem(dc, center.X, center.Y - num2, 0.0, -1.0, num);
			DrawGem(dc, center.X, center.Y + num2, 0.0, 1.0, num);
			DrawGem(dc, center.X - num2, center.Y, -1.0, 0.0, num);
			DrawGem(dc, center.X + num2, center.Y, 1.0, 0.0, num);
			Pen pen = new Pen(WithOpacity(Accent, 0.42), Math.Max(0.55, num * 0.012))
			{
				StartLineCap = PenLineCap.Round,
				EndLineCap = PenLineCap.Round
			};
			dc.DrawLine(pen, new Point(center.X - num2 * 0.62, center.Y - num2 * 0.72), new Point(center.X - num2 * 0.16, center.Y - num2 * 0.95));
			dc.DrawLine(pen, new Point(center.X + num2 * 0.22, center.Y - num2 * 0.93), new Point(center.X + num2 * 0.68, center.Y - num2 * 0.66));
			Pen pen2 = new Pen(WithOpacity(Ring, 0.72), Math.Max(0.65, num * 0.016))
			{
				StartLineCap = PenLineCap.Round,
				EndLineCap = PenLineCap.Round
			};
			StreamGeometry streamGeometry = new StreamGeometry();
			using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
			{
				streamGeometryContext.BeginFigure(new Point(center.X - num2 * 0.54, center.Y - num2 * 0.72), isFilled: false, isClosed: false);
				streamGeometryContext.BezierTo(new Point(center.X - num2 * 0.32, center.Y - num2 * 0.96), new Point(center.X + num2 * 0.18, center.Y - num2 * 1.02), new Point(center.X + num2 * 0.48, center.Y - num2 * 0.78), isStroked: true, isSmoothJoin: false);
			}
			streamGeometry.Freeze();
			dc.DrawGeometry(null, pen2, streamGeometry);
		}
	}

	private static Brush CreateIconSheenBrush()
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.0);
		linearGradientBrush.EndPoint = new Point(1.0, 1.0);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 49, 223, 242), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(48, 49, 223, 242), 0.22));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(24, 242, 190, 104), 0.42));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 216, 108, 239), 0.68));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}

	private void DrawGem(DrawingContext dc, double x, double y, double sx, double sy, double size)
	{
		double num = Math.Max(1.6, size * 0.032);
		Pen pen = new Pen(WithOpacity(Ring, 0.86), Math.Max(0.55, size * 0.01))
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		if (Math.Abs(sx) > 0.1)
		{
			dc.DrawLine(pen, new Point(x, y - num), new Point(x + sx * num * 1.8, y));
			dc.DrawLine(pen, new Point(x + sx * num * 1.8, y), new Point(x, y + num));
		}
		else
		{
			dc.DrawLine(pen, new Point(x - num, y), new Point(x, y + sy * num * 1.8));
			dc.DrawLine(pen, new Point(x, y + sy * num * 1.8), new Point(x + num, y));
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
