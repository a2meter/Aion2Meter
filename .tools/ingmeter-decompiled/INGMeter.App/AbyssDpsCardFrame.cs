using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class AbyssDpsCardFrame : FrameworkElement
{
	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(AbyssDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(AbyssDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(AbyssDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Brush), typeof(AbyssDpsCardFrame), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

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

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 4.0) && !(actualHeight <= 4.0))
		{
			double num = Math.Clamp(actualHeight * 0.18, 5.0, 12.0);
			Rect rect = Pixel(new Rect(1.1, 1.1, actualWidth - 2.2, actualHeight - 2.2));
			Rect rectangle = Pixel(new Rect(4.0, 4.0, Math.Max(1.0, actualWidth - 8.0), Math.Max(1.0, actualHeight - 8.0)));
			dc.DrawRoundedRectangle(Fill, null, rect, num, num);
			dc.DrawRoundedRectangle(null, new Pen(WithOpacity(Stroke, 0.48), 0.55), rect, num, num);
			dc.DrawRoundedRectangle(null, new Pen(WithOpacity(Glow, 0.3), 0.35), rect, num, num);
			dc.DrawRoundedRectangle(null, new Pen(WithOpacity(Accent, 0.16), 0.35), rectangle, Math.Max(1.0, num - 3.0), Math.Max(1.0, num - 3.0));
			dc.PushClip(new RectangleGeometry(rect, num, num));
			DrawClassLight(dc, rect);
			DrawJobAura(dc, rect);
			DrawGlass(dc, rect);
			DrawSeparator(dc, rect);
			dc.Pop();
			DrawCorner(dc, rect.Left + 5.0, rect.Top + 5.0, 1.0, 1.0, actualHeight);
			DrawCorner(dc, rect.Right - 5.0, rect.Top + 5.0, -1.0, 1.0, actualHeight);
			DrawCorner(dc, rect.Left + 5.0, rect.Bottom - 5.0, 1.0, -1.0, actualHeight);
			DrawCorner(dc, rect.Right - 5.0, rect.Bottom - 5.0, -1.0, -1.0, actualHeight);
		}
	}

	private void DrawGlass(DrawingContext dc, Rect rect)
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.5),
			EndPoint = new Point(1.0, 0.5)
		};
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 49, 223, 242), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(112, 49, 223, 242), 0.12));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(88, 216, 108, 239), 0.46));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(58, 242, 190, 104), 0.72));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 49, 223, 242), 1.0));
		linearGradientBrush.Freeze();
		dc.DrawRectangle(linearGradientBrush, null, new Rect(rect.Left + 10.0, rect.Top + 1.0, Math.Max(0.0, rect.Width - 20.0), 0.85));
		LinearGradientBrush linearGradientBrush2 = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.0),
			EndPoint = new Point(0.0, 1.0)
		};
		linearGradientBrush2.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0));
		linearGradientBrush2.GradientStops.Add(new GradientStop(Color.FromArgb(130, 0, 0, 0), 1.0));
		linearGradientBrush2.Freeze();
		dc.DrawRectangle(linearGradientBrush2, null, new Rect(rect.Left, rect.Top + rect.Height * 0.48, rect.Width, rect.Height * 0.52));
		Pen pen = new Pen(WithOpacity(Accent, 0.18), 0.45)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		dc.DrawLine(pen, new Point(rect.Left + 48.0, rect.Top + rect.Height * 0.2), new Point(rect.Right - 42.0, rect.Top + rect.Height * 0.14));
	}

	private void DrawJobAura(DrawingContext dc, Rect rect)
	{
		Pen pen = new Pen(WithOpacity(Glow, 0.46), 0.46)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		Pen pen2 = new Pen(WithOpacity(Glow, 0.18), 0.4)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		Pen pen3 = new Pen(WithOpacity(Glow, 0.3), 0.4)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		dc.DrawLine(pen, new Point(rect.Left + 8.0, rect.Top + 1.6), new Point(rect.Right - 8.0, rect.Top + 1.4));
		dc.DrawLine(pen2, new Point(rect.Left + 9.0, rect.Bottom - 1.7), new Point(rect.Right - 9.0, rect.Bottom - 1.6));
		dc.DrawLine(pen3, new Point(rect.Left + 1.3, rect.Top + 10.0), new Point(rect.Left + 1.3, rect.Bottom - 10.0));
	}

	private void DrawClassLight(DrawingContext dc, Rect rect)
	{
		dc.PushOpacityMask(CreateClassLightMask());
		dc.DrawRectangle(WithOpacity(Glow, 0.12), null, rect);
		dc.Pop();
		dc.PushOpacityMask(CreateClassEdgeLightMask());
		dc.DrawRectangle(WithOpacity(Glow, 0.16), null, rect);
		dc.Pop();
	}

	private void DrawSeparator(DrawingContext dc, Rect rect)
	{
		if (!(rect.Height < 42.0))
		{
			double num = Math.Round(rect.Top + rect.Height * 0.61) + 0.5;
			dc.DrawLine(new Pen(WithOpacity(Accent, 0.08), 0.55), new Point(rect.Left + 44.0, num), new Point(rect.Right - 24.0, num));
			dc.DrawLine(new Pen(WithOpacity(Stroke, 0.1), 0.55), new Point(rect.Left + 54.0, num + 3.0), new Point(rect.Right - 32.0, num + 3.0));
		}
	}

	private void DrawCorner(DrawingContext dc, double x, double y, double sx, double sy, double height)
	{
		double num = Math.Clamp(height * 0.18, 5.0, 10.0);
		Pen pen = new Pen(WithOpacity(Stroke, 0.42), 0.55)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		Pen pen2 = new Pen(WithOpacity(Accent, 0.22), 0.42)
		{
			StartLineCap = PenLineCap.Round,
			EndLineCap = PenLineCap.Round
		};
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(x, y + sy * num * 0.7), isFilled: false, isClosed: false);
			streamGeometryContext.BezierTo(new Point(x + sx * num * 0.14, y + sy * num * 0.24), new Point(x + sx * num * 0.52, y + sy * num * 0.12), new Point(x + sx * num, y), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.BeginFigure(new Point(x + sx * num * 0.24, y + sy * num * 0.92), isFilled: false, isClosed: false);
			streamGeometryContext.BezierTo(new Point(x + sx * num * 0.34, y + sy * num * 0.56), new Point(x + sx * num * 0.12, y + sy * num * 0.38), new Point(x + sx * num * 0.12, y + sy * num * 0.66), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		dc.DrawGeometry(null, pen, streamGeometry);
		dc.DrawLine(pen2, new Point(x + sx * num * 0.2, y), new Point(x + sx * num * 0.82, y));
	}

	private static Rect Pixel(Rect rect)
	{
		return new Rect(Math.Round(rect.Left, MidpointRounding.AwayFromZero), Math.Round(rect.Top, MidpointRounding.AwayFromZero), Math.Round(rect.Width, MidpointRounding.AwayFromZero), Math.Round(rect.Height, MidpointRounding.AwayFromZero));
	}

	private static Brush WithOpacity(Brush brush, double opacity)
	{
		Brush brush2 = brush.CloneCurrentValue();
		brush2.Opacity *= opacity;
		brush2.Freeze();
		return brush2;
	}

	private static Brush CreateClassLightMask()
	{
		RadialGradientBrush radialGradientBrush = new RadialGradientBrush();
		radialGradientBrush.Center = new Point(0.06, 0.24);
		radialGradientBrush.GradientOrigin = new Point(0.0, 0.1);
		radialGradientBrush.RadiusX = 0.64;
		radialGradientBrush.RadiusY = 0.92;
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(242, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(122, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.24));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(24, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.52));
		radialGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0));
		radialGradientBrush.Freeze();
		return radialGradientBrush;
	}

	private static Brush CreateClassEdgeLightMask()
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush();
		linearGradientBrush.StartPoint = new Point(0.0, 0.0);
		linearGradientBrush.EndPoint = new Point(1.0, 1.0);
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(210, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(84, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.12));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 0.34));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, byte.MaxValue, byte.MaxValue, byte.MaxValue), 1.0));
		linearGradientBrush.Freeze();
		return linearGradientBrush;
	}
}
