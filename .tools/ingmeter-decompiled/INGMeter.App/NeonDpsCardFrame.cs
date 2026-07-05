using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class NeonDpsCardFrame : FrameworkElement
{
	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(NeonDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(NeonDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(NeonDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Brush), typeof(NeonDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty CircuitProperty = DependencyProperty.Register("Circuit", typeof(Brush), typeof(NeonDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty ShowInnerProperty = DependencyProperty.Register("ShowInner", typeof(bool), typeof(NeonDpsCardFrame), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

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

	public Brush Circuit
	{
		get
		{
			return (Brush)GetValue(CircuitProperty);
		}
		set
		{
			SetValue(CircuitProperty, value);
		}
	}

	public bool ShowInner
	{
		get
		{
			return (bool)GetValue(ShowInnerProperty);
		}
		set
		{
			SetValue(ShowInnerProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 4.0) && !(actualHeight <= 4.0))
		{
			Rect rect = new Rect(1.2, 1.2, actualWidth - 2.4, actualHeight - 2.4);
			StreamGeometry streamGeometry = CreateFrame(rect);
			Brush brush = WithOpacity(Glow, 0.38);
			Brush brush2 = WithOpacity(Circuit, 0.56);
			dc.DrawGeometry(null, new Pen(brush, 3.6), streamGeometry);
			dc.DrawGeometry(null, new Pen(WithOpacity(Stroke, 0.36), 1.6), streamGeometry);
			dc.DrawGeometry(Fill, new Pen(Stroke, 0.95), streamGeometry);
			dc.PushClip(streamGeometry);
			DrawScanLines(dc, WithOpacity(Circuit, 0.2), rect);
			DrawCircuit(dc, brush2, rect);
			dc.Pop();
			if (ShowInner)
			{
				dc.DrawGeometry(geometry: CreateFrame(new Rect(rect.Left + 4.2, rect.Top + 4.2, Math.Max(1.0, rect.Width - 8.4), Math.Max(1.0, rect.Height - 8.4))), brush: null, pen: new Pen(WithOpacity(Accent, 0.52), 0.45));
			}
			DrawCornerBrackets(dc, Stroke, Accent, rect);
		}
	}

	private static StreamGeometry CreateFrame(Rect rect)
	{
		double num = Math.Clamp(rect.Height * 0.13, 3.5, 6.5);
		double num2 = num * 1.05;
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(rect.Left + num, rect.Top), isFilled: true, isClosed: true);
			streamGeometryContext.LineTo(new Point(rect.Right - num2, rect.Top), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Right, rect.Top + num), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Right, rect.Bottom - num), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Right - num2, rect.Bottom), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Left + num, rect.Bottom), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Left, rect.Bottom - num), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Left, rect.Top + num), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}

	private static void DrawCornerBrackets(DrawingContext dc, Brush stroke, Brush accent, Rect rect)
	{
		double num = Math.Clamp(rect.Height * 0.36, 10.0, 17.0);
		double num2 = Math.Clamp(rect.Height * 0.2, 6.0, 10.0);
		Pen pen = new Pen(WithOpacity(accent, 0.82), 0.95)
		{
			StartLineCap = PenLineCap.Square,
			EndLineCap = PenLineCap.Square
		};
		Pen pen2 = new Pen(WithOpacity(stroke, 0.7), 0.75)
		{
			StartLineCap = PenLineCap.Square,
			EndLineCap = PenLineCap.Square
		};
		dc.DrawLine(pen, new Point(rect.Left + num2 + 2.0, rect.Top + 2.0), new Point(rect.Left + num2 + num, rect.Top + 2.0));
		dc.DrawLine(pen, new Point(rect.Right - num2 - 2.0, rect.Bottom - 2.0), new Point(rect.Right - num2 - num, rect.Bottom - 2.0));
		dc.DrawLine(pen2, new Point(rect.Right - num, rect.Top + 2.0), new Point(rect.Right - 5.0, rect.Top + 2.0));
		dc.DrawLine(pen2, new Point(rect.Left + 5.0, rect.Bottom - 2.0), new Point(rect.Left + num, rect.Bottom - 2.0));
	}

	private static void DrawCircuit(DrawingContext dc, Brush brush, Rect rect)
	{
		if (!(rect.Width < 150.0))
		{
			Pen pen = new Pen(brush, 0.45)
			{
				StartLineCap = PenLineCap.Square,
				EndLineCap = PenLineCap.Square
			};
			double num = rect.Top + rect.Height * 0.36;
			double num2 = rect.Left + rect.Width * 0.42;
			double num3 = rect.Left + rect.Width * 0.63;
			double num4 = rect.Right - Math.Min(42.0, rect.Width * 0.1);
			dc.DrawLine(pen, new Point(num2, num), new Point(num3, num));
			dc.DrawLine(pen, new Point(num3, num), new Point(num3 + 10.0, num - 8.0));
			dc.DrawLine(pen, new Point(num3 + 10.0, num - 8.0), new Point(num4, num - 8.0));
			dc.DrawLine(pen, new Point(num2 + 18.0, rect.Bottom - 9.0), new Point(num4 - 24.0, rect.Bottom - 9.0));
			dc.DrawEllipse(brush, null, new Point(num2 - 8.0, num), 1.2, 1.2);
			dc.DrawEllipse(brush, null, new Point(num4 + 5.0, num - 8.0), 1.1, 1.1);
		}
	}

	private static void DrawScanLines(DrawingContext dc, Brush brush, Rect rect)
	{
		Pen pen = new Pen(brush, 0.32);
		for (double num = rect.Top + 4.0; num < rect.Bottom - 2.0; num += 5.0)
		{
			dc.DrawLine(pen, new Point(rect.Left + 6.0, num), new Point(rect.Right - 8.0, num));
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
