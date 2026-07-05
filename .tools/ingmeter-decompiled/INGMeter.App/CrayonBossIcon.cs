using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class CrayonBossIcon : FrameworkElement
{
	public static readonly DependencyProperty InkProperty = DependencyProperty.Register("Ink", typeof(Brush), typeof(CrayonBossIcon), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ScribbleProperty = DependencyProperty.Register("Scribble", typeof(Brush), typeof(CrayonBossIcon), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ShellProperty = DependencyProperty.Register("Shell", typeof(Brush), typeof(CrayonBossIcon), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public Brush Ink
	{
		get
		{
			return (Brush)GetValue(InkProperty);
		}
		set
		{
			SetValue(InkProperty, value);
		}
	}

	public Brush Scribble
	{
		get
		{
			return (Brush)GetValue(ScribbleProperty);
		}
		set
		{
			SetValue(ScribbleProperty, value);
		}
	}

	public Brush Shell
	{
		get
		{
			return (Brush)GetValue(ShellProperty);
		}
		set
		{
			SetValue(ShellProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 4.0) && !(actualHeight <= 4.0))
		{
			double num = Math.Min(actualWidth, actualHeight);
			Point center = new Point(actualWidth * 0.5, actualHeight * 0.5);
			double num2 = num * 0.48;
			dc.PushClip(new EllipseGeometry(center, num2, num2));
			Pen pen = new Pen(Ink, Math.Max(1.0, num * 0.035))
			{
				StartLineCap = PenLineCap.Round,
				EndLineCap = PenLineCap.Round,
				LineJoin = PenLineJoin.Round
			};
			Pen pen2 = new Pen(Scribble, Math.Max(0.8, num * 0.026))
			{
				StartLineCap = PenLineCap.Round,
				EndLineCap = PenLineCap.Round
			};
			Point center2 = new Point(center.X, center.Y - num * 0.13);
			double num3 = num * 0.27;
			dc.DrawEllipse(null, pen, center2, num3 * 1.08, num3);
			DrawScribbles(dc, pen2, center2.X - num3 * 0.76, center2.Y - num3 * 0.48, center2.X + num3 * 0.76, center2.Y + num3 * 0.54, 6);
			DrawEye(dc, pen, center2.X - num3 * 0.42, center2.Y - num3 * 0.14, num * 0.025);
			DrawEye(dc, pen, center2.X + num3 * 0.42, center2.Y - num3 * 0.13, num * 0.025);
			DrawMuzzle(dc, pen, center2.X, center2.Y + num3 * 0.32, num);
			Rect rectangle = new Rect(center.X - num * 0.21, center.Y + num * 0.11, num * 0.42, num * 0.3);
			dc.DrawRoundedRectangle(null, pen, rectangle, num * 0.08, num * 0.08);
			DrawScribbles(dc, pen2, rectangle.Left + num * 0.02, rectangle.Top + num * 0.03, rectangle.Right - num * 0.02, rectangle.Bottom - num * 0.03, 4);
			Rect rectangle2 = new Rect(center.X + num * 0.02, center.Y + num * 0.21, num * 0.15, num * 0.16);
			dc.DrawRoundedRectangle(Shell, pen, rectangle2, num * 0.04, num * 0.04);
			dc.DrawLine(pen, new Point(rectangle2.Left + rectangle2.Width * 0.5, rectangle2.Top + rectangle2.Height * 0.08), new Point(rectangle2.Left + rectangle2.Width * 0.5, rectangle2.Bottom - rectangle2.Height * 0.08));
			dc.Pop();
		}
	}

	private static void DrawScribbles(DrawingContext dc, Pen pen, double left, double top, double right, double bottom, int count)
	{
		Math.Max(1.0, right - left);
		double num = Math.Max(1.0, bottom - top);
		for (int i = 0; i < count; i++)
		{
			double num2 = top + num * ((double)i + 0.5) / (double)count;
			CrayonDoodleFrame.DrawSketchLine(dc, pen, new Point(left, num2), new Point(right, num2 + Math.Sin(i) * num * 0.15), 5, (double)i * 0.6);
		}
	}

	private static void DrawEye(DrawingContext dc, Pen ink, double x, double y, double size)
	{
		dc.DrawEllipse(null, ink, new Point(x, y), size * 1.65, size);
		dc.DrawEllipse(ink.Brush, null, new Point(x + size * 0.18, y + size * 0.04), size * 0.58, size * 0.48);
	}

	private static void DrawMuzzle(DrawingContext dc, Pen ink, double x, double y, double size)
	{
		dc.DrawEllipse(null, ink, new Point(x - size * 0.04, y), size * 0.047, size * 0.06);
		dc.DrawEllipse(null, ink, new Point(x + size * 0.04, y), size * 0.047, size * 0.06);
		dc.DrawEllipse(ink.Brush, null, new Point(x, y - size * 0.045), size * 0.02, size * 0.016);
	}
}
