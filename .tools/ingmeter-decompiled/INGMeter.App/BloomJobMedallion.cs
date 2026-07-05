using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class BloomJobMedallion : FrameworkElement
{
	public static readonly DependencyProperty SourceProperty = DependencyProperty.Register("Source", typeof(ImageSource), typeof(BloomJobMedallion), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FallbackTextProperty = DependencyProperty.Register("FallbackText", typeof(string), typeof(BloomJobMedallion), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(BloomJobMedallion), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty RingProperty = DependencyProperty.Register("Ring", typeof(Brush), typeof(BloomJobMedallion), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register("TextBrush", typeof(Brush), typeof(BloomJobMedallion), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

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

	protected override void OnRender(DrawingContext dc)
	{
		base.OnRender(dc);
		double actualWidth = base.ActualWidth;
		double actualHeight = base.ActualHeight;
		if (!(actualWidth <= 2.0) && !(actualHeight <= 2.0))
		{
			double num = Math.Min(actualWidth, actualHeight);
			Point center = new Point(actualWidth * 0.5, actualHeight * 0.5);
			double num2 = num * 0.49;
			dc.DrawEllipse(Fill, new Pen(Ring, Math.Max(1.1, num * 0.055)), center, num2, num2);
			if (Source != null)
			{
				double num3 = num * 0.93;
				Rect rectangle = PixelAlign(new Rect(center.X - num3 * 0.5, center.Y - num3 * 0.5, num3, num3));
				dc.PushGuidelineSet(new GuidelineSet(new double[2] { rectangle.Left, rectangle.Right }, new double[2] { rectangle.Top, rectangle.Bottom }));
				dc.DrawImage(Source, rectangle);
				dc.Pop();
			}
			else if (!string.IsNullOrWhiteSpace(FallbackText))
			{
				double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
				double emSize = ((FallbackText.Trim().Length <= 1) ? Math.Max(11.0, num * 0.46) : Math.Max(8.0, num * 0.3));
				FormattedText formattedText = new FormattedText(FallbackText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), emSize, TextBrush, pixelsPerDip);
				dc.DrawText(formattedText, new Point((actualWidth - formattedText.Width) * 0.5, (actualHeight - formattedText.Height) * 0.5));
			}
		}
	}

	private static Rect PixelAlign(Rect rect)
	{
		return new Rect(Math.Round(rect.Left, MidpointRounding.AwayFromZero), Math.Round(rect.Top, MidpointRounding.AwayFromZero), Math.Round(rect.Width, MidpointRounding.AwayFromZero), Math.Round(rect.Height, MidpointRounding.AwayFromZero));
	}
}
