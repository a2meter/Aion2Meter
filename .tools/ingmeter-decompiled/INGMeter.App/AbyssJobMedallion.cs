using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class AbyssJobMedallion : FrameworkElement
{
	public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(ImageSource), typeof(AbyssJobMedallion), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FallbackTextProperty = DependencyProperty.Register("FallbackText", typeof(string), typeof(AbyssJobMedallion), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(AbyssJobMedallion), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RingProperty = DependencyProperty.Register("Ring", typeof(Brush), typeof(AbyssJobMedallion), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty AccentProperty = DependencyProperty.Register("Accent", typeof(Brush), typeof(AbyssJobMedallion), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register("TextBrush", typeof(Brush), typeof(AbyssJobMedallion), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty GlowIntensityProperty = DependencyProperty.Register("GlowIntensity", typeof(double), typeof(AbyssJobMedallion), new FrameworkPropertyMetadata(0.32, FrameworkPropertyMetadataOptions.AffectsRender));

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

	public double GlowIntensity
	{
		get
		{
			return (double)GetValue(GlowIntensityProperty);
		}
		set
		{
			SetValue(GlowIntensityProperty, value);
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
			double num2 = num * 0.47;
			double num3 = num * 0.36;
			double num4 = Math.Clamp(GlowIntensity, 0.0, 1.0);
			dc.DrawEllipse(WithOpacity(Ring, 0.08 + num4 * 0.3), null, center, num2 + num * (0.04 + num4 * 0.05), num2 + num * (0.04 + num4 * 0.05));
			dc.DrawEllipse(WithOpacity(Ring, 0.04 + num4 * 0.2), null, center, num2 + num * (0.11 + num4 * 0.06), num2 + num * (0.11 + num4 * 0.06));
			dc.DrawEllipse(Fill, new Pen(WithOpacity(Ring, 0.62 + num4 * 0.38), Math.Max(1.2, num * 0.045)), center, num2, num2);
			dc.DrawEllipse(null, new Pen(WithOpacity(Ring, 0.14 + num4 * 0.46), Math.Max(0.8, num * 0.018)), center, num2 - num * 0.055, num2 - num * 0.055);
			dc.DrawEllipse(null, new Pen(WithOpacity(Accent, 0.82), Math.Max(0.8, num * 0.018)), center, num3, num3);
			EllipseGeometry clipGeometry = new EllipseGeometry(center, num * 0.31, num * 0.31);
			dc.PushClip(clipGeometry);
			if (Source != null)
			{
				double num5 = num * 0.68;
				dc.DrawImage(Source, new Rect(center.X - num5 * 0.5, center.Y - num5 * 0.5, num5, num5));
			}
			else if (!string.IsNullOrWhiteSpace(FallbackText))
			{
				double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
				FormattedText formattedText = new FormattedText(FallbackText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(8.0, num * 0.28), TextBrush, pixelsPerDip);
				dc.DrawText(formattedText, new Point((actualWidth - formattedText.Width) * 0.5, (actualHeight - formattedText.Height) * 0.5));
			}
			dc.Pop();
			DrawProng(dc, center.X, center.Y - num2, 0.0, -1.0, num);
			DrawProng(dc, center.X, center.Y + num2, 0.0, 1.0, num);
			DrawProng(dc, center.X - num2, center.Y, -1.0, 0.0, num);
			DrawProng(dc, center.X + num2, center.Y, 1.0, 0.0, num);
		}
	}

	private void DrawProng(DrawingContext dc, double x, double y, double sx, double sy, double size)
	{
		double num = Math.Max(2.0, size * 0.045);
		Pen pen = new Pen(WithOpacity(Accent, 0.78), Math.Max(0.7, size * 0.014));
		if (Math.Abs(sx) > 0.1)
		{
			dc.DrawLine(pen, new Point(x, y - num), new Point(x + sx * num * 1.7, y));
			dc.DrawLine(pen, new Point(x + sx * num * 1.7, y), new Point(x, y + num));
		}
		else
		{
			dc.DrawLine(pen, new Point(x - num, y), new Point(x, y + sy * num * 1.7));
			dc.DrawLine(pen, new Point(x, y + sy * num * 1.7), new Point(x + num, y));
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
