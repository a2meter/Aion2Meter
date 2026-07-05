using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class NeonHexJobIcon : FrameworkElement
{
	public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(ImageSource), typeof(NeonHexJobIcon), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FallbackTextProperty = DependencyProperty.Register("FallbackText", typeof(string), typeof(NeonHexJobIcon), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(NeonHexJobIcon), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(NeonHexJobIcon), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowProperty = DependencyProperty.Register("Glow", typeof(Brush), typeof(NeonHexJobIcon), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(NeonHexJobIcon), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register("TextBrush", typeof(Brush), typeof(NeonHexJobIcon), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register("StrokeThickness", typeof(double), typeof(NeonHexJobIcon), new FrameworkPropertyMetadata(1.35, FrameworkPropertyMetadataOptions.AffectsRender));

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

	public string FallbackText
	{
		get
		{
			return (string)GetValue(FallbackTextProperty);
		}
		set
		{
			SetValue(FallbackTextProperty, value);
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

	public Brush TextBrush
	{
		get
		{
			return (Brush)GetValue(TextBrushProperty);
		}
		set
		{
			SetValue(TextBrushProperty, value);
		}
	}

	public double StrokeThickness
	{
		get
		{
			return (double)GetValue(StrokeThicknessProperty);
		}
		set
		{
			SetValue(StrokeThicknessProperty, value);
		}
	}

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 4.0) && !(actualHeight <= 4.0))
		{
			Rect rect = new Rect(1.6, 1.6, actualWidth - 3.2, actualHeight - 3.2);
			Rect rect2 = new Rect(5.0, 5.0, Math.Max(1.0, actualWidth - 10.0), Math.Max(1.0, actualHeight - 10.0));
			StreamGeometry geometry = CreateHex(rect);
			StreamGeometry geometry2 = CreateHex(rect2);
			dc.DrawGeometry(null, new Pen(WithOpacity(Glow, 0.36), 3.0), geometry);
			dc.DrawGeometry(Fill, new Pen(Stroke, Math.Min(StrokeThickness, 1.05)), geometry);
			double num = Math.Max(4.0, Math.Min(actualWidth, actualHeight) * 0.18);
			Rect rectangle = PixelAlign(new Rect(num, num, Math.Max(1.0, actualWidth - num * 2.0), Math.Max(1.0, actualHeight - num * 2.0)));
			if (Source != null)
			{
				dc.DrawImage(Source, rectangle);
			}
			else if (!string.IsNullOrWhiteSpace(FallbackText))
			{
				double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
				FormattedText formattedText = new FormattedText(FallbackText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(8.0, Math.Min(actualWidth, actualHeight) * 0.3), TextBrush, pixelsPerDip);
				dc.DrawText(formattedText, new Point((actualWidth - formattedText.Width) * 0.5, (actualHeight - formattedText.Height) * 0.5));
			}
			dc.DrawGeometry(null, new Pen(WithOpacity(Accent, 0.82), 0.7), geometry2);
			dc.DrawGeometry(null, new Pen(WithOpacity(Stroke, 0.48), 0.45), CreateHex(new Rect(rect.Left + 2.8, rect.Top + 2.8, Math.Max(1.0, rect.Width - 5.6), Math.Max(1.0, rect.Height - 5.6))));
			DrawTicks(dc, rect);
		}
	}

	private void DrawTicks(DrawingContext dc, Rect rect)
	{
		double num = Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.11, 3.0, 5.5);
		Pen pen = new Pen(WithOpacity(Accent, 0.56), 0.65)
		{
			StartLineCap = PenLineCap.Square,
			EndLineCap = PenLineCap.Square
		};
		dc.DrawLine(pen, new Point(rect.Left + num, rect.Top + 2.0), new Point(rect.Left + num * 1.8, rect.Top + 2.0));
		dc.DrawLine(pen, new Point(rect.Right - num, rect.Bottom - 2.0), new Point(rect.Right - num * 1.8, rect.Bottom - 2.0));
		dc.DrawLine(pen, new Point(rect.Left + 2.0, rect.Bottom - num), new Point(rect.Left + 2.0, rect.Bottom - num * 1.8));
		dc.DrawLine(pen, new Point(rect.Right - 2.0, rect.Top + num), new Point(rect.Right - 2.0, rect.Top + num * 1.8));
	}

	private static Rect PixelAlign(Rect rect)
	{
		return new Rect(Math.Round(rect.Left, MidpointRounding.AwayFromZero), Math.Round(rect.Top, MidpointRounding.AwayFromZero), Math.Round(rect.Width, MidpointRounding.AwayFromZero), Math.Round(rect.Height, MidpointRounding.AwayFromZero));
	}

	private static StreamGeometry CreateHex(Rect rect)
	{
		double num = rect.Left + rect.Width * 0.5;
		double num2 = rect.Top + rect.Height * 0.5;
		double num3 = Math.Min(rect.Width * 0.5, rect.Height / Math.Sqrt(3.0));
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(new Point(num + num3, num2), isFilled: true, isClosed: true);
			for (int i = 1; i < 6; i++)
			{
				double num4 = (double)i * Math.PI / 3.0;
				streamGeometryContext.LineTo(new Point(num + Math.Cos(num4) * num3, num2 + Math.Sin(num4) * num3), isStroked: true, isSmoothJoin: false);
			}
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
}
