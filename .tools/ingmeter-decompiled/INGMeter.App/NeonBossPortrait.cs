using System;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class NeonBossPortrait : FrameworkElement
{
	public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(ImageSource), typeof(NeonBossPortrait), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(NeonBossPortrait), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(NeonBossPortrait), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Brush), typeof(NeonBossPortrait), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

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
		if (!(base.ActualWidth <= 8.0) && !(base.ActualHeight <= 8.0))
		{
			Rect rect = new Rect(0.0, 0.0, base.ActualWidth, base.ActualHeight);
			StreamGeometry streamGeometry = CreateFrame(rect);
			dc.DrawGeometry(null, new Pen(WithOpacity(Glow, 0.22), Math.Max(1.2, base.ActualHeight * 0.055)), streamGeometry);
			if (Source != null)
			{
				ImageBrush imageBrush = new ImageBrush(Source)
				{
					Stretch = Stretch.Fill,
					ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
					Viewbox = new Rect(0.055, 0.025, 0.89, 0.92)
				};
				imageBrush.Freeze();
				dc.DrawGeometry(imageBrush, null, streamGeometry);
			}
			else
			{
				dc.DrawGeometry(WithOpacity(Glow, 0.12), null, streamGeometry);
			}
			dc.PushClip(streamGeometry);
			DrawVignette(dc, rect);
			dc.Pop();
			DrawCornerLines(dc, rect);
		}
	}

	private static StreamGeometry CreateFrame(Rect rect)
	{
		double num = Math.Clamp(rect.Height * 0.16, 4.0, 7.0);
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(rect.Left + num, rect.Top), isFilled: true, isClosed: true);
			streamGeometryContext.LineTo(new Point(rect.Right - num, rect.Top), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Right, rect.Top + num), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Right, rect.Bottom - num), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Right - num, rect.Bottom), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Left + num, rect.Bottom), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Left, rect.Bottom - num), isStroked: true, isSmoothJoin: false);
			streamGeometryContext.LineTo(new Point(rect.Left, rect.Top + num), isStroked: true, isSmoothJoin: false);
		}
		streamGeometry.Freeze();
		return streamGeometry;
	}

	private static void DrawVignette(DrawingContext dc, Rect rect)
	{
		LinearGradientBrush linearGradientBrush = new LinearGradientBrush
		{
			StartPoint = new Point(0.0, 0.5),
			EndPoint = new Point(1.0, 0.5),
			MappingMode = BrushMappingMode.RelativeToBoundingBox
		};
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(116, 0, 0, 0), 0.0));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.28));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.72));
		linearGradientBrush.GradientStops.Add(new GradientStop(Color.FromArgb(104, 0, 0, 0), 1.0));
		linearGradientBrush.Freeze();
		dc.DrawRectangle(linearGradientBrush, null, rect);
	}

	private void DrawCornerLines(DrawingContext dc, Rect rect)
	{
		double num = Math.Clamp(rect.Height * 0.2, 6.0, 10.0);
		Pen pen = new Pen(WithOpacity(Stroke, 0.5), 0.55)
		{
			StartLineCap = PenLineCap.Square,
			EndLineCap = PenLineCap.Square
		};
		Pen pen2 = new Pen(WithOpacity(Accent, 0.58), 0.55)
		{
			StartLineCap = PenLineCap.Square,
			EndLineCap = PenLineCap.Square
		};
		dc.DrawLine(pen2, new Point(rect.Left + 2.0, rect.Top + num), new Point(rect.Left + 2.0, rect.Top + 3.0));
		dc.DrawLine(pen2, new Point(rect.Left + 3.0, rect.Top + 2.0), new Point(rect.Left + num, rect.Top + 2.0));
		dc.DrawLine(pen, new Point(rect.Right - num, rect.Top + 2.0), new Point(rect.Right - 3.0, rect.Top + 2.0));
		dc.DrawLine(pen, new Point(rect.Right - 2.0, rect.Top + 3.0), new Point(rect.Right - 2.0, rect.Top + num));
	}

	private static Brush WithOpacity(Brush brush, double opacity)
	{
		Brush brush2 = brush.CloneCurrentValue();
		brush2.Opacity *= opacity;
		brush2.Freeze();
		return brush2;
	}
}
