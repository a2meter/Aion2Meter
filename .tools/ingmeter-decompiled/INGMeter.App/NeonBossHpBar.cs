using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class NeonBossHpBar : FrameworkElement
{
	public static readonly DependencyProperty RatioProperty = DependencyProperty.Register("Ratio", typeof(double), typeof(NeonBossHpBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(NeonBossHpBar), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(NeonBossHpBar), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(NeonBossHpBar), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Brush), typeof(NeonBossHpBar), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register("Highlight", typeof(Brush), typeof(NeonBossHpBar), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

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

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 8.0) && !(actualHeight < 2.0))
		{
			double num = Math.Clamp(actualHeight, 3.0, 8.0);
			double num2 = Math.Max(0.0, (actualHeight - num) * 0.5);
			double num3 = Math.Clamp(num * 0.45, 2.0, 5.0);
			double num4 = Math.Max(8.0, actualWidth - num3 * 2.0);
			double num5 = Math.Clamp(num4 * Math.Clamp(Ratio, 0.0, 1.0), 0.0, num4);
			StreamGeometry streamGeometry = CreateRail(num3, num2, num4, num);
			dc.DrawGeometry(null, new Pen(WithOpacity(Glow, 0.58), 4.4), streamGeometry);
			dc.DrawGeometry(Track, new Pen(WithOpacity(Stroke, 0.96), 0.85), streamGeometry);
			dc.PushClip(streamGeometry);
			if (num5 > 0.5)
			{
				dc.DrawRectangle(WithOpacity(Glow, 0.32), null, new Rect(num3, num2 - 1.0, num5, num + 2.0));
				dc.DrawRectangle(Fill, null, new Rect(num3, num2, num5, num));
				dc.DrawRectangle(WithOpacity(Highlight, 0.92), null, new Rect(num3 + 5.0, num2 + Math.Max(0.6, num * 0.18), Math.Max(0.0, num5 - 10.0), Math.Max(0.8, num * 0.18)));
				dc.DrawRectangle(WithOpacity(Glow, 0.4), null, new Rect(num3 + 4.0, num2 + num * 0.68, Math.Max(0.0, num5 - 8.0), Math.Max(1.0, num * 0.16)));
			}
			DrawChevrons(dc, num3, num2, num4, num, num5);
			dc.Pop();
			if (num5 > 4.0 && num5 < num4 - 3.0)
			{
				DrawCursor(dc, num3 + num5, num2, num);
			}
		}
	}

	private static StreamGeometry CreateRail(double left, double top, double width, double height)
	{
		double num = left + width;
		double num2 = top + height;
		double num3 = Math.Clamp(height * 0.34, 2.0, 4.0);
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(left + num3, top), isFilled: true, isClosed: true);
			streamGeometryContext.LineTo(new Point(num - num3, top), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(num, top + num3), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(num, num2 - num3), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(num - num3, num2), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(left + num3, num2), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(left, num2 - num3), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(left, top + num3), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}

	private void DrawChevrons(DrawingContext dc, double left, double top, double width, double height, double fillWidth)
	{
		Pen pen = new Pen(WithOpacity(Highlight, 0.48), 0.7);
		Pen pen2 = new Pen(WithOpacity(Stroke, 0.22), 0.55);
		double num = Math.Clamp(height * 1.05, 7.0, 12.0);
		double num2 = Math.Clamp(height * 0.3, 2.8, 4.6);
		double num3 = top + height * 0.5;
		for (double num4 = left + num * 1.2; num4 < left + width - num; num4 += num)
		{
			Pen pen3 = ((num4 <= left + fillWidth) ? pen : pen2);
			dc.DrawLine(pen3, new Point(num4 - num2, num3 - num2), new Point(num4, num3));
			dc.DrawLine(pen3, new Point(num4, num3), new Point(num4 - num2, num3 + num2));
		}
	}

	private void DrawCursor(DrawingContext dc, double x, double top, double height)
	{
		double num = Math.Clamp(height * 0.24, 3.0, 5.0);
		Rect rectangle = new Rect(x - num * 0.5, top - 2.0, num, height + 4.0);
		dc.DrawRoundedRectangle(WithOpacity(Glow, 0.7), null, new Rect(rectangle.Left - 2.0, rectangle.Top, rectangle.Width + 4.0, rectangle.Height), 1.8, 1.8);
		dc.DrawRoundedRectangle(Fill, new Pen(Highlight, 0.9), rectangle, 1.4, 1.4);
	}

	private static Brush WithOpacity(Brush brush, double opacity)
	{
		Brush brush2 = brush.CloneCurrentValue();
		brush2.Opacity *= opacity;
		brush2.Freeze();
		return brush2;
	}
}
