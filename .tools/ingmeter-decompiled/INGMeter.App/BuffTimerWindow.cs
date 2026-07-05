using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace INGMeter.App;

public class BuffTimerWindow : Window, IComponentConnector, IStyleConnector
{
	private const double HiddenChromeOpacity = 0.08;

	private const double HiddenPresenceOpacity = 0.38;

	private int _rowCount;

	private bool _chromeVisible;

	private bool _isMouseInside;

	private bool _isSizingOrMoving;

	private bool _lockedBackgroundHidden;

	private readonly DispatcherTimer _hideChromeTimer;

	private readonly ObservableCollection<BuffTimerRow> _rows = new ObservableCollection<BuffTimerRow>();

	private readonly Dictionary<int, BuffTimerRow> _rowsByKey = new Dictionary<int, BuffTimerRow>();

	private int _hiddenRowCount;

	private readonly ObservableCollection<BuffTimerRow> _hiddenRows = new ObservableCollection<BuffTimerRow>();

	private readonly Dictionary<int, BuffTimerRow> _hiddenRowsByKey = new Dictionary<int, BuffTimerRow>();

	internal Border root;

	internal Border chromeSurface;

	internal Border presenceHint;

	internal ItemsControl itemsBuffTimers;

	internal Border hiddenBuffTray;

	internal ItemsControl itemsHiddenBuffTimers;

	internal TextBlock txtEmpty;

	internal Button btnClose;

	private bool _contentLoaded;

	public event EventHandler<int>? HideBuffRequested;

	public event EventHandler<int>? RestoreBuffRequested;

	public BuffTimerWindow()
	{
		InitializeComponent();
		itemsBuffTimers.ItemsSource = _rows;
		itemsHiddenBuffTimers.ItemsSource = _hiddenRows;
		_hideChromeTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(450L)
		};
		_hideChromeTimer.Tick += delegate
		{
			_hideChromeTimer.Stop();
			if (!_isMouseInside && !_isSizingOrMoving)
			{
				SetChromeVisible(visible: false);
			}
		};
		base.SourceInitialized += delegate
		{
			HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
		};
		_chromeVisible = true;
		SetChromeVisible(visible: false);
	}

	public void SetRows(IReadOnlyList<BuffTimerRow> rows, IReadOnlyList<BuffTimerRow> hiddenRows)
	{
		_rowCount = rows.Count;
		_hiddenRowCount = hiddenRows.Count;
		SyncRows(rows, _rows, _rowsByKey);
		SyncRows(hiddenRows, _hiddenRows, _hiddenRowsByKey);
		UpdateEmptyVisibility();
	}

	public void SetLockedBackgroundHidden(bool hidden)
	{
		if (_lockedBackgroundHidden != hidden)
		{
			_lockedBackgroundHidden = hidden;
			_hideChromeTimer.Stop();
			_chromeVisible = !hidden && (_isMouseInside || _isSizingOrMoving);
			ApplyChromeOpacity(_chromeVisible, animate: false);
			UpdateEmptyVisibility();
		}
	}

	private static void SyncRows(IReadOnlyList<BuffTimerRow> sourceRows, ObservableCollection<BuffTimerRow> targetRows, Dictionary<int, BuffTimerRow> rowsByKey)
	{
		HashSet<int> hashSet = new HashSet<int>();
		for (int i = 0; i < sourceRows.Count; i++)
		{
			BuffTimerRow buffTimerRow = sourceRows[i];
			hashSet.Add(buffTimerRow.Key);
			if (!rowsByKey.TryGetValue(buffTimerRow.Key, out BuffTimerRow value))
			{
				value = buffTimerRow;
				rowsByKey[buffTimerRow.Key] = value;
				targetRows.Insert(i, value);
				continue;
			}
			value.CopyFrom(buffTimerRow);
			int num = targetRows.IndexOf(value);
			if (num >= 0 && num != i)
			{
				targetRows.Move(num, i);
			}
		}
		for (int num2 = targetRows.Count - 1; num2 >= 0; num2--)
		{
			BuffTimerRow buffTimerRow2 = targetRows[num2];
			if (!hashSet.Contains(buffTimerRow2.Key))
			{
				rowsByKey.Remove(buffTimerRow2.Key);
				targetRows.RemoveAt(num2);
			}
		}
	}

	private void SetChromeVisible(bool visible)
	{
		if (_lockedBackgroundHidden)
		{
			_chromeVisible = false;
			ApplyChromeOpacity(visible: false, animate: false);
			UpdateEmptyVisibility();
		}
		else if ((visible || !_isSizingOrMoving) && _chromeVisible != visible)
		{
			_chromeVisible = visible;
			ApplyChromeOpacity(visible, animate: true);
			UpdateEmptyVisibility();
		}
	}

	private void ApplyChromeOpacity(bool visible, bool animate)
	{
		double num = (_lockedBackgroundHidden ? 0.0 : (visible ? 1.0 : 0.08));
		double presenceHintOpacity = GetPresenceHintOpacity(visible);
		double num2 = (_lockedBackgroundHidden ? 0.0 : (visible ? 1.0 : 0.0));
		if (visible && !_lockedBackgroundHidden)
		{
			btnClose.Visibility = Visibility.Visible;
		}
		if (animate)
		{
			AnimateChrome(chromeSurface, UIElement.OpacityProperty, num);
			AnimateChrome(presenceHint, UIElement.OpacityProperty, presenceHintOpacity);
			AnimateChrome(btnClose, UIElement.OpacityProperty, num2, delegate
			{
				if (!_chromeVisible || _lockedBackgroundHidden)
				{
					btnClose.Visibility = Visibility.Collapsed;
				}
			});
		}
		else
		{
			SetOpacityImmediate(chromeSurface, num);
			SetOpacityImmediate(presenceHint, presenceHintOpacity);
			SetOpacityImmediate(btnClose, num2);
			if (num2 <= 0.0)
			{
				btnClose.Visibility = Visibility.Collapsed;
			}
		}
	}

	private static void AnimateChrome(DependencyObject target, DependencyProperty property, double to, Action? completed = null)
	{
		DoubleAnimation doubleAnimation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(160L))
		{
			EasingFunction = new QuadraticEase
			{
				EasingMode = EasingMode.EaseOut
			}
		};
		Storyboard.SetTarget(doubleAnimation, target);
		Storyboard.SetTargetProperty(doubleAnimation, new PropertyPath(property));
		if (completed != null)
		{
			doubleAnimation.Completed += delegate
			{
				completed();
			};
		}
		Storyboard storyboard = new Storyboard();
		storyboard.Children.Add(doubleAnimation);
		storyboard.Begin();
	}

	private static void SetOpacityImmediate(UIElement target, double opacity)
	{
		target.BeginAnimation(UIElement.OpacityProperty, null);
		target.Opacity = opacity;
	}

	private void UpdateEmptyVisibility()
	{
		bool flag = _rowCount > 0 || _hiddenRowCount > 0;
		txtEmpty.Visibility = ((_lockedBackgroundHidden || !_chromeVisible || flag) ? Visibility.Collapsed : Visibility.Visible);
		hiddenBuffTray.Visibility = ((_hiddenRowCount <= 0) ? Visibility.Collapsed : Visibility.Visible);
		presenceHint.Opacity = GetPresenceHintOpacity(_chromeVisible);
	}

	private double GetPresenceHintOpacity(bool chromeVisible)
	{
		if (_lockedBackgroundHidden || chromeVisible || _rowCount != 0 || _hiddenRowCount != 0)
		{
			return 0.0;
		}
		return 0.38;
	}

	private void Root_MouseEnter(object sender, MouseEventArgs e)
	{
		_isMouseInside = true;
		_hideChromeTimer.Stop();
		SetChromeVisible(visible: true);
	}

	private void Root_MouseLeave(object sender, MouseEventArgs e)
	{
		_isMouseInside = false;
		_hideChromeTimer.Stop();
		_hideChromeTimer.Start();
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		switch (msg)
		{
		case 532:
		case 534:
		case 561:
			_isSizingOrMoving = true;
			SetChromeVisible(visible: true);
			break;
		case 562:
			_isSizingOrMoving = false;
			if (_isMouseInside)
			{
				SetChromeVisible(visible: true);
			}
			else
			{
				_hideChromeTimer.Start();
			}
			break;
		}
		return IntPtr.Zero;
	}

	private void WindowDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed || IsInsideButton(e.OriginalSource as DependencyObject))
		{
			return;
		}
		try
		{
			DragMove();
		}
		catch
		{
		}
	}

	private static bool IsInsideButton(DependencyObject? source)
	{
		while (source != null)
		{
			if (source is Button)
			{
				return true;
			}
			source = VisualTreeHelper.GetParent(source);
		}
		return false;
	}

	private void CloseButton_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void HideBuffButton_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (sender is FrameworkElement { Tag: var tag } && tag is int e2)
		{
			this.HideBuffRequested?.Invoke(this, e2);
		}
	}

	private void RestoreHiddenBuffButton_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (sender is FrameworkElement { Tag: var tag } && tag is int e2)
		{
			this.RestoreBuffRequested?.Invoke(this, e2);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/bufftimerwindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			root = (Border)target;
			root.MouseEnter += Root_MouseEnter;
			root.MouseLeave += Root_MouseLeave;
			root.PreviewMouseLeftButtonDown += WindowDrag_MouseLeftButtonDown;
			root.MouseLeftButtonDown += WindowDrag_MouseLeftButtonDown;
			break;
		case 2:
			chromeSurface = (Border)target;
			break;
		case 3:
			presenceHint = (Border)target;
			break;
		case 4:
			itemsBuffTimers = (ItemsControl)target;
			break;
		case 6:
			hiddenBuffTray = (Border)target;
			break;
		case 7:
			itemsHiddenBuffTimers = (ItemsControl)target;
			break;
		case 9:
			txtEmpty = (TextBlock)target;
			break;
		case 10:
			btnClose = (Button)target;
			btnClose.Click += CloseButton_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IStyleConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 5:
			((Button)target).Click += HideBuffButton_Click;
			break;
		case 8:
			((Button)target).Click += RestoreHiddenBuffButton_Click;
			break;
		}
	}
}
