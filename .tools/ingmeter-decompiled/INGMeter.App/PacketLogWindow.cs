using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using INGMeter.Core;

namespace INGMeter.App;

public class PacketLogWindow : Window, IComponentConnector, IStyleConnector
{
	private readonly MeterEngine _engine;

	private readonly SkillNameMap _skillNames;

	private readonly ICollectionView _entriesView;

	private bool _isPaused;

	private int _totalAccepted;

	private int _totalFiltered;

	private const string KindFilterNative = "__native";

	private const string KindFilterPrefix = "kind:";

	private readonly ObservableCollection<KindFilterOption> _kindFilterOptions = new ObservableCollection<KindFilterOption>();

	private readonly Dictionary<string, string> _knownKinds = new Dictionary<string, string>(StringComparer.Ordinal);

	private bool _isUpdatingKindFilter;

	private Action<DamageEvent, string>? _logHandler;

	private Action<NativePacketInfo>? _nativeInfoHandler;

	internal TextBlock txtStatus;

	internal Button btnPause;

	internal ToggleButton btnKindFilter;

	internal TextBlock txtKindFilterSummary;

	internal Popup popKindFilter;

	internal ItemsControl itemsKindFilter;

	internal TextBox txtActorFilter;

	internal TextBlock txtCount;

	internal CheckBox chkAutoScroll;

	internal DataGrid dgLog;

	internal TextBlock txtInfo;

	internal TextBlock txtStats;

	private bool _contentLoaded;

	public ObservableCollection<PacketLogEntry> AllEntries { get; } = new ObservableCollection<PacketLogEntry>();

	public PacketLogWindow(MeterEngine engine, SkillNameMap skillNames)
	{
		InitializeComponent();
		_engine = engine;
		_skillNames = skillNames;
		itemsKindFilter.ItemsSource = _kindFilterOptions;
		RebuildKindFilterOptions();
		_entriesView = CollectionViewSource.GetDefaultView(AllEntries);
		_entriesView.Filter = FilterEntries;
		dgLog.ItemsSource = _entriesView;
		_logHandler = OnPacketParsed;
		_engine.PacketLogEvent += _logHandler;
		_nativeInfoHandler = OnNativePacketInfo;
		_engine.NativePacketInfoReceived += _nativeInfoHandler;
		_engine.SetNativeLookupTraceEnabled(enabled: true);
		DispatcherTimer dispatcherTimer = new DispatcherTimer();
		dispatcherTimer.Interval = TimeSpan.FromMilliseconds(500L);
		dispatcherTimer.Tick += delegate
		{
			UpdateStats();
		};
		dispatcherTimer.Start();
	}

	private void OnPacketParsed(DamageEvent evt, string filterReason)
	{
		if (!_isPaused)
		{
			string name;
			string actorName = (_engine.TryGetActorName(evt.ActorId, out name) ? (name ?? $"#{evt.ActorId}") : $"#{evt.ActorId}");
			string name2;
			string targetName = ((evt.TargetId == 0) ? "-" : (_engine.TryGetActorName(evt.TargetId, out name2) ? (name2 ?? $"#{evt.TargetId}") : $"#{evt.TargetId}"));
			string status;
			if (string.IsNullOrEmpty(filterReason))
			{
				status = "적용";
				_totalAccepted++;
			}
			else
			{
				status = (filterReason.Contains("힐") ? "힐제외" : "제외");
				_totalFiltered++;
			}
			string specials = ((evt.Specials != null && evt.Specials.Count > 0) ? string.Join(", ", evt.Specials) : "");
			PacketLogEntry obj = new PacketLogEntry
			{
				Time = evt.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"),
				Status = status,
				FilterReason = (filterReason ?? ""),
				Kind = "DamageRecord",
				IsExcluded = !string.IsNullOrEmpty(filterReason),
				IsNative = false,
				ActorName = actorName,
				TargetName = targetName,
				SkillCode = evt.SkillCodeRaw.ToString(),
				SkillName = _skillNames.GetNameOrCode(evt.SkillCodeRaw),
				Damage = ((evt.Damage > 0) ? evt.Damage.ToString("N0") : ""),
				MultiDamage = ((evt.MultiHitDamage > 0) ? evt.MultiHitDamage.ToString("N0") : ""),
				HealAmount = ((evt.HealAmount > 0) ? evt.HealAmount.ToString("N0") : ""),
				TypeInfo = evt.Type.ToString(),
				Specials = specials,
				IsDot = (evt.IsDot ? "●" : ""),
				Flag = evt.Flag.ToString(),
				RawPacket = evt.RawPacket,
				SwitchVar = evt.SwitchVar,
				RawSkillCode = evt.SkillCodeRaw,
				RawDamage = evt.Damage,
				RawMultiDamage = evt.MultiHitDamage,
				RawHealAmount = evt.HealAmount
			};
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(67, 6);
			defaultInterpolatedStringHandler.AppendLiteral("rawBytes=");
			byte[]? rawPacket = evt.RawPacket;
			defaultInterpolatedStringHandler.AppendFormatted((rawPacket != null) ? rawPacket.Length : 0);
			defaultInterpolatedStringHandler.AppendLiteral(", switch=");
			defaultInterpolatedStringHandler.AppendFormatted(evt.SwitchVar);
			defaultInterpolatedStringHandler.AppendLiteral(", unknown=");
			defaultInterpolatedStringHandler.AppendFormatted(evt.Unknown);
			defaultInterpolatedStringHandler.AppendLiteral(", skillLevel=");
			defaultInterpolatedStringHandler.AppendFormatted(evt.SkillLevel);
			defaultInterpolatedStringHandler.AppendLiteral(", baseSkillLevel=");
			defaultInterpolatedStringHandler.AppendFormatted(evt.BaseSkillLevel);
			defaultInterpolatedStringHandler.AppendLiteral(", filter=");
			defaultInterpolatedStringHandler.AppendFormatted(evt.FilterReason ?? filterReason ?? "");
			obj.Detail = defaultInterpolatedStringHandler.ToStringAndClear();
			PacketLogEntry entry = obj;
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				AppendEntry(entry);
			}, DispatcherPriority.Background);
		}
	}

	private void OnNativePacketInfo(NativePacketInfo info)
	{
		if (!_isPaused)
		{
			_totalAccepted++;
			PacketLogEntry entry = new PacketLogEntry
			{
				Time = info.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"),
				Status = "DLL",
				Kind = info.Kind,
				FilterReason = "",
				IsExcluded = false,
				IsNative = true,
				ActorName = ((info.PrimaryId > 0) ? $"#{info.PrimaryId}" : "-"),
				TargetName = ((info.SecondaryId > 0) ? $"#{info.SecondaryId}" : "-"),
				SkillCode = ((info.SkillCode > 0) ? info.SkillCode.ToString() : ""),
				SkillName = ((info.SkillCode > 0) ? _skillNames.GetNameOrCode(info.SkillCode) : ""),
				Damage = ((info.Value != 0L) ? info.Value.ToString("N0") : ""),
				MultiDamage = "",
				HealAmount = "",
				TypeInfo = "native",
				Specials = "",
				IsDot = "",
				Flag = "",
				RawPacket = null,
				SwitchVar = 0,
				RawSkillCode = info.SkillCode,
				RawDamage = 0,
				RawMultiDamage = 0,
				RawHealAmount = 0,
				Detail = (string.IsNullOrWhiteSpace(info.Detail) ? info.Summary : info.Detail)
			};
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				AppendEntry(entry);
			}, DispatcherPriority.Background);
		}
	}

	private void AppendEntry(PacketLogEntry entry)
	{
		if (AllEntries.Count >= 5000)
		{
			for (int i = 0; i < 500; i++)
			{
				if (AllEntries.Count <= 0)
				{
					break;
				}
				AllEntries.RemoveAt(0);
			}
		}
		RegisterKind(entry);
		AllEntries.Add(entry);
		UpdateStats();
		ScrollToLastVisible();
	}

	private void ScrollToLastVisible()
	{
		if (chkAutoScroll.IsChecked != true || _entriesView == null)
		{
			return;
		}
		object obj = null;
		foreach (object item in _entriesView)
		{
			obj = item;
		}
		if (obj != null)
		{
			dgLog.ScrollIntoView(obj);
		}
	}

	private void RegisterKind(PacketLogEntry entry)
	{
		if (!string.IsNullOrWhiteSpace(entry.Kind))
		{
			string key = "kind:" + entry.Kind;
			if (!_knownKinds.ContainsKey(key))
			{
				_knownKinds[key] = entry.Kind;
				RebuildKindFilterOptions();
			}
		}
	}

	private void RebuildKindFilterOptions()
	{
		HashSet<string> hashSet = (from x in _kindFilterOptions
			where x.IsSelected
			select x.Key).ToHashSet<string>(StringComparer.Ordinal);
		_isUpdatingKindFilter = true;
		try
		{
			_kindFilterOptions.Clear();
			_kindFilterOptions.Add(new KindFilterOption("__native", "DLL 콜백 전체", hashSet.Contains("__native")));
			foreach (KeyValuePair<string, string> item in _knownKinds.OrderBy<KeyValuePair<string, string>, string>((KeyValuePair<string, string> x) => x.Value, StringComparer.CurrentCultureIgnoreCase))
			{
				_kindFilterOptions.Add(new KindFilterOption(item.Key, item.Value, hashSet.Contains(item.Key)));
			}
		}
		finally
		{
			_isUpdatingKindFilter = false;
		}
		UpdateKindFilterSummary();
	}

	private void UpdateStats()
	{
		txtStats.Text = $"적용: {_totalAccepted:N0}  |  제외: {_totalFiltered:N0}  |  합계: {_totalAccepted + _totalFiltered:N0}";
		int count = AllEntries.Count;
		int num = 0;
		foreach (object item in _entriesView)
		{
			_ = item;
			num++;
		}
		txtCount.Text = ((num == count) ? $"{count}건" : $"{num} / {count}건");
		txtStatus.Text = (_isPaused ? "  ⏸ 일시정지" : "  ● 수신중...");
		txtStatus.Foreground = (_isPaused ? new SolidColorBrush(Color.FromRgb(byte.MaxValue, 107, 107)) : new SolidColorBrush(Color.FromRgb(78, 204, 163)));
	}

	private bool FilterEntries(object item)
	{
		if (!(item is PacketLogEntry packetLogEntry))
		{
			return false;
		}
		HashSet<string> selectedKindKeys = GetSelectedKindKeys();
		if (!MatchesKindFilter(packetLogEntry, selectedKindKeys))
		{
			return false;
		}
		string value = txtActorFilter.Text.Trim();
		if (string.IsNullOrEmpty(value))
		{
			return true;
		}
		return packetLogEntry.ActorName.Contains(value, StringComparison.OrdinalIgnoreCase);
	}

	private HashSet<string> GetSelectedKindKeys()
	{
		return (from x in _kindFilterOptions
			where x.IsSelected
			select x.Key).ToHashSet<string>(StringComparer.Ordinal);
	}

	private static bool MatchesKindFilter(PacketLogEntry entry, HashSet<string> selectedKinds)
	{
		if (selectedKinds.Count == 0)
		{
			return true;
		}
		if (entry.IsNative && selectedKinds.Contains("__native"))
		{
			return true;
		}
		return selectedKinds.Contains("kind:" + entry.Kind);
	}

	private void TxtActorFilter_TextChanged(object sender, TextChangedEventArgs e)
	{
		_entriesView.Refresh();
		UpdateStats();
	}

	private void KindFilterCheckChanged(object sender, RoutedEventArgs e)
	{
		if (!_isUpdatingKindFilter && _entriesView != null)
		{
			_entriesView.Refresh();
			UpdateKindFilterSummary();
			UpdateStats();
			ScrollToLastVisible();
		}
	}

	private void BtnClearKindFilter_Click(object sender, RoutedEventArgs e)
	{
		_isUpdatingKindFilter = true;
		try
		{
			foreach (KindFilterOption kindFilterOption in _kindFilterOptions)
			{
				kindFilterOption.IsSelected = false;
			}
		}
		finally
		{
			_isUpdatingKindFilter = false;
		}
		_entriesView.Refresh();
		UpdateKindFilterSummary();
		UpdateStats();
		ScrollToLastVisible();
	}

	private void UpdateKindFilterSummary()
	{
		List<KindFilterOption> list = _kindFilterOptions.Where((KindFilterOption x) => x.IsSelected).ToList();
		TextBlock textBlock = txtKindFilterSummary;
		textBlock.Text = list.Count switch
		{
			0 => "전체", 
			1 => list[0].Label, 
			_ => $"{list.Count}개 선택", 
		};
	}

	private void BtnPause_Click(object sender, RoutedEventArgs e)
	{
		_isPaused = !_isPaused;
		btnPause.Content = (_isPaused ? "▶ 재개" : "⏸ 일시정지");
	}

	private void BtnClear_Click(object sender, RoutedEventArgs e)
	{
		AllEntries.Clear();
		txtCount.Text = "0건";
		_totalAccepted = 0;
		_totalFiltered = 0;
		_knownKinds.Clear();
		RebuildKindFilterOptions();
		UpdateStats();
	}

	private void BtnShowFilters_Click(object sender, RoutedEventArgs e)
	{
		string text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\r\n\ud83d\udccb 딜량 제외 필터 규칙 (CombatAggregator)\r\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\r\n\r\n1\ufe0f\u20e3  UNKNOWN 특수비트 포함 패킷\r\n   → e.Specials에 SpecialDamage.UNKNOWN이 포함된 경우\r\n   → 알 수 없는 비트(0x02, 0x20 등)가 있으면 오탐으로 판단\r\n\r\n2\ufe0f\u20e3  액터ID 범위 필터\r\n   → ActorId < 1 또는 ActorId > 99999 → 제외\r\n   → 유효하지 않은 ID (시스템 패킷 등)\r\n\r\n3\ufe0f\u20e3  타겟ID 범위 필터\r\n   → TargetId ≠ 0 이면서 TargetId < 1 또는 > 99999 → 제외\r\n\r\n4\ufe0f\u20e3  스킬ID 음수 필터\r\n   → SkillCodeRaw < 0 → 오탐 제외\r\n\r\n5\ufe0f\u20e3  저스킬+저데미지 필터\r\n   → SkillCodeRaw < 1000 이면서 실제 데미지 < 100 → 쓰레기 패킷 제외\r\n\r\n6\ufe0f\u20e3  순수 힐 패킷 필터 (HealAmount > 0, Damage = 0)\n   → 흡수/회복량이 붙어도 실제 데미지가 있으면 딜량에 포함합니다.\n   → 데미지 없이 힐량만 있는 패킷만 제외됩니다.\n\r\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\r\n\ud83d\udca1 필터에 걸린 패킷은 로그에 '제외' 또는 '힐제외'로 표시됩니다.\r\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";
		Window window = new Window();
		window.Title = "\ud83d\udccb 딜량 제외 필터 규칙";
		window.Width = 520.0;
		window.Height = 580.0;
		window.Background = new SolidColorBrush(Color.FromRgb(26, 26, 46));
		window.Foreground = new SolidColorBrush(Colors.White);
		window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
		window.Owner = this;
		window.WindowStyle = WindowStyle.ToolWindow;
		TextBox content = new TextBox
		{
			Text = text,
			IsReadOnly = true,
			Background = new SolidColorBrush(Color.FromRgb(26, 26, 46)),
			Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 220)),
			FontFamily = new FontFamily("Consolas, 맑은 고딕"),
			FontSize = 13.0,
			BorderThickness = new Thickness(0.0),
			Padding = new Thickness(15.0),
			TextWrapping = TextWrapping.Wrap,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			AcceptsReturn = true
		};
		window.Content = content;
		window.ShowDialog();
	}

	private void Header_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left)
		{
			DragMove();
		}
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	protected override void OnClosed(EventArgs e)
	{
		if (_logHandler != null)
		{
			_engine.PacketLogEvent -= _logHandler;
		}
		if (_nativeInfoHandler != null)
		{
			_engine.NativePacketInfoReceived -= _nativeInfoHandler;
		}
		_engine.SetNativeLookupTraceEnabled(enabled: false);
		base.OnClosed(e);
	}

	private void DgLog_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (dgLog.SelectedItem is PacketLogEntry { RawPacket: not null } packetLogEntry && packetLogEntry.RawPacket.Length != 0)
		{
			ShowPacketViewer(packetLogEntry);
		}
	}

	private void ShowPacketViewer(PacketLogEntry entry)
	{
		DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(110, 17);
		defaultInterpolatedStringHandler.AppendLiteral("시간: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.Time);
		defaultInterpolatedStringHandler.AppendLiteral("\n");
		defaultInterpolatedStringHandler.AppendLiteral("상태: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.Status);
		defaultInterpolatedStringHandler.AppendLiteral(" ");
		defaultInterpolatedStringHandler.AppendFormatted(string.IsNullOrEmpty(entry.FilterReason) ? "" : ("(사유: " + entry.FilterReason + ")"));
		defaultInterpolatedStringHandler.AppendLiteral("\n");
		defaultInterpolatedStringHandler.AppendLiteral("종류: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.Kind);
		defaultInterpolatedStringHandler.AppendLiteral("\n");
		defaultInterpolatedStringHandler.AppendLiteral("액터: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.ActorName);
		defaultInterpolatedStringHandler.AppendLiteral("  →  타겟: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.TargetName);
		defaultInterpolatedStringHandler.AppendLiteral("\n");
		defaultInterpolatedStringHandler.AppendLiteral("스킬: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.SkillName);
		defaultInterpolatedStringHandler.AppendLiteral(" (code=");
		defaultInterpolatedStringHandler.AppendFormatted(entry.SkillCode);
		defaultInterpolatedStringHandler.AppendLiteral(")\n");
		defaultInterpolatedStringHandler.AppendLiteral("데미지: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.RawDamage, "N0");
		defaultInterpolatedStringHandler.AppendLiteral("  멀티: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.RawMultiDamage, "N0");
		defaultInterpolatedStringHandler.AppendLiteral("  힐: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.RawHealAmount, "N0");
		defaultInterpolatedStringHandler.AppendLiteral("\n");
		defaultInterpolatedStringHandler.AppendLiteral("DOT: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.IsDot);
		defaultInterpolatedStringHandler.AppendLiteral("  Flag: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.Flag);
		defaultInterpolatedStringHandler.AppendLiteral("  Type: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.TypeInfo);
		defaultInterpolatedStringHandler.AppendLiteral("  Switch: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.SwitchVar);
		defaultInterpolatedStringHandler.AppendLiteral("\n");
		defaultInterpolatedStringHandler.AppendLiteral("특수: ");
		defaultInterpolatedStringHandler.AppendFormatted(entry.Specials);
		defaultInterpolatedStringHandler.AppendLiteral("\n");
		defaultInterpolatedStringHandler.AppendLiteral("패킷 크기: ");
		byte[]? rawPacket = entry.RawPacket;
		defaultInterpolatedStringHandler.AppendFormatted((rawPacket != null) ? rawPacket.Length : 0);
		defaultInterpolatedStringHandler.AppendLiteral(" bytes");
		string summary = defaultInterpolatedStringHandler.ToStringAndClear();
		PacketDetailsWindow packetDetailsWindow = new PacketDetailsWindow($"\ud83d\udd0d 패킷 상세 정보 — {entry.SkillName} ({entry.SkillCode})", summary, entry.RawPacket, entry.RawSkillCode, entry.RawDamage);
		packetDetailsWindow.Owner = this;
		packetDetailsWindow.Show();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/packetlogwindow.xaml", UriKind.Relative);
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
			((Border)target).MouseDown += Header_MouseDown;
			break;
		case 2:
			txtStatus = (TextBlock)target;
			break;
		case 3:
			((Button)target).Click += BtnClose_Click;
			break;
		case 4:
			btnPause = (Button)target;
			btnPause.Click += BtnPause_Click;
			break;
		case 5:
			((Button)target).Click += BtnClear_Click;
			break;
		case 6:
			((Button)target).Click += BtnShowFilters_Click;
			break;
		case 7:
			btnKindFilter = (ToggleButton)target;
			break;
		case 8:
			txtKindFilterSummary = (TextBlock)target;
			break;
		case 9:
			popKindFilter = (Popup)target;
			break;
		case 10:
			((Button)target).Click += BtnClearKindFilter_Click;
			break;
		case 11:
			itemsKindFilter = (ItemsControl)target;
			break;
		case 13:
			txtActorFilter = (TextBox)target;
			txtActorFilter.TextChanged += TxtActorFilter_TextChanged;
			break;
		case 14:
			txtCount = (TextBlock)target;
			break;
		case 15:
			chkAutoScroll = (CheckBox)target;
			break;
		case 16:
			dgLog = (DataGrid)target;
			dgLog.MouseDoubleClick += DgLog_MouseDoubleClick;
			break;
		case 17:
			txtInfo = (TextBlock)target;
			break;
		case 18:
			txtStats = (TextBlock)target;
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
		if (connectionId == 12)
		{
			((CheckBox)target).Checked += KindFilterCheckChanged;
			((CheckBox)target).Unchecked += KindFilterCheckChanged;
		}
	}
}
