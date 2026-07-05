using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace INGMeter.App;

public class CombatDetailWindow : Window, IComponentConnector
{
	private enum BuffUptimeFilter
	{
		OwnSkill,
		Consumable,
		All
	}

	private IReadOnlyList<DpsGraphRow> _dpsGraphRows = Array.Empty<DpsGraphRow>();

	private IReadOnlyList<BuffUptimeRow> _buffRows = Array.Empty<BuffUptimeRow>();

	private IReadOnlyList<RdpsBuffRow> _rdpsRows = Array.Empty<RdpsBuffRow>();

	private double _rdpsAddedDps;

	private double _rdpsReducedDps;

	private string _lastSkillRowsSignature = "";

	private string _lastHealingRowsSignature = "";

	private string _lastLogRowsSignature = "";

	private string _lastDpsGraphRowsSignature = "";

	private string _lastBuffRowsSignature = "";

	private string _lastRdpsRowsSignature = "";

	private BuffUptimeFilter _buffFilter;

	private bool _isHeaderDragging;

	private Point _dragStartMouse;

	private Point _dragStartWindow;

	internal TextBlock txtActorName;

	internal TextBlock txtSummaryCombatPower;

	internal TextBlock txtSummaryAvgDps;

	internal TextBlock txtCombatTime;

	internal TextBlock txtSummaryDamage;

	internal TextBlock txtSummaryDps;

	internal TextBlock txtSummaryNdps;

	internal TextBlock txtSummaryRdps;

	internal TextBlock txtSummaryHealing;

	internal TextBlock txtSummaryHps;

	internal TextBlock txtSummaryCrit;

	internal TextBlock txtSummaryBack;

	internal TextBlock txtSummarySmite;

	internal TextBlock txtSummaryMulti;

	internal TextBlock txtSummaryPerfect;

	internal TextBlock txtSummaryAccuracy;

	internal TabControl tabRoot;

	internal TabItem tabSkills;

	internal DataGrid dgvSkills;

	internal TabItem tabDpsGraph;

	internal TextBlock txtDpsGraphSummary;

	internal Canvas dpsGraphCanvas;

	internal TextBlock txtDpsGraphEmpty;

	internal TabItem tabBuffs;

	internal ToggleButton btnBuffFilterOwnSkill;

	internal ToggleButton btnBuffFilterConsumable;

	internal ToggleButton btnBuffFilterAll;

	internal TextBlock txtBuffCount;

	internal ItemsControl itemsBuffs;

	internal TextBlock txtBuffEmpty;

	internal TabItem tabRdps;

	internal TextBlock txtRdpsSummary;

	internal TextBlock txtRdpsCount;

	internal ItemsControl itemsRdps;

	internal TextBlock txtRdpsEmpty;

	internal TabItem tabHealing;

	internal DataGrid dgvHealing;

	internal TabItem tabLog;

	internal DataGrid dgvLog;

	private bool _contentLoaded;

	public bool IsSkillTabSelected => tabSkills.IsSelected;

	public bool IsHealingTabSelected => tabHealing.IsSelected;

	public bool IsDpsGraphTabSelected => tabDpsGraph.IsSelected;

	public bool IsBuffTabSelected => tabBuffs.IsSelected;

	public bool IsRdpsTabSelected => tabRdps.IsSelected;

	public string? SkillSortMemberPath { get; private set; }

	public ListSortDirection? SkillSortDirection { get; private set; }

	public event Action? RefreshRequested;

	public CombatDetailWindow()
	{
		InitializeComponent();
		dpsGraphCanvas.SizeChanged += delegate
		{
			DrawDpsGraph();
		};
		base.Title = "전투 상세정보 : INGMeter";
	}

	public void SetActorTitle(string name)
	{
		txtActorName.Text = (string.IsNullOrWhiteSpace(name) ? "전투 상세정보" : (name + " 상세정보"));
	}

	public void SetCombatTime(string text)
	{
		txtCombatTime.Text = (string.IsNullOrWhiteSpace(text) ? "전투 시간: 00:00.0" : text);
	}

	public void SetSkillRows(IReadOnlyList<SkillRow> rows)
	{
		string text = BuildSkillRowsSignature(rows);
		if (!(text == _lastSkillRowsSignature))
		{
			_lastSkillRowsSignature = text;
			dgvSkills.ItemsSource = rows;
		}
	}

	public void SetHealingRows(IReadOnlyList<SkillRow> rows)
	{
		string text = BuildSkillRowsSignature(rows);
		if (!(text == _lastHealingRowsSignature))
		{
			_lastHealingRowsSignature = text;
			dgvHealing.ItemsSource = rows;
		}
	}

	public void SetSummary(CombatDetailSummary summary)
	{
		SetTextIfChanged(txtSummaryDamage, summary.TotalDamage);
		SetTextIfChanged(txtSummaryDps, summary.Dps);
		SetTextIfChanged(txtSummaryHealing, summary.TotalHealing);
		SetTextIfChanged(txtSummaryHps, summary.Hps);
		UpdateDerivedDpsMetrics(ParseDashboardNumber(summary.Dps));
		SetTextIfChanged(txtSummaryCrit, summary.CritRate);
		SetTextIfChanged(txtSummaryBack, summary.BackRate);
		SetTextIfChanged(txtSummarySmite, summary.SmiteRate);
		SetTextIfChanged(txtSummaryMulti, summary.MultiRate);
		SetTextIfChanged(txtSummaryPerfect, summary.PerfectRate);
		SetTextIfChanged(txtSummaryAccuracy, summary.AccuracyRate);
		SetTextIfChanged(txtSummaryCombatPower, summary.CombatPower);
		SetTextIfChanged(txtSummaryAvgDps, summary.AvgDps);
	}

	public void SetLogRows(IReadOnlyList<LogRow> rows)
	{
		string text = BuildLogRowsSignature(rows);
		if (!(text == _lastLogRowsSignature))
		{
			_lastLogRowsSignature = text;
			dgvLog.ItemsSource = rows;
		}
	}

	public void SetDpsGraphRows(IReadOnlyList<DpsGraphRow> rows)
	{
		string text = BuildDpsGraphRowsSignature(rows);
		if (!(text == _lastDpsGraphRowsSignature))
		{
			_lastDpsGraphRowsSignature = text;
			_dpsGraphRows = rows;
			DrawDpsGraph();
		}
	}

	public void SetBuffRows(IReadOnlyList<BuffUptimeRow> rows)
	{
		string text = BuildBuffRowsSignature(rows);
		if (!(text == _lastBuffRowsSignature))
		{
			_lastBuffRowsSignature = text;
			_buffRows = rows;
			ApplyBuffFilter();
		}
	}

	public void SetRdpsRows(IReadOnlyList<RdpsBuffRow> rows, double baseDps = double.NaN)
	{
		if (!double.IsFinite(baseDps))
		{
			baseDps = ParseDashboardNumber(txtSummaryDps.Text);
		}
		string text = BuildRdpsRowsSignature(rows, baseDps);
		if (!(text == _lastRdpsRowsSignature))
		{
			_lastRdpsRowsSignature = text;
			_rdpsRows = rows;
			_rdpsAddedDps = _rdpsRows.Sum((RdpsBuffRow row) => row.AdditionalDps);
			_rdpsReducedDps = _rdpsRows.Sum((RdpsBuffRow row) => row.ReducedDps);
			itemsRdps.ItemsSource = _rdpsRows;
			SetTextIfChanged(txtRdpsCount, $"{_rdpsRows.Count:N0}개");
			txtRdpsEmpty.Visibility = ((_rdpsRows.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
			double rdpsAddedDps = _rdpsAddedDps;
			double rdpsReducedDps = _rdpsReducedDps;
			double value = rdpsAddedDps - rdpsReducedDps;
			UpdateDerivedDpsMetrics(baseDps);
			SetTextIfChanged(txtRdpsSummary, $"가산 {rdpsAddedDps:N0} / 차감 {rdpsReducedDps:N0} / 순 {FormatSignedDps(value)}");
		}
	}

	private void BuffFilter_Click(object sender, RoutedEventArgs e)
	{
		if (sender == btnBuffFilterConsumable)
		{
			_buffFilter = BuffUptimeFilter.Consumable;
		}
		else if (sender == btnBuffFilterAll)
		{
			_buffFilter = BuffUptimeFilter.All;
		}
		else
		{
			_buffFilter = BuffUptimeFilter.OwnSkill;
		}
		ApplyBuffFilter();
	}

	private void ApplyBuffFilter()
	{
		IReadOnlyList<BuffUptimeRow> readOnlyList = _buffFilter switch
		{
			BuffUptimeFilter.Consumable => _buffRows.Where((BuffUptimeRow row) => row.IsConsumable).ToList(), 
			BuffUptimeFilter.All => _buffRows, 
			_ => _buffRows.Where((BuffUptimeRow row) => row.IsOwnSkill).ToList(), 
		};
		itemsBuffs.ItemsSource = readOnlyList;
		SetTextIfChanged(txtBuffCount, $"{readOnlyList.Count:N0}개");
		txtBuffEmpty.Visibility = ((readOnlyList.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		btnBuffFilterOwnSkill.IsChecked = _buffFilter == BuffUptimeFilter.OwnSkill;
		btnBuffFilterConsumable.IsChecked = _buffFilter == BuffUptimeFilter.Consumable;
		btnBuffFilterAll.IsChecked = _buffFilter == BuffUptimeFilter.All;
	}

	private static string BuildSkillRowsSignature(IReadOnlyList<SkillRow> rows)
	{
		if (rows.Count == 0)
		{
			return "0";
		}
		long value = rows.Sum((SkillRow row) => row.RawDamage);
		long value2 = rows.Sum((SkillRow row) => row.RawHealing);
		long value3 = rows.Sum((SkillRow row) => row.RawSelfHealing);
		long value4 = rows.Sum((SkillRow row) => row.RawOtherHealing);
		string value5 = string.Join(";", from row in rows.Take(8)
			select $"{row.Name}:{row.RawDamage}:{row.RawHealing}:{row.RawSelfHealing}:{row.RawOtherHealing}:{row.Hit}:{row.Evade}:{row.Parry}");
		SkillRow skillRow = rows[rows.Count - 1];
		return $"{rows.Count}|{value}|{value2}|{value3}|{value4}|{value5}|{skillRow.Name}:{skillRow.RawDamage}:{skillRow.RawHealing}:{skillRow.RawSelfHealing}:{skillRow.RawOtherHealing}:{skillRow.Hit}:{skillRow.Evade}:{skillRow.Parry}";
	}

	private static string BuildLogRowsSignature(IReadOnlyList<LogRow> rows)
	{
		if (rows.Count == 0)
		{
			return "0";
		}
		LogRow logRow = rows[0];
		LogRow logRow2 = rows[rows.Count - 1];
		return $"{rows.Count}|{logRow.TimeStr}:{logRow.Damage}:{logRow.SkillName}|{logRow2.TimeStr}:{logRow2.Damage}:{logRow2.SkillName}";
	}

	private static string BuildDpsGraphRowsSignature(IReadOnlyList<DpsGraphRow> rows)
	{
		if (rows.Count == 0)
		{
			return "0";
		}
		DpsGraphRow dpsGraphRow = rows[0];
		DpsGraphRow dpsGraphRow2 = rows[rows.Count - 1];
		long value = rows.Sum((DpsGraphRow row) => row.Damage);
		return $"{rows.Count}|{dpsGraphRow.Second}:{dpsGraphRow.Dps}|{dpsGraphRow2.Second}:{dpsGraphRow2.Dps}|{value}";
	}

	private static string BuildBuffRowsSignature(IReadOnlyList<BuffUptimeRow> rows)
	{
		if (rows.Count == 0)
		{
			return "0";
		}
		_ = rows[0];
		BuffUptimeRow buffUptimeRow = rows[rows.Count - 1];
		int value = rows.Sum((BuffUptimeRow row) => row.ApplyCount);
		double value2 = rows.Sum((BuffUptimeRow row) => row.UptimeSeconds);
		string value3 = string.Join(";", from row in rows.Take(16)
			select $"{row.Name}:{row.LevelText}:{row.UptimePercentValue:0.###}");
		return $"{rows.Count}|{value}|{value2:0.###}|{value3}|{buffUptimeRow.Name}:{buffUptimeRow.LevelText}:{buffUptimeRow.UptimePercentValue:0.###}";
	}

	private static string BuildRdpsRowsSignature(IReadOnlyList<RdpsBuffRow> rows, double baseDps)
	{
		double value = (double.IsFinite(baseDps) ? baseDps : double.NaN);
		if (rows.Count != 0)
		{
			RdpsBuffRow rdpsBuffRow = rows[0];
			RdpsBuffRow rdpsBuffRow2 = rows[rows.Count - 1];
			double value2 = rows.Sum((RdpsBuffRow row) => row.AdditionalDps);
			double value3 = rows.Sum((RdpsBuffRow row) => row.ReducedDps);
			return $"{rows.Count}|{value:0.###}|{value2:0.###}|{value3:0.###}|{rdpsBuffRow.Name}:{rdpsBuffRow.NetDps:0.###}|{rdpsBuffRow2.Name}:{rdpsBuffRow2.NetDps:0.###}";
		}
		return $"0|{value:0.###}";
	}

	private static void SetTextIfChanged(TextBlock textBlock, string value)
	{
		if (!string.Equals(textBlock.Text, value, StringComparison.Ordinal))
		{
			textBlock.Text = value;
		}
	}

	private void UpdateDerivedDpsMetrics(double baseDps)
	{
		if (!double.IsFinite(baseDps))
		{
			baseDps = 0.0;
		}
		double num = Math.Max(0.0, baseDps - _rdpsReducedDps);
		double num2 = Math.Max(0.0, num + _rdpsAddedDps);
		SetTextIfChanged(txtSummaryNdps, num.ToString("N0"));
		SetTextIfChanged(txtSummaryRdps, num2.ToString("N0"));
	}

	private static double ParseDashboardNumber(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return 0.0;
		}
		if (!double.TryParse(value.Replace(",", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return 0.0;
		}
		return result;
	}

	private static string FormatSignedDps(double value)
	{
		if (value > 0.5)
		{
			return $"+{value:N0}";
		}
		if (value < -0.5)
		{
			return $"-{Math.Abs(value):N0}";
		}
		return "0";
	}

	private void DgvSkills_Sorting(object sender, DataGridSortingEventArgs e)
	{
		e.Handled = true;
		ListSortDirection value = ((e.Column.SortDirection == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending);
		foreach (DataGridColumn column in dgvSkills.Columns)
		{
			column.SortDirection = null;
		}
		e.Column.SortDirection = value;
		SkillSortMemberPath = e.Column.SortMemberPath;
		SkillSortDirection = value;
		dgvSkills.Items.SortDescriptions.Clear();
		this.RefreshRequested?.Invoke();
	}

	private void TabRoot_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (sender == tabRoot)
		{
			this.RefreshRequested?.Invoke();
		}
	}

	private void DrawDpsGraph()
	{
		if (dpsGraphCanvas == null || txtDpsGraphEmpty == null || txtDpsGraphSummary == null)
		{
			return;
		}
		dpsGraphCanvas.Children.Clear();
		IReadOnlyList<DpsGraphRow> readOnlyList = _dpsGraphRows ?? Array.Empty<DpsGraphRow>();
		txtDpsGraphEmpty.Visibility = ((readOnlyList.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		if (readOnlyList.Count == 0)
		{
			txtDpsGraphSummary.Text = "데이터 없음";
			return;
		}
		double actualWidth = dpsGraphCanvas.ActualWidth;
		double actualHeight = dpsGraphCanvas.ActualHeight;
		if (actualWidth < 120.0 || actualHeight < 90.0)
		{
			return;
		}
		long num = Math.Max(1L, readOnlyList.Max((DpsGraphRow r) => r.Dps));
		long value = readOnlyList.Sum((DpsGraphRow r) => r.Damage);
		long dps = readOnlyList[readOnlyList.Count - 1].Dps;
		txtDpsGraphSummary.Text = $"현재 {dps:N0} / 최고 {num:N0} / 피해 {value:N0}";
		double num2 = 52.0;
		double num3 = 16.0;
		double num4 = 18.0;
		double num5 = 30.0;
		double num6 = Math.Max(1.0, actualWidth - num2 - num3);
		double num7 = Math.Max(1.0, actualHeight - num4 - num5);
		Brush stroke = FindBrush("ThemeBorderBrush", Brushes.DimGray);
		Brush brush = FindBrush("ThemeTextSecondaryBrush", Brushes.Gray);
		Brush brush2 = FindBrush("DpsValueBrush", Brushes.DeepSkyBlue);
		Brush fill = FindBrush("ThemeAccentBrush", brush2);
		for (int num8 = 0; num8 <= 4; num8++)
		{
			double num9 = (double)num8 / 4.0;
			double num10 = num4 + num9 * num7;
			dpsGraphCanvas.Children.Add(new Line
			{
				X1 = num2,
				X2 = num2 + num6,
				Y1 = num10,
				Y2 = num10,
				Stroke = stroke,
				StrokeThickness = ((num8 == 4) ? 1.2 : 0.8),
				Opacity = ((num8 == 4) ? 0.7 : 0.35)
			});
			long value2 = (long)Math.Round((double)num * (1.0 - num9));
			TextBlock element = new TextBlock
			{
				Text = FormatAxisValue(value2),
				Foreground = brush,
				FontSize = 10.0,
				FontWeight = FontWeights.SemiBold
			};
			Canvas.SetLeft(element, 0.0);
			Canvas.SetTop(element, Math.Max(0.0, num10 - 8.0));
			dpsGraphCanvas.Children.Add(element);
		}
		dpsGraphCanvas.Children.Add(new Line
		{
			X1 = num2,
			X2 = num2,
			Y1 = num4,
			Y2 = num4 + num7,
			Stroke = stroke,
			StrokeThickness = 1.0,
			Opacity = 0.6
		});
		List<Point> list = new List<Point>();
		for (int num11 = 0; num11 < readOnlyList.Count; num11++)
		{
			double x = ((readOnlyList.Count == 1) ? (num2 + num6 / 2.0) : (num2 + num6 * (double)num11 / (double)(readOnlyList.Count - 1)));
			double y = num4 + (1.0 - Math.Clamp((double)readOnlyList[num11].Dps / (double)num, 0.0, 1.0)) * num7;
			list.Add(new Point(x, y));
		}
		dpsGraphCanvas.Children.Add(new Path
		{
			Data = CreateSmoothLineGeometry(list),
			Stroke = brush2,
			StrokeThickness = 2.4,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			StrokeLineJoin = PenLineJoin.Round
		});
		if (readOnlyList.Count <= 90)
		{
			foreach (Point item in list)
			{
				Ellipse element2 = new Ellipse
				{
					Width = 5.0,
					Height = 5.0,
					Fill = fill,
					Stroke = brush2,
					StrokeThickness = 1.0
				};
				Canvas.SetLeft(element2, item.X - 2.5);
				Canvas.SetTop(element2, item.Y - 2.5);
				dpsGraphCanvas.Children.Add(element2);
			}
		}
		DrawTimeLabel(readOnlyList[0].TimeRange, num2, num4 + num7 + 8.0, brush);
		if (readOnlyList.Count > 2)
		{
			DrawTimeLabel(readOnlyList[readOnlyList.Count / 2].TimeRange, num2 + num6 / 2.0 - 18.0, num4 + num7 + 8.0, brush);
		}
		DrawTimeLabel(readOnlyList[readOnlyList.Count - 1].TimeRange, num2 + num6 - 42.0, num4 + num7 + 8.0, brush);
	}

	private static Geometry CreateSmoothLineGeometry(IReadOnlyList<Point> points)
	{
		PathGeometry pathGeometry = new PathGeometry();
		if (points.Count == 0)
		{
			return pathGeometry;
		}
		PathFigure pathFigure = new PathFigure
		{
			StartPoint = points[0],
			IsClosed = false,
			IsFilled = false
		};
		if (points.Count == 1)
		{
			pathGeometry.Figures.Add(pathFigure);
			return pathGeometry;
		}
		for (int i = 0; i < points.Count - 1; i++)
		{
			Point point = ((i == 0) ? points[i] : points[i - 1]);
			Point point2 = points[i];
			Point point3 = points[i + 1];
			Point point4 = ((i + 2 < points.Count) ? points[i + 2] : point3);
			Point point5 = new Point(point2.X + (point3.X - point.X) / 6.0, ClampBetween(point2.Y + (point3.Y - point.Y) / 6.0, point2.Y, point3.Y));
			Point point6 = new Point(point3.X - (point4.X - point2.X) / 6.0, ClampBetween(point3.Y - (point4.Y - point2.Y) / 6.0, point2.Y, point3.Y));
			pathFigure.Segments.Add(new BezierSegment(point5, point6, point3, isStroked: true));
		}
		pathGeometry.Figures.Add(pathFigure);
		return pathGeometry;
	}

	private static double ClampBetween(double value, double a, double b)
	{
		return Math.Clamp(value, Math.Min(a, b), Math.Max(a, b));
	}

	private void DrawTimeLabel(string text, double left, double top, Brush brush)
	{
		TextBlock element = new TextBlock
		{
			Text = text,
			Foreground = brush,
			FontSize = 10.0,
			FontWeight = FontWeights.SemiBold
		};
		Canvas.SetLeft(element, Math.Max(0.0, left));
		Canvas.SetTop(element, top);
		dpsGraphCanvas.Children.Add(element);
	}

	private Brush FindBrush(string resourceKey, Brush fallback)
	{
		return (TryFindResource(resourceKey) as Brush) ?? fallback;
	}

	private static string FormatAxisValue(long value)
	{
		if (value >= 1000000)
		{
			return $"{(double)value / 1000000.0:0.#}M";
		}
		if (value >= 10000)
		{
			return $"{(double)value / 1000.0:0.#}K";
		}
		return value.ToString("N0");
	}

	private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left)
		{
			_isHeaderDragging = true;
			_dragStartMouse = GetMouseScreenDip(e);
			_dragStartWindow = new Point(base.Left, base.Top);
			if (sender is UIElement uIElement)
			{
				uIElement.CaptureMouse();
			}
			e.Handled = true;
		}
	}

	private void Header_MouseMove(object sender, MouseEventArgs e)
	{
		if (_isHeaderDragging)
		{
			if (e.LeftButton != MouseButtonState.Pressed)
			{
				EndHeaderDrag(sender);
				return;
			}
			Point mouseScreenDip = GetMouseScreenDip(e);
			base.Left = _dragStartWindow.X + mouseScreenDip.X - _dragStartMouse.X;
			base.Top = _dragStartWindow.Y + mouseScreenDip.Y - _dragStartMouse.Y;
			e.Handled = true;
		}
	}

	private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_isHeaderDragging)
		{
			EndHeaderDrag(sender);
			e.Handled = true;
		}
	}

	private void Header_LostMouseCapture(object sender, MouseEventArgs e)
	{
		_isHeaderDragging = false;
	}

	private void EndHeaderDrag(object? sender)
	{
		_isHeaderDragging = false;
		if (sender is UIElement { IsMouseCaptured: not false } uIElement)
		{
			uIElement.ReleaseMouseCapture();
		}
	}

	private Point GetMouseScreenDip(MouseEventArgs e)
	{
		Point point = PointToScreen(e.GetPosition(this));
		return (PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity).Transform(point);
	}

	private void BtnMinimize_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Close();
			e.Handled = true;
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/combatdetailwindow.xaml", UriKind.Relative);
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
			((CombatDetailWindow)target).PreviewKeyDown += Window_PreviewKeyDown;
			break;
		case 2:
			((Border)target).MouseLeftButtonDown += Header_MouseLeftButtonDown;
			((Border)target).MouseMove += Header_MouseMove;
			((Border)target).MouseLeftButtonUp += Header_MouseLeftButtonUp;
			((Border)target).LostMouseCapture += Header_LostMouseCapture;
			break;
		case 3:
			txtActorName = (TextBlock)target;
			break;
		case 4:
			txtSummaryCombatPower = (TextBlock)target;
			break;
		case 5:
			txtSummaryAvgDps = (TextBlock)target;
			break;
		case 6:
			txtCombatTime = (TextBlock)target;
			break;
		case 7:
			((Button)target).Click += BtnMinimize_Click;
			break;
		case 8:
			((Button)target).Click += BtnClose_Click;
			break;
		case 9:
			txtSummaryDamage = (TextBlock)target;
			break;
		case 10:
			txtSummaryDps = (TextBlock)target;
			break;
		case 11:
			txtSummaryNdps = (TextBlock)target;
			break;
		case 12:
			txtSummaryRdps = (TextBlock)target;
			break;
		case 13:
			txtSummaryHealing = (TextBlock)target;
			break;
		case 14:
			txtSummaryHps = (TextBlock)target;
			break;
		case 15:
			txtSummaryCrit = (TextBlock)target;
			break;
		case 16:
			txtSummaryBack = (TextBlock)target;
			break;
		case 17:
			txtSummarySmite = (TextBlock)target;
			break;
		case 18:
			txtSummaryMulti = (TextBlock)target;
			break;
		case 19:
			txtSummaryPerfect = (TextBlock)target;
			break;
		case 20:
			txtSummaryAccuracy = (TextBlock)target;
			break;
		case 21:
			tabRoot = (TabControl)target;
			tabRoot.SelectionChanged += TabRoot_SelectionChanged;
			break;
		case 22:
			tabSkills = (TabItem)target;
			break;
		case 23:
			dgvSkills = (DataGrid)target;
			dgvSkills.Sorting += DgvSkills_Sorting;
			break;
		case 24:
			tabDpsGraph = (TabItem)target;
			break;
		case 25:
			txtDpsGraphSummary = (TextBlock)target;
			break;
		case 26:
			dpsGraphCanvas = (Canvas)target;
			break;
		case 27:
			txtDpsGraphEmpty = (TextBlock)target;
			break;
		case 28:
			tabBuffs = (TabItem)target;
			break;
		case 29:
			btnBuffFilterOwnSkill = (ToggleButton)target;
			btnBuffFilterOwnSkill.Click += BuffFilter_Click;
			break;
		case 30:
			btnBuffFilterConsumable = (ToggleButton)target;
			btnBuffFilterConsumable.Click += BuffFilter_Click;
			break;
		case 31:
			btnBuffFilterAll = (ToggleButton)target;
			btnBuffFilterAll.Click += BuffFilter_Click;
			break;
		case 32:
			txtBuffCount = (TextBlock)target;
			break;
		case 33:
			itemsBuffs = (ItemsControl)target;
			break;
		case 34:
			txtBuffEmpty = (TextBlock)target;
			break;
		case 35:
			tabRdps = (TabItem)target;
			break;
		case 36:
			txtRdpsSummary = (TextBlock)target;
			break;
		case 37:
			txtRdpsCount = (TextBlock)target;
			break;
		case 38:
			itemsRdps = (ItemsControl)target;
			break;
		case 39:
			txtRdpsEmpty = (TextBlock)target;
			break;
		case 40:
			tabHealing = (TabItem)target;
			break;
		case 41:
			dgvHealing = (DataGrid)target;
			break;
		case 42:
			tabLog = (TabItem)target;
			break;
		case 43:
			dgvLog = (DataGrid)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
