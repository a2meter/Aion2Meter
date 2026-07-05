using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace INGMeter.App;

public sealed class StigmaMarqueeControl : Control
{
	public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register("ItemsSource", typeof(IEnumerable), typeof(StigmaMarqueeControl), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

	public static readonly DependencyProperty PixelsPerSecondProperty = DependencyProperty.Register("PixelsPerSecond", typeof(double), typeof(StigmaMarqueeControl), new FrameworkPropertyMetadata(12.0));

	public static readonly DependencyProperty InitialDelaySecondsProperty = DependencyProperty.Register("InitialDelaySeconds", typeof(double), typeof(StigmaMarqueeControl), new FrameworkPropertyMetadata(1.5));

	public static readonly DependencyProperty EdgePauseSecondsProperty = DependencyProperty.Register("EdgePauseSeconds", typeof(double), typeof(StigmaMarqueeControl), new FrameworkPropertyMetadata(1.2));

	public static readonly DependencyProperty BadgeFontSizeProperty = DependencyProperty.Register("BadgeFontSize", typeof(double), typeof(StigmaMarqueeControl), new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BadgeLineHeightProperty = DependencyProperty.Register("BadgeLineHeight", typeof(double), typeof(StigmaMarqueeControl), new FrameworkPropertyMetadata(11.0, FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BadgePaddingProperty = DependencyProperty.Register("BadgePadding", typeof(Thickness), typeof(StigmaMarqueeControl), new FrameworkPropertyMetadata(new Thickness(4.0, 1.0, 4.0, 1.0), FrameworkPropertyMetadataOptions.AffectsRender));

	public static readonly DependencyProperty BadgeMarginProperty = DependencyProperty.Register("BadgeMargin", typeof(Thickness), typeof(StigmaMarqueeControl), new FrameworkPropertyMetadata(new Thickness(0.0, 0.0, 4.0, 0.0), FrameworkPropertyMetadataOptions.AffectsRender));

	private readonly DispatcherTimer _timer;

	private readonly BrushConverter _brushConverter = new BrushConverter();

	private static int _nextInstanceIndex;

	private readonly double _staggerDelaySeconds;

	private DateTime _lastTickUtc = DateTime.UtcNow;

	private DateTime _pauseUntilUtc = DateTime.MinValue;

	private double _offset;

	private int _direction = 1;

	private INotifyCollectionChanged? _collectionChanged;

	public IEnumerable? ItemsSource
	{
		get
		{
			return (IEnumerable)GetValue(ItemsSourceProperty);
		}
		set
		{
			SetValue(ItemsSourceProperty, value);
		}
	}

	public double PixelsPerSecond
	{
		get
		{
			return (double)GetValue(PixelsPerSecondProperty);
		}
		set
		{
			SetValue(PixelsPerSecondProperty, value);
		}
	}

	public double InitialDelaySeconds
	{
		get
		{
			return (double)GetValue(InitialDelaySecondsProperty);
		}
		set
		{
			SetValue(InitialDelaySecondsProperty, value);
		}
	}

	public double EdgePauseSeconds
	{
		get
		{
			return (double)GetValue(EdgePauseSecondsProperty);
		}
		set
		{
			SetValue(EdgePauseSecondsProperty, value);
		}
	}

	public double BadgeFontSize
	{
		get
		{
			return (double)GetValue(BadgeFontSizeProperty);
		}
		set
		{
			SetValue(BadgeFontSizeProperty, value);
		}
	}

	public double BadgeLineHeight
	{
		get
		{
			return (double)GetValue(BadgeLineHeightProperty);
		}
		set
		{
			SetValue(BadgeLineHeightProperty, value);
		}
	}

	public Thickness BadgePadding
	{
		get
		{
			return (Thickness)GetValue(BadgePaddingProperty);
		}
		set
		{
			SetValue(BadgePaddingProperty, value);
		}
	}

	public Thickness BadgeMargin
	{
		get
		{
			return (Thickness)GetValue(BadgeMarginProperty);
		}
		set
		{
			SetValue(BadgeMarginProperty, value);
		}
	}

	public StigmaMarqueeControl()
	{
		base.ClipToBounds = true;
		base.SnapsToDevicePixels = true;
		int num = Interlocked.Increment(ref _nextInstanceIndex);
		_staggerDelaySeconds = (double)(num % 6) * 0.25;
		_timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(33L)
		};
		_timer.Tick += delegate
		{
			OnAnimationTick();
		};
		base.Loaded += delegate
		{
			_lastTickUtc = DateTime.UtcNow;
			_pauseUntilUtc = _lastTickUtc.AddSeconds(GetInitialDelaySeconds());
			_timer.Start();
		};
		base.Unloaded += delegate
		{
			_timer.Stop();
		};
	}

	protected override void OnRender(DrawingContext drawingContext)
	{
		base.OnRender(drawingContext);
		IReadOnlyList<StigmaBadgeItem> items = GetItems();
		if (items.Count == 0 || base.ActualWidth <= 0.0 || base.ActualHeight <= 0.0)
		{
			return;
		}
		double num = 0.0 - Math.Max(0.0, _offset);
		double y = Math.Max(0.0, (base.ActualHeight - GetBadgeHeight()) / 2.0);
		double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
		foreach (StigmaBadgeItem item in items)
		{
			double num2 = MeasureBadgeWidth(item, pixelsPerDip);
			DrawBadge(drawingContext, item, num, y, num2, pixelsPerDip);
			num += num2 + BadgeMargin.Left + BadgeMargin.Right;
		}
	}

	private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is StigmaMarqueeControl stigmaMarqueeControl)
		{
			if (stigmaMarqueeControl._collectionChanged != null)
			{
				stigmaMarqueeControl._collectionChanged.CollectionChanged -= stigmaMarqueeControl.OnCollectionChanged;
			}
			stigmaMarqueeControl._collectionChanged = e.NewValue as INotifyCollectionChanged;
			if (stigmaMarqueeControl._collectionChanged != null)
			{
				stigmaMarqueeControl._collectionChanged.CollectionChanged += stigmaMarqueeControl.OnCollectionChanged;
			}
			stigmaMarqueeControl.ResetScroll();
		}
	}

	private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		ResetScroll();
	}

	private void ResetScroll()
	{
		_offset = 0.0;
		_direction = 1;
		_lastTickUtc = DateTime.UtcNow;
		_pauseUntilUtc = _lastTickUtc.AddSeconds(GetInitialDelaySeconds());
		InvalidateVisual();
	}

	private void OnAnimationTick()
	{
		DateTime utcNow = DateTime.UtcNow;
		double num = Math.Clamp((utcNow - _lastTickUtc).TotalSeconds, 0.0, 0.1);
		_lastTickUtc = utcNow;
		double num2 = MeasureContentWidth();
		double num3 = Math.Max(0.0, num2 - base.ActualWidth);
		if (num3 <= 1.0)
		{
			if (_offset != 0.0)
			{
				_offset = 0.0;
				InvalidateVisual();
			}
		}
		else if (!(utcNow < _pauseUntilUtc))
		{
			double num4 = Math.Clamp(PixelsPerSecond, 6.0, 80.0);
			_offset += (double)_direction * num4 * num;
			if (_offset >= num3)
			{
				_offset = num3;
				_direction = -1;
				PauseAtEdge(utcNow);
			}
			else if (_offset <= 0.0)
			{
				_offset = 0.0;
				_direction = 1;
				PauseAtEdge(utcNow);
			}
			InvalidateVisual();
		}
	}

	private double GetInitialDelaySeconds()
	{
		return Math.Clamp(InitialDelaySeconds, 0.0, 10.0) + _staggerDelaySeconds;
	}

	private void PauseAtEdge(DateTime now)
	{
		double num = Math.Clamp(EdgePauseSeconds, 0.0, 10.0);
		if (num > 0.0)
		{
			_pauseUntilUtc = now.AddSeconds(num);
		}
	}

	private IReadOnlyList<StigmaBadgeItem> GetItems()
	{
		return ItemsSource?.OfType<StigmaBadgeItem>().ToArray() ?? Array.Empty<StigmaBadgeItem>();
	}

	private double MeasureContentWidth()
	{
		double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
		double num = 0.0;
		foreach (StigmaBadgeItem item in GetItems())
		{
			num += MeasureBadgeWidth(item, pixelsPerDip) + BadgeMargin.Left + BadgeMargin.Right;
		}
		return Math.Max(0.0, num - BadgeMargin.Right);
	}

	private double MeasureBadgeWidth(StigmaBadgeItem item, double pixelsPerDip)
	{
		FormattedText formattedText = CreateText(item.Name, base.FontWeight, GetBrush(item.ForegroundBrush, Brushes.White), pixelsPerDip);
		FormattedText formattedText2 = CreateText(item.LevelText, FontWeights.Bold, GetBrush(item.ForegroundBrush, Brushes.White), pixelsPerDip);
		return Math.Ceiling(BadgePadding.Left + formattedText.WidthIncludingTrailingWhitespace + 4.0 + formattedText2.WidthIncludingTrailingWhitespace + BadgePadding.Right);
	}

	private double GetBadgeHeight()
	{
		return Math.Ceiling(Math.Max(BadgeLineHeight, BadgeFontSize + 2.0) + BadgePadding.Top + BadgePadding.Bottom);
	}

	private void DrawBadge(DrawingContext dc, StigmaBadgeItem item, double x, double y, double width, double pixelsPerDip)
	{
		double badgeHeight = GetBadgeHeight();
		Rect rectangle = new Rect(Math.Round(x + BadgeMargin.Left), Math.Round(y + BadgeMargin.Top), width, badgeHeight);
		Brush brush = GetBrush(item.BackgroundBrush, Brushes.DarkSlateGray);
		Brush brush2 = GetBrush(item.BorderBrush, Brushes.Gray);
		dc.DrawRoundedRectangle(brush, new Pen(brush2, 0.65), rectangle, 4.0, 4.0);
		Brush brush3 = GetBrush(item.ForegroundBrush, Brushes.White);
		FormattedText formattedText = CreateText(item.Name, base.FontWeight, brush3, pixelsPerDip);
		FormattedText formattedText2 = CreateText(item.LevelText, FontWeights.Bold, brush3, pixelsPerDip);
		double y2 = rectangle.Y + Math.Max(0.0, (rectangle.Height - BadgeLineHeight) / 2.0) - 0.5;
		double num = rectangle.X + BadgePadding.Left;
		dc.DrawText(formattedText, new Point(num, y2));
		dc.DrawText(formattedText2, new Point(num + formattedText.WidthIncludingTrailingWhitespace + 4.0, y2));
	}

	private FormattedText CreateText(string text, FontWeight weight, Brush brush, double pixelsPerDip)
	{
		Typeface typeface = new Typeface(base.FontFamily, base.FontStyle, weight, base.FontStretch);
		return new FormattedText(text ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, BadgeFontSize, brush, pixelsPerDip)
		{
			LineHeight = BadgeLineHeight
		};
	}

	private Brush GetBrush(string value, Brush fallback)
	{
		try
		{
			if (_brushConverter.ConvertFromString(value) is Brush result)
			{
				return result;
			}
		}
		catch
		{
		}
		return fallback;
	}
}
