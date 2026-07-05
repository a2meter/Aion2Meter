using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class CrayonDoodleFrame : FrameworkElement
{
	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(CrayonDoodleFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(CrayonDoodleFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(CrayonDoodleFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ShowDoodlesProperty = DependencyProperty.Register("ShowDoodles", typeof(bool), typeof(CrayonDoodleFrame), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty SketchPhaseProperty = DependencyProperty.Register("SketchPhase", typeof(double), typeof(CrayonDoodleFrame), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

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

	public Brush Stroke
	{
		get
		{
			return (Brush)GetValue(StrokeProperty);
		}
		set
		{
			SetValue(StrokeProperty, value);
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

	public bool ShowDoodles
	{
		get
		{
			return (bool)GetValue(ShowDoodlesProperty);
		}
		set
		{
			SetValue(ShowDoodlesProperty, value);
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
		if (!(actualWidth <= 3.0) && !(actualHeight <= 3.0))
		{
			double num = Math.Clamp(actualHeight * 0.28, 7.0, 18.0);
			Rect rect = new Rect(1.0, 1.0, actualWidth - 2.0, actualHeight - 2.0);
			dc.DrawRoundedRectangle(Fill, null, rect, num, num);
			double sketchPhase = SketchPhase;
			DrawLooseFrame(dc, rect, num, Stroke, 1.25, sketchPhase);
			DrawLooseFrame(dc, new Rect(4.0, 4.0, actualWidth - 8.0, Math.Max(1.0, actualHeight - 8.0)), Math.Max(2.0, num - 3.0), Accent, 0.75, sketchPhase + 1.0);
			if (ShowDoodles && actualWidth > 150.0 && actualHeight > 70.0)
			{
				DrawDoodles(dc, actualWidth, actualHeight, sketchPhase);
			}
		}
	}

	private void DrawDoodles(DrawingContext dc, double width, double height, double phase)
	{
		Pen pen = new Pen(Accent, 0.95)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		DrawStar(dc, pen, width * 0.18, height * 0.17, 6.0);
		DrawStar(dc, pen, width * 0.63, height * 0.12, 4.0);
		DrawWave(dc, pen, width * 0.35, height * 0.16 + Math.Sin(phase) * 0.8, width * 0.46, height * 0.16, 7);
		DrawPaw(dc, Accent, width * 0.83, height * 0.86, 7.0);
		DrawFish(dc, pen, width * 0.47, height * 0.91, 9.0);
	}

	private static void DrawLooseFrame(DrawingContext dc, Rect rect, double radius, Brush brush, double thickness, double phase)
	{
		Pen pen = new Pen(brush, thickness)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round,
			LineJoin = PenLineJoin.Round
		};
		double num = rect.Left + radius;
		double num2 = rect.Right - radius;
		double top = rect.Top;
		double bottom = rect.Bottom;
		double num3 = radius * (0.2 + Jitter(phase, 0.7, 0.08));
		double num4 = radius * (0.34 + Jitter(phase, 1.3, 0.1));
		double num5 = radius * (0.34 + Jitter(phase, 2.1, 0.12));
		double num6 = radius * (0.2 + Jitter(phase, 2.9, 0.08));
		double num7 = radius * (0.34 + Jitter(phase, 3.7, 0.1));
		double num8 = radius * (0.34 + Jitter(phase, 4.5, 0.12));
		DrawSketchLine(dc, pen, new Point(num + Jitter(phase, 5.1, 2.0), top), new Point(num2 + Jitter(phase, 5.9, 2.0), top), 12, phase);
		DrawSketchLine(dc, pen, new Point(num2 + num5, top + num3), new Point(rect.Right + Jitter(phase, 6.7, 1.8), bottom - num4), 9, phase + 0.7);
		DrawSketchLine(dc, pen, new Point(num2 + Jitter(phase, 7.5, 2.0), bottom), new Point(num + Jitter(phase, 8.3, 2.0), bottom), 12, phase + 1.1);
		DrawSketchLine(dc, pen, new Point(rect.Left + Jitter(phase, 9.1, 1.8), bottom - num7), new Point(num - num8, top + num6), 9, phase + 1.8);
	}

	private static double Jitter(double phase, double salt, double amount)
	{
		return Math.Sin(phase * 2.37 + salt * 5.11) * amount;
	}

	internal static void DrawSketchLine(DrawingContext dc, Pen pen, Point start, Point end, int segments, double phase)
	{
		Vector vector = end - start;
		if (!(vector.LengthSquared < 0.01))
		{
			Vector vector2 = new Vector(0.0 - vector.Y, vector.X);
			vector2.Normalize();
			Point point = start;
			for (int i = 1; i <= segments; i++)
			{
				double num = (double)i / (double)segments;
				double num2 = Math.Sin(((double)i + phase) * 1.74) * 1.2;
				Point point2 = start + vector * num + vector2 * num2;
				dc.DrawLine(pen, point, point2);
				point = point2;
			}
		}
	}

	internal static void DrawStar(DrawingContext dc, Pen pen, double x, double y, double size)
	{
		dc.DrawLine(pen, new Point(x - size, y), new Point(x + size, y));
		dc.DrawLine(pen, new Point(x, y - size), new Point(x, y + size));
		dc.DrawLine(pen, new Point(x - size * 0.55, y - size * 0.55), new Point(x + size * 0.55, y + size * 0.55));
		dc.DrawLine(pen, new Point(x - size * 0.55, y + size * 0.55), new Point(x + size * 0.55, y - size * 0.55));
	}

	internal static void DrawWave(DrawingContext dc, Pen pen, double x1, double y1, double x2, double y2, int arcs)
	{
		double num = (x2 - x1) / (double)Math.Max(1, arcs);
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(x1, y1), isFilled: false, isClosed: false);
			for (int i = 0; i < arcs; i++)
			{
				double num2 = x1 + num * (double)i;
				double x3 = num2 + num;
				double x4 = num2 + num * 0.5;
				double num3 = ((i % 2 == 0) ? (-1.0) : 1.0) * 3.0;
				streamGeometryContext.QuadraticBezierTo(new Point(x4, y1 + num3), new Point(x3, y2), isStroked: true, isSmoothJoin: false);
			}
		}
		streamGeometry.Freeze();
		dc.DrawGeometry(null, pen, streamGeometry);
	}

	internal static void DrawPaw(DrawingContext dc, Brush brush, double x, double y, double size)
	{
		dc.DrawEllipse(brush, null, new Point(x, y + size * 0.12), size * 0.38, size * 0.3);
		dc.DrawEllipse(brush, null, new Point(x - size * 0.42, y - size * 0.2), size * 0.16, size * 0.18);
		dc.DrawEllipse(brush, null, new Point(x, y - size * 0.32), size * 0.16, size * 0.2);
		dc.DrawEllipse(brush, null, new Point(x + size * 0.42, y - size * 0.2), size * 0.16, size * 0.18);
	}

	internal static void DrawFish(DrawingContext dc, Pen pen, double x, double y, double size)
	{
		dc.DrawEllipse(null, pen, new Point(x, y), size * 0.62, size * 0.32);
		dc.DrawLine(pen, new Point(x - size * 0.62, y), new Point(x - size * 1.02, y - size * 0.28));
		dc.DrawLine(pen, new Point(x - size * 0.62, y), new Point(x - size * 1.02, y + size * 0.28));
		dc.DrawLine(pen, new Point(x + size * 0.28, y - size * 0.08), new Point(x + size * 0.36, y - size * 0.08));
	}
}
