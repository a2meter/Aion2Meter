using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class CrayonPaperBackdrop : FrameworkElement
{
	public static readonly DependencyProperty InkProperty = DependencyProperty.Register("Ink", typeof(Brush), typeof(CrayonPaperBackdrop), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ScribbleProperty = DependencyProperty.Register("Scribble", typeof(Brush), typeof(CrayonPaperBackdrop), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(CrayonPaperBackdrop), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ShellProperty = DependencyProperty.Register("Shell", typeof(Brush), typeof(CrayonPaperBackdrop), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

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
		if (!(actualWidth <= 20.0) && !(actualHeight <= 20.0))
		{
			Pen pen = new Pen(Accent, 1.0)
			{
				StartLineCap = PenLineCap.Round,
				EndLineCap = PenLineCap.Round
			};
			CrayonDoodleFrame.DrawWave(dc, pen, 24.0, 20.0, actualWidth - 24.0, 20.0, Math.Max(8, (int)(actualWidth / 62.0)));
			CrayonDoodleFrame.DrawWave(dc, pen, 22.0, actualHeight - 18.0, actualWidth - 22.0, actualHeight - 18.0, Math.Max(8, (int)(actualWidth / 58.0)));
			dc.PushOpacity(0.22);
			DrawFigure(dc, actualWidth * 0.59, actualHeight * 0.5, Math.Min(actualWidth, actualHeight) * 0.54, compact: false);
			dc.Pop();
			if (actualWidth > 250.0 && actualHeight > 125.0)
			{
				dc.PushOpacity(0.78);
				DrawFigure(dc, actualWidth - 41.0, actualHeight - 37.0, Math.Min(actualWidth, actualHeight) * 0.18, compact: true);
				dc.Pop();
			}
			DrawDecorations(dc, actualWidth, actualHeight, pen);
		}
	}

	private void DrawDecorations(DrawingContext dc, double width, double height, Pen line)
	{
		CrayonDoodleFrame.DrawStar(dc, line, width * 0.13, height * 0.78, 5.0);
		CrayonDoodleFrame.DrawStar(dc, line, width * 0.86, height * 0.17, 4.5);
		CrayonDoodleFrame.DrawPaw(dc, Accent, width * 0.91, height * 0.88, 6.5);
		CrayonDoodleFrame.DrawPaw(dc, Accent, width * 0.2, height * 0.23, 4.8);
		CrayonDoodleFrame.DrawFish(dc, line, width * 0.45, height - 15.0, 8.0);
	}

	private void DrawFigure(DrawingContext dc, double x, double y, double size, bool compact)
	{
		double num = Math.Max(12.0, size);
		Pen pen = new Pen(Ink, compact ? 1.15 : 1.45)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round,
			LineJoin = PenLineJoin.Round
		};
		Pen pen2 = new Pen(Scribble, compact ? 0.8 : 1.1)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		double num2 = num * (compact ? 0.29 : 0.32);
		Point center = new Point(x - num * 0.02, y - num * 0.18);
		DrawLooseOval(dc, null, pen, center, num2 * 1.08, num2 * 0.95, compact ? 1.7 : 0.4);
		DrawScribbles(dc, pen2, center.X - num2 * 0.83, center.Y - num2 * 0.53, center.X + num2 * 0.85, center.Y + num2 * 0.58, compact ? 7 : 14, compact ? 0.18 : 0.24);
		double num3 = center.Y - num2 * 0.12;
		DrawEye(dc, pen, center.X - num2 * 0.45, num3 + num * 0.006, num * 0.035);
		DrawEye(dc, pen, center.X + num2 * 0.42, num3 - num * 0.014, num * 0.037);
		DrawMuzzle(dc, pen, center.X - num * 0.01, center.Y + num2 * 0.33, num);
		Point[] points = new Point[7]
		{
			new Point(x - num * 0.25, y + num * 0.05),
			new Point(x + num * 0.19, y + num * 0.06),
			new Point(x + num * 0.27, y + num * 0.21),
			new Point(x + num * 0.24, y + num * 0.47),
			new Point(x + num * 0.08, y + num * 0.51),
			new Point(x - num * 0.21, y + num * 0.46),
			new Point(x - num * 0.28, y + num * 0.23)
		};
		DrawLoosePolygon(dc, pen, points, compact ? 1.9 : 0.8);
		DrawScribbles(dc, pen2, x - num * 0.25, y + num * 0.1, x + num * 0.23, y + num * 0.46, compact ? 6 : 10, compact ? 0.16 : 0.21);
		DrawArm(dc, pen, x - num * 0.11, y + num * 0.24, x - num * 0.29, y + num * 0.31, num);
		DrawArm(dc, pen, x + num * 0.09, y + num * 0.23, x + num * 0.24, y + num * 0.3, num);
		DrawLeg(dc, pen, x - num * 0.13, y + num * 0.48, x - num * 0.21, y + num * 0.72, num);
		DrawLeg(dc, pen, x + num * 0.12, y + num * 0.48, x + num * 0.21, y + num * 0.69, num);
		Point center2 = new Point(x + num * 0.13, y + num * 0.34);
		DrawLooseOval(dc, Shell, pen, center2, num * 0.085, num * 0.145, compact ? 2.6 : 1.1);
		Pen pen3 = new Pen(Ink, compact ? 0.75 : 0.95)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		CrayonDoodleFrame.DrawSketchLine(dc, pen3, new Point(center2.X, center2.Y - num * 0.12), new Point(center2.X + num * 0.01, center2.Y + num * 0.12), 4, num * 0.02);
		CrayonDoodleFrame.DrawSketchLine(dc, pen3, new Point(center2.X - num * 0.06, center2.Y - num * 0.02), new Point(center2.X + num * 0.06, center2.Y - num * 0.06), 3, num * 0.03);
		CrayonDoodleFrame.DrawSketchLine(dc, pen3, new Point(center2.X - num * 0.055, center2.Y + num * 0.04), new Point(center2.X + num * 0.055, center2.Y + num * 0.07), 3, num * 0.04);
	}

	private static void DrawScribbles(DrawingContext dc, Pen pen, double left, double top, double right, double bottom, int count, double amplitude)
	{
		double num = Math.Max(1.0, right - left);
		double num2 = Math.Max(1.0, bottom - top);
		for (int i = 0; i < count; i++)
		{
			double num3 = top + num2 * ((double)i + 0.5) / (double)count;
			double num4 = num2 * amplitude;
			Point start = new Point(left - num * (0.08 + (double)(i % 3) * 0.04), num3 + Math.Sin((double)i * 1.9) * num4);
			Point end = new Point(right + num * (0.04 + (double)(i % 2) * 0.06), num3 + Math.Cos((double)i * 2.2) * num4);
			CrayonDoodleFrame.DrawSketchLine(dc, pen, start, end, 4, (double)i * 0.83);
		}
	}

	private static void DrawEye(DrawingContext dc, Pen ink, double x, double y, double size)
	{
		for (int i = 0; i < 3; i++)
		{
			double num = (double)(i - 1) * size * 0.38;
			CrayonDoodleFrame.DrawSketchLine(dc, ink, new Point(x - size * 1.25, y + num), new Point(x + size * 1.05, y - num * 0.7), 3, (double)i * 0.9);
		}
		dc.DrawEllipse(ink.Brush, null, new Point(x + size * 0.2, y), size * 0.58, size * 0.48);
	}

	private static void DrawMuzzle(DrawingContext dc, Pen ink, double x, double y, double size)
	{
		DrawLooseOval(dc, null, ink, new Point(x - size * 0.05, y + size * 0.012), size * 0.06, size * 0.083, size * 0.015);
		DrawLooseOval(dc, null, ink, new Point(x + size * 0.05, y + size * 0.02), size * 0.055, size * 0.078, size * 0.025);
		dc.DrawEllipse(ink.Brush, null, new Point(x, y - size * 0.06), size * 0.026, size * 0.02);
		dc.DrawLine(ink, new Point(x - size * 0.018, y - size * 0.03), new Point(x - size * 0.045, y + size * 0.035));
		dc.DrawLine(ink, new Point(x + size * 0.018, y - size * 0.03), new Point(x + size * 0.045, y + size * 0.035));
	}

	private static void DrawLooseOval(DrawingContext dc, Brush? fill, Pen pen, Point center, double rx, double ry, double phase)
	{
		Point[] array = new Point[14];
		for (int i = 0; i < array.Length; i++)
		{
			double num = Math.PI * 2.0 * (double)i / (double)array.Length;
			double num2 = 1.0 + Math.Sin((double)i * 1.7 + phase) * 0.08 + Math.Cos((double)i * 2.1 + phase) * 0.05;
			array[i] = new Point(center.X + Math.Cos(num) * rx * num2, center.Y + Math.Sin(num) * ry * num2);
		}
		if (fill != null)
		{
			dc.DrawGeometry(fill, null, CreatePolygon(array));
		}
		DrawLoosePolygon(dc, pen, array, phase);
	}

	private static void DrawLoosePolygon(DrawingContext dc, Pen pen, Point[] points, double phase)
	{
		for (int i = 0; i < points.Length; i++)
		{
			CrayonDoodleFrame.DrawSketchLine(dc, pen, points[i], points[(i + 1) % points.Length], 2, phase + (double)i * 0.43);
		}
	}

	private static StreamGeometry CreatePolygon(Point[] points)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(points[0], isFilled: true, isClosed: true);
			for (int i = 1; i < points.Length; i++)
			{
				streamGeometryContext.LineTo(points[i], isStroked: true, isSmoothJoin: false);
			}
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}

	private static void DrawArm(DrawingContext dc, Pen ink, double x1, double y1, double x2, double y2, double size)
	{
		CrayonDoodleFrame.DrawSketchLine(dc, ink, new Point(x1, y1), new Point(x2, y2), 4, size * 0.01);
	}

	private static void DrawLeg(DrawingContext dc, Pen ink, double x1, double y1, double x2, double y2, double size)
	{
		CrayonDoodleFrame.DrawSketchLine(dc, ink, new Point(x1, y1), new Point(x2, y2), 5, size * 0.02);
	}
}
