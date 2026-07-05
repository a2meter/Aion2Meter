using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class NeonBossEmblem : FrameworkElement
{
	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(NeonBossEmblem), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(NeonBossEmblem), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Brush), typeof(NeonBossEmblem), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(NeonBossEmblem), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty EyeProperty = DependencyProperty.Register("Eye", typeof(Brush), typeof(NeonBossEmblem), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

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

	public Brush Eye
	{
		get
		{
			return (Brush)GetValue(EyeProperty);
		}
		set
		{
			SetValue(EyeProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double num = Math.Min(base.ActualWidth, base.ActualHeight);
		if (!(num <= 6.0))
		{
			double num2 = base.ActualWidth * 0.5;
			double num3 = base.ActualHeight * 0.52;
			double num4 = num * 0.45;
			Pen pen = new Pen(WithOpacity(Glow, 0.5), Math.Max(2.0, num * 0.085));
			Pen pen2 = new Pen(Stroke, Math.Max(1.0, num * 0.032));
			Pen pen3 = new Pen(WithOpacity(Accent, 0.86), Math.Max(0.7, num * 0.018));
			dc.DrawEllipse(null, pen, new Point(num2, num3), num4, num4);
			dc.DrawEllipse(WithOpacity(Fill, 0.84), pen2, new Point(num2, num3), num4 * 0.94, num4 * 0.94);
			dc.DrawEllipse(null, new Pen(WithOpacity(Stroke, 0.48), Math.Max(0.7, num * 0.018)), new Point(num2, num3), num4 * 0.78, num4 * 0.78);
			dc.DrawEllipse(null, pen3, new Point(num2, num3), num4 * 0.62, num4 * 0.62);
			DrawOrbit(dc, num2, num3, num4);
			StreamGeometry geometry = CreateHead(num2, num3, num4);
			dc.DrawGeometry(null, new Pen(WithOpacity(Glow, 0.38), Math.Max(1.4, num * 0.045)), geometry);
			dc.DrawGeometry(CreateMaskFill(), new Pen(Stroke, Math.Max(0.9, num * 0.024)), geometry);
			DrawHorn(dc, num2, num3, num4, -1.0);
			DrawHorn(dc, num2, num3, num4, 1.0);
			DrawFaceLines(dc, num2, num3, num4);
			DrawEyes(dc, num2, num3, num4);
			DrawCornerMarks(dc, num2, num3, num4);
			DrawTicks(dc, num2, num3, num4);
		}
	}

	private void DrawHorn(DrawingContext dc, double cx, double cy, double r, double side)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(cx + side * r * 0.1, cy - r * 0.33), isFilled: false, isClosed: false);
			streamGeometryContext.BezierTo(new Point(cx + side * r * 0.36, cy - r * 0.93), new Point(cx + side * r * 0.88, cy - r * 0.76), new Point(cx + side * r * 0.68, cy - r * 0.2), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		dc.DrawGeometry(null, new Pen(WithOpacity(Glow, 0.38), Math.Max(1.5, r * 0.1))
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		}, streamGeometry);
		dc.DrawGeometry(null, new Pen(WithOpacity(Accent, 0.94), Math.Max(0.9, r * 0.05))
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		}, streamGeometry);
		dc.DrawGeometry(null, new Pen(WithOpacity(Stroke, 0.46), Math.Max(0.55, r * 0.026))
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		}, streamGeometry);
	}

	private void DrawEyes(DrawingContext dc, double cx, double cy, double r)
	{
		Pen pen = new Pen(WithOpacity(Eye, 0.9), Math.Max(1.0, r * 0.055))
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		Pen pen2 = new Pen(WithOpacity(Eye, 0.36), Math.Max(1.8, r * 0.12))
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		Point point = new Point(cx - r * 0.38, cy - r * 0.04);
		Point point2 = new Point(cx - r * 0.14, cy + r * 0.07);
		Point point3 = new Point(cx + r * 0.38, cy - r * 0.04);
		Point point4 = new Point(cx + r * 0.14, cy + r * 0.07);
		dc.DrawLine(pen2, point, point2);
		dc.DrawLine(pen2, point3, point4);
		dc.DrawLine(pen, point, point2);
		dc.DrawLine(pen, point3, point4);
		dc.DrawEllipse(WithOpacity(Eye, 0.78), null, new Point(cx - r * 0.24, cy + r * 0.02), r * 0.045, r * 0.03);
		dc.DrawEllipse(WithOpacity(Eye, 0.78), null, new Point(cx + r * 0.24, cy + r * 0.02), r * 0.045, r * 0.03);
	}

	private void DrawTicks(DrawingContext dc, double cx, double cy, double r)
	{
		Pen pen = new Pen(WithOpacity(Stroke, 0.72), Math.Max(0.65, r * 0.02));
		for (int i = 0; i < 28; i++)
		{
			double num = (double)i * Math.PI * 2.0 / 28.0;
			double num2 = ((i % 4 == 0) ? (r * 0.12) : (r * 0.055));
			Point point = new Point(cx + Math.Cos(num) * (r * 1.02), cy + Math.Sin(num) * (r * 1.02));
			Point point2 = new Point(cx + Math.Cos(num) * (r * 1.02 - num2), cy + Math.Sin(num) * (r * 1.02 - num2));
			dc.DrawLine(pen, point, point2);
		}
	}

	private void DrawOrbit(DrawingContext dc, double cx, double cy, double r)
	{
		Pen pen = new Pen(WithOpacity(Stroke, 0.8), Math.Max(0.7, r * 0.026));
		Pen pen2 = new Pen(WithOpacity(Accent, 0.82), Math.Max(0.7, r * 0.026));
		dc.DrawArc(pen, new Point(cx - r * 0.82, cy + r * 0.14), new Point(cx + r * 0.5, cy + r * 0.72), r * 0.96, r * 0.78, isLargeArc: false);
		dc.DrawArc(pen2, new Point(cx + r * 0.78, cy - r * 0.22), new Point(cx - r * 0.54, cy - r * 0.76), r * 0.98, r * 0.78, isLargeArc: false);
	}

	private void DrawFaceLines(DrawingContext dc, double cx, double cy, double r)
	{
		Pen pen = new Pen(WithOpacity(Stroke, 0.55), Math.Max(0.55, r * 0.02))
		{
			StartLineCap = PenLineCap.Square,
			EndLineCap = PenLineCap.Square
		};
		dc.DrawLine(pen, new Point(cx, cy - r * 0.42), new Point(cx, cy + r * 0.42));
		dc.DrawLine(pen, new Point(cx - r * 0.32, cy - r * 0.2), new Point(cx - r * 0.1, cy - r * 0.04));
		dc.DrawLine(pen, new Point(cx + r * 0.32, cy - r * 0.2), new Point(cx + r * 0.1, cy - r * 0.04));
		dc.DrawLine(pen, new Point(cx - r * 0.22, cy + r * 0.22), new Point(cx, cy + r * 0.38));
		dc.DrawLine(pen, new Point(cx + r * 0.22, cy + r * 0.22), new Point(cx, cy + r * 0.38));
	}

	private void DrawCornerMarks(DrawingContext dc, double cx, double cy, double r)
	{
		Pen pen = new Pen(WithOpacity(Accent, 0.9), Math.Max(0.8, r * 0.03))
		{
			StartLineCap = PenLineCap.Square,
			EndLineCap = PenLineCap.Square
		};
		Pen pen2 = new Pen(WithOpacity(Stroke, 0.82), Math.Max(0.7, r * 0.026))
		{
			StartLineCap = PenLineCap.Square,
			EndLineCap = PenLineCap.Square
		};
		double num = cx - r * 1.02;
		double num2 = cx + r * 1.02;
		double num3 = cy - r * 1.02;
		double y = cy + r * 1.02;
		double num4 = r * 0.22;
		dc.DrawLine(pen, new Point(num, num3 + num4), new Point(num, num3 + num4 * 0.42));
		dc.DrawLine(pen, new Point(num, num3), new Point(num + num4, num3));
		dc.DrawLine(pen2, new Point(num2 - num4, num3), new Point(num2, num3));
		dc.DrawLine(pen2, new Point(num2, num3), new Point(num2, num3 + num4));
		dc.DrawLine(pen, new Point(num + r * 0.12, y), new Point(num + r * 0.36, y));
		dc.DrawLine(pen2, new Point(num2 - r * 0.36, y), new Point(num2 - r * 0.12, y));
	}

	private static StreamGeometry CreateHead(double cx, double cy, double r)
	{
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(cx, cy - r * 0.58), isFilled: true, isClosed: true);
			streamGeometryContext.LineTo(new Point(cx + r * 0.48, cy - r * 0.26), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(cx + r * 0.4, cy + r * 0.22), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(cx + r * 0.16, cy + r * 0.56), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(cx, cy + r * 0.68), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(cx - r * 0.16, cy + r * 0.56), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(cx - r * 0.4, cy + r * 0.22), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(cx - r * 0.48, cy - r * 0.26), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}

	private static Brush WithOpacity(Brush brush, double opacity)
	{
		Brush brush2 = brush.CloneCurrentValue();
		brush2.Opacity *= opacity;
		brush2.Freeze();
		return brush2;
	}

	private static Brush CreateMaskFill()
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.0);
		linearGradientBrush.EndPoint = new Point(1.0, 1.0);
		linearGradientBrush.MappingMode = BrushMappingMode.RelativeToBoundingBox;
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(240, 24, 11, 52), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(216, 66, 22, 118), 0.55));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(240, 7, 21, 46), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}
}
