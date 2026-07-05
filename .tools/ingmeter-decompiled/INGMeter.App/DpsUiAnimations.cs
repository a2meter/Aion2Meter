using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace INGMeter.App;

internal static class DpsUiAnimations
{
	private sealed class ListState
	{
		public Dictionary<object, double> LastLayoutTops { get; } = new Dictionary<object, double>();

		public HashSet<object> SeenItems { get; } = new HashSet<object>();

		public HashSet<object> PendingEnterItems { get; } = new HashSet<object>();
	}

	private static readonly Duration RowMoveDuration = TimeSpan.FromMilliseconds(500L);

	private static readonly Duration RowEnterDuration = TimeSpan.FromMilliseconds(420L);

	private static readonly Duration SurfaceEnterDuration = TimeSpan.FromMilliseconds(260L);

	private static readonly Duration ViewSwitchDuration = TimeSpan.FromMilliseconds(220L);

	private static readonly Duration WidthDuration = TimeSpan.FromMilliseconds(260L);

	private static readonly Duration RatioDuration = TimeSpan.FromMilliseconds(260L);

	private static readonly IEasingFunction RowMoveEase = new CubicEase
	{
		EasingMode = EasingMode.EaseInOut
	};

	private static readonly IEasingFunction RowEnterEase = new CubicEase
	{
		EasingMode = EasingMode.EaseOut
	};

	private static readonly IEasingFunction SurfaceEnterEase = new CubicEase
	{
		EasingMode = EasingMode.EaseOut
	};

	private static readonly IEasingFunction WidthEase = new QuadraticEase
	{
		EasingMode = EasingMode.EaseOut
	};

	private static readonly DependencyProperty ListStateProperty = DependencyProperty.RegisterAttached("ListState", typeof(ListState), typeof(DpsUiAnimations));

	public static readonly DependencyProperty AnimateItemsProperty = DependencyProperty.RegisterAttached("AnimateItems", typeof(bool), typeof(DpsUiAnimations), new PropertyMetadata(false, OnAnimateItemsChanged));

	public static readonly DependencyProperty AnimatedWidthProperty = DependencyProperty.RegisterAttached("AnimatedWidth", typeof(double), typeof(DpsUiAnimations), new PropertyMetadata(0.0, OnAnimatedWidthChanged));

	private static readonly DependencyProperty WidthInitializedProperty = DependencyProperty.RegisterAttached("WidthInitialized", typeof(bool), typeof(DpsUiAnimations), new PropertyMetadata(false));

	public static readonly DependencyProperty AnimatedRatioProperty = DependencyProperty.RegisterAttached("AnimatedRatio", typeof(double), typeof(DpsUiAnimations), new PropertyMetadata(0.0, OnAnimatedRatioChanged));

	private static readonly DependencyProperty RatioInitializedProperty = DependencyProperty.RegisterAttached("RatioInitialized", typeof(bool), typeof(DpsUiAnimations), new PropertyMetadata(false));

	public static void SetAnimateItems(DependencyObject element, bool value)
	{
		element.SetValue(AnimateItemsProperty, value);
	}

	public static bool GetAnimateItems(DependencyObject element)
	{
		return (bool)element.GetValue(AnimateItemsProperty);
	}

	public static void ResetItems(ListBox? listBox)
	{
		if (listBox?.GetValue(ListStateProperty) is ListState listState)
		{
			listState.LastLayoutTops.Clear();
			listState.SeenItems.Clear();
		}
	}

	public static IReadOnlyDictionary<object, double> CaptureItemTops(ListBox? listBox)
	{
		Dictionary<object, double> dictionary = new Dictionary<object, double>();
		if (listBox == null)
		{
			return dictionary;
		}
		listBox.UpdateLayout();
		for (int i = 0; i < listBox.Items.Count; i++)
		{
			object obj = listBox.Items[i];
			if (listBox.ItemContainerGenerator.ContainerFromItem(obj) is ListBoxItem { IsVisible: not false } listBoxItem)
			{
				EnsureRowTransform(listBoxItem);
				TranslateTransform rowTranslate = GetRowTranslate(listBoxItem);
				if (rowTranslate != null)
				{
					dictionary[obj] = VisualTreeHelper.GetOffset(listBoxItem).Y + rowTranslate.Y;
				}
			}
		}
		return dictionary;
	}

	public static void AnimateItemsFrom(ListBox? listBox, IReadOnlyDictionary<object, double> previousTops)
	{
		if (listBox == null || previousTops.Count == 0)
		{
			return;
		}
		listBox.UpdateLayout();
		Dictionary<object, double> dictionary = new Dictionary<object, double>();
		for (int i = 0; i < listBox.Items.Count; i++)
		{
			object obj = listBox.Items[i];
			if (!(listBox.ItemContainerGenerator.ContainerFromItem(obj) is ListBoxItem { IsVisible: not false } listBoxItem))
			{
				continue;
			}
			EnsureRowTransform(listBoxItem);
			double num = (dictionary[obj] = VisualTreeHelper.GetOffset(listBoxItem).Y);
			if (previousTops.TryGetValue(obj, out var value))
			{
				double num2 = value - num;
				if (Math.Abs(num2) > 0.5)
				{
					AnimateRowMove(listBoxItem, num2);
				}
			}
		}
		if (!(listBox.GetValue(ListStateProperty) is ListState listState))
		{
			return;
		}
		listState.LastLayoutTops.Clear();
		foreach (KeyValuePair<object, double> item in dictionary)
		{
			listState.LastLayoutTops[item.Key] = item.Value;
			listState.SeenItems.Add(item.Key);
		}
	}

	public static void PlayBossCardEnter(FrameworkElement? element)
	{
		PlaySurfaceEnter(element, SurfaceEnterDuration, 0.0, -10.0, 0.985);
	}

	public static void PlayViewSwitch(FrameworkElement? element, bool fromRight)
	{
		PlaySurfaceEnter(element, ViewSwitchDuration, fromRight ? 18 : (-18), 0.0, 0.995);
	}

	public static void PlayItemEnter(ListBox? listBox, object? item)
	{
		if (listBox != null && item != null)
		{
			if (listBox.GetValue(ListStateProperty) is ListState listState)
			{
				listState.SeenItems.Remove(item);
				listState.PendingEnterItems.Add(item);
			}
			TryPlayItemEnter(listBox, item);
		}
	}

	private static bool TryPlayItemEnter(ListBox listBox, object item)
	{
		if (!listBox.IsLoaded || listBox.Visibility != Visibility.Visible)
		{
			return false;
		}
		listBox.UpdateLayout();
		if (!(listBox.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem { IsVisible: not false } listBoxItem))
		{
			return false;
		}
		if (listBox.GetValue(ListStateProperty) is ListState listState)
		{
			if (!listState.PendingEnterItems.Remove(item) && listState.SeenItems.Contains(item))
			{
				return true;
			}
			listState.SeenItems.Add(item);
		}
		EnsureRowTransform(listBoxItem);
		AnimateRowEnter(listBoxItem);
		return true;
	}

	private static void OnAnimateItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is ListBox listBox)
		{
			if ((bool)e.NewValue)
			{
				listBox.SetValue(ListStateProperty, new ListState());
				listBox.LayoutUpdated += ListBox_LayoutUpdated;
				listBox.Unloaded += ListBox_Unloaded;
			}
			else
			{
				listBox.LayoutUpdated -= ListBox_LayoutUpdated;
				listBox.Unloaded -= ListBox_Unloaded;
				listBox.ClearValue(ListStateProperty);
			}
		}
	}

	private static void ListBox_Unloaded(object sender, RoutedEventArgs e)
	{
		if (sender is ListBox element)
		{
			SetAnimateItems(element, value: false);
		}
	}

	private static void ListBox_LayoutUpdated(object? sender, EventArgs e)
	{
		if (!(sender is ListBox listBox) || !(listBox.GetValue(ListStateProperty) is ListState listState))
		{
			return;
		}
		Dictionary<object, double> dictionary = new Dictionary<object, double>();
		for (int i = 0; i < listBox.Items.Count; i++)
		{
			object obj = listBox.Items[i];
			if (!(listBox.ItemContainerGenerator.ContainerFromItem(obj) is ListBoxItem { IsVisible: not false } listBoxItem))
			{
				continue;
			}
			EnsureRowTransform(listBoxItem);
			if (GetRowTranslate(listBoxItem) != null)
			{
				double y = VisualTreeHelper.GetOffset(listBoxItem).Y;
				dictionary[obj] = y;
				if (listState.PendingEnterItems.Remove(obj) || !listState.SeenItems.Contains(obj))
				{
					AnimateRowEnter(listBoxItem);
				}
				listState.SeenItems.Add(obj);
			}
		}
		listState.LastLayoutTops.Clear();
		foreach (KeyValuePair<object, double> item in dictionary)
		{
			listState.LastLayoutTops[item.Key] = item.Value;
		}
	}

	private static void EnsureRowTransform(ListBoxItem item)
	{
		EnsureScaleTranslateTransform(item, out ScaleTransform _, out TranslateTransform _);
	}

	private static void EnsureScaleTranslateTransform(FrameworkElement element, out ScaleTransform scale, out TranslateTransform translate)
	{
		if (element.RenderTransform is TransformGroup transformGroup && transformGroup.Children.Count == 2 && transformGroup.Children[0] is ScaleTransform scaleTransform && transformGroup.Children[1] is TranslateTransform translateTransform)
		{
			scale = scaleTransform;
			translate = translateTransform;
			return;
		}
		scale = new ScaleTransform(1.0, 1.0);
		translate = new TranslateTransform();
		element.RenderTransformOrigin = new Point(0.5, 0.5);
		element.RenderTransform = new TransformGroup
		{
			Children = 
			{
				(Transform)scale,
				(Transform)translate
			}
		};
	}

	private static TranslateTransform? GetRowTranslate(ListBoxItem item)
	{
		if (!(item.RenderTransform is TransformGroup transformGroup) || transformGroup.Children.Count <= 1)
		{
			return null;
		}
		return transformGroup.Children[1] as TranslateTransform;
	}

	private static void AnimateRowMove(ListBoxItem item, double fromY)
	{
		if (!(item.RenderTransform is TransformGroup transformGroup))
		{
			return;
		}
		Transform transform = transformGroup.Children[1];
		TranslateTransform translate = transform as TranslateTransform;
		if (translate != null)
		{
			Panel.SetZIndex(item, (fromY > 0.0) ? 21 : 20);
			DoubleAnimation doubleAnimation = new DoubleAnimation
			{
				From = fromY,
				To = 0.0,
				Duration = RowMoveDuration,
				EasingFunction = RowMoveEase
			};
			doubleAnimation.Completed += delegate
			{
				translate.BeginAnimation(TranslateTransform.YProperty, null);
				translate.Y = 0.0;
				Panel.SetZIndex(item, 0);
			};
			translate.BeginAnimation(TranslateTransform.YProperty, doubleAnimation, HandoffBehavior.SnapshotAndReplace);
		}
	}

	private static void AnimateRowEnter(ListBoxItem item)
	{
		if (!(item.RenderTransform is TransformGroup transformGroup))
		{
			return;
		}
		Transform transform = transformGroup.Children[0];
		ScaleTransform scale = transform as ScaleTransform;
		if (scale == null)
		{
			return;
		}
		transform = transformGroup.Children[1];
		TranslateTransform translate = transform as TranslateTransform;
		if (translate != null)
		{
			item.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
			{
				From = 0.0,
				To = 1.0,
				Duration = RowEnterDuration,
				EasingFunction = RowEnterEase
			}, HandoffBehavior.SnapshotAndReplace);
			scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
			{
				From = 0.96,
				To = 1.0,
				Duration = RowEnterDuration,
				EasingFunction = RowEnterEase
			}, HandoffBehavior.SnapshotAndReplace);
			scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
			{
				From = 0.78,
				To = 1.0,
				Duration = RowEnterDuration,
				EasingFunction = RowEnterEase
			}, HandoffBehavior.SnapshotAndReplace);
			DoubleAnimation doubleAnimation = new DoubleAnimation
			{
				From = 24.0,
				To = 0.0,
				Duration = RowEnterDuration,
				EasingFunction = RowEnterEase
			};
			doubleAnimation.Completed += delegate
			{
				item.BeginAnimation(UIElement.OpacityProperty, null);
				item.Opacity = 1.0;
				scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
				scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
				scale.ScaleX = 1.0;
				scale.ScaleY = 1.0;
				translate.BeginAnimation(TranslateTransform.YProperty, null);
				translate.Y = 0.0;
			};
			translate.BeginAnimation(TranslateTransform.YProperty, doubleAnimation, HandoffBehavior.SnapshotAndReplace);
		}
	}

	private static void PlaySurfaceEnter(FrameworkElement? element, Duration duration, double fromX, double fromY, double fromScale)
	{
		if (element == null || !element.IsLoaded || !SystemParameters.ClientAreaAnimation)
		{
			return;
		}
		EnsureScaleTranslateTransform(element, out ScaleTransform scale, out TranslateTransform translate);
		double targetOpacity = element.Opacity;
		scale.ScaleX = 1.0;
		scale.ScaleY = 1.0;
		translate.X = 0.0;
		translate.Y = 0.0;
		element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
		{
			From = 0.0,
			To = targetOpacity,
			Duration = duration,
			EasingFunction = SurfaceEnterEase
		}, HandoffBehavior.SnapshotAndReplace);
		scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
		{
			From = fromScale,
			To = 1.0,
			Duration = duration,
			EasingFunction = SurfaceEnterEase
		}, HandoffBehavior.SnapshotAndReplace);
		scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
		{
			From = fromScale,
			To = 1.0,
			Duration = duration,
			EasingFunction = SurfaceEnterEase
		}, HandoffBehavior.SnapshotAndReplace);
		translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
		{
			From = fromX,
			To = 0.0,
			Duration = duration,
			EasingFunction = SurfaceEnterEase
		}, HandoffBehavior.SnapshotAndReplace);
		DoubleAnimation doubleAnimation = new DoubleAnimation
		{
			From = fromY,
			To = 0.0,
			Duration = duration,
			EasingFunction = SurfaceEnterEase
		};
		doubleAnimation.Completed += delegate
		{
			element.BeginAnimation(UIElement.OpacityProperty, null);
			element.Opacity = targetOpacity;
			scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
			scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
			translate.BeginAnimation(TranslateTransform.XProperty, null);
			translate.BeginAnimation(TranslateTransform.YProperty, null);
			scale.ScaleX = 1.0;
			scale.ScaleY = 1.0;
			translate.X = 0.0;
			translate.Y = 0.0;
			if (element.RenderTransform is TransformGroup transformGroup && transformGroup.Children.Count == 2 && transformGroup.Children[0] == scale && transformGroup.Children[1] == translate)
			{
				element.ClearValue(UIElement.RenderTransformProperty);
			}
		};
		translate.BeginAnimation(TranslateTransform.YProperty, doubleAnimation, HandoffBehavior.SnapshotAndReplace);
	}

	public static void SetAnimatedWidth(DependencyObject element, double value)
	{
		element.SetValue(AnimatedWidthProperty, value);
	}

	public static double GetAnimatedWidth(DependencyObject element)
	{
		return (double)element.GetValue(AnimatedWidthProperty);
	}

	private static void OnAnimatedWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (d is FrameworkElement frameworkElement)
		{
			double num = SanitizeWidth((double)e.NewValue);
			double num2 = SanitizeWidth((frameworkElement.ActualWidth > 0.0) ? frameworkElement.ActualWidth : frameworkElement.Width);
			double num3 = SanitizeWidth((double)e.OldValue);
			if (!(bool)frameworkElement.GetValue(WidthInitializedProperty) || !frameworkElement.IsLoaded || (num3 <= 0.5 && num2 <= 0.5 && num > 0.5))
			{
				frameworkElement.SetValue(WidthInitializedProperty, true);
				frameworkElement.BeginAnimation(FrameworkElement.WidthProperty, null);
				frameworkElement.Width = num;
			}
			else if (Math.Abs(num2 - num) < 0.5)
			{
				frameworkElement.Width = num;
			}
			else
			{
				frameworkElement.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation
				{
					From = num2,
					To = num,
					Duration = WidthDuration,
					EasingFunction = WidthEase
				}, HandoffBehavior.SnapshotAndReplace);
			}
		}
	}

	private static double SanitizeWidth(double value)
	{
		if (!double.IsNaN(value) && !double.IsInfinity(value))
		{
			return Math.Max(0.0, value);
		}
		return 0.0;
	}

	public static void SetAnimatedRatio(DependencyObject element, double value)
	{
		element.SetValue(AnimatedRatioProperty, value);
	}

	public static double GetAnimatedRatio(DependencyObject element)
	{
		return (double)element.GetValue(AnimatedRatioProperty);
	}

	private static void OnAnimatedRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
	{
		if (!(d is FrameworkElement frameworkElement))
		{
			return;
		}
		DependencyProperty ratioProperty = GetRatioProperty(frameworkElement);
		if (ratioProperty != null)
		{
			double num = SanitizeRatio((double)e.NewValue);
			double num2 = SanitizeRatio((frameworkElement.GetValue(ratioProperty) as double?).GetValueOrDefault());
			double num3 = SanitizeRatio((double)e.OldValue);
			if (!(bool)frameworkElement.GetValue(RatioInitializedProperty) || !frameworkElement.IsLoaded || (num3 <= 0.5 && num2 <= 0.5 && num > 0.5))
			{
				frameworkElement.SetValue(RatioInitializedProperty, true);
				frameworkElement.BeginAnimation(ratioProperty, null);
				frameworkElement.SetValue(ratioProperty, num);
			}
			else
			{
				frameworkElement.BeginAnimation(ratioProperty, new DoubleAnimation
				{
					From = num2,
					To = num,
					Duration = RatioDuration,
					EasingFunction = WidthEase
				}, HandoffBehavior.SnapshotAndReplace);
			}
		}
	}

	private static DependencyProperty? GetRatioProperty(FrameworkElement element)
	{
		return element.GetType().GetField("RatioProperty", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null) as DependencyProperty;
	}

	private static double SanitizeRatio(double value)
	{
		if (!double.IsNaN(value) && !double.IsInfinity(value))
		{
			return Math.Clamp(value, 0.0, 100.0);
		}
		return 0.0;
	}
}
