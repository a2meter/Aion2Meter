using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Media3D;
using System.Windows.Resources;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Threading;
using INGMeter.App.Updates;
using INGMeter.Capture;
using INGMeter.Core;
using INGMeter.WpfUI;
using Microsoft.Win32;

namespace INGMeter.App;

public class MainWindow : Window, IComponentConnector, IStyleConnector
{
	private enum MainContentView
	{
		Lookup,
		Dps
	}

	private enum CloseButtonBehavior
	{
		Ask,
		MinimizeToTray,
		Exit
	}

	private enum TargetFilterItemKind
	{
		All,
		LiveBoss,
		ArchivedBoss,
		ClearHistory
	}

	private enum EncounterViewKind
	{
		LiveBoss,
		ArchivedBoss
	}

	private sealed class TargetFilterOption
	{
		public TargetFilterItemKind Kind { get; init; }

		public int TargetId { get; init; }

		public int ArchivedRecordId { get; init; }
	}

	private sealed class ArchivedBossRecord
	{
		public int ArchivedRecordId { get; init; }

		public int TargetId { get; init; }

		public int BossMobCode { get; init; }

		public string TargetName { get; init; } = "";

		public string DungeonText { get; init; } = "";

		public string LocalPlayerDpsText { get; init; } = "";

		public string SourceFullPath { get; init; } = "";

		public DateTime DisplayTimeLocal { get; init; }

		public CombatSnapshot Snapshot { get; init; }

		public Dictionary<int, UiActorState> UiActors { get; init; } = new Dictionary<int, UiActorState>();
	}

	private sealed class TargetFilterEntry
	{
		public TargetFilterOption Option { get; init; } = new TargetFilterOption();

		public string Label { get; init; } = "";
	}

	public enum LocalEncounterPanelRowKind
	{
		DateSeparator,
		Live,
		Archived,
		Stored
	}

	public sealed class LocalEncounterPanelRow : INotifyPropertyChanged
	{
		private bool _isReplayActive;

		private double _replayProgressRatio;

		public LocalEncounterPanelRowKind Kind { get; init; }

		public string Key { get; init; } = "";

		public int TargetId { get; init; }

		public int ArchivedRecordId { get; init; }

		public string FullPath { get; init; } = "";

		public DateTime? StartUtc { get; init; }

		public string TimeText { get; init; } = "";

		public string BossName { get; init; } = "";

		public string DungeonText { get; init; } = "";

		public string DurationText { get; init; } = "";

		public string TotalDamageText { get; init; } = "";

		public string ParticipantText { get; init; } = "";

		public string LocalPlayerDpsText { get; init; } = "";

		public string DateText { get; init; } = "";

		public bool IsReplayActive
		{
			get
			{
				return _isReplayActive;
			}
			set
			{
				if (_isReplayActive != value)
				{
					_isReplayActive = value;
					OnPropertyChanged("IsReplayActive");
					OnPropertyChanged("ReplayButtonToolTip");
					OnPropertyChanged("ReplayProgressVisibility");
				}
			}
		}

		public double ReplayProgressRatio
		{
			get
			{
				return _replayProgressRatio;
			}
			set
			{
				double num = ((double.IsNaN(value) || double.IsInfinity(value)) ? 0.0 : Math.Clamp(value, 0.0, 100.0));
				if (!(Math.Abs(_replayProgressRatio - num) < 0.05))
				{
					_replayProgressRatio = num;
					OnPropertyChanged("ReplayProgressRatio");
				}
			}
		}

		public bool IsDateSeparator => Kind == LocalEncounterPanelRowKind.DateSeparator;

		public bool IsEncounterRow => !IsDateSeparator;

		public bool CanReplay
		{
			get
			{
				if (IsEncounterRow && !string.IsNullOrWhiteSpace(FullPath))
				{
					return string.Equals(System.IO.Path.GetExtension(FullPath), ".inglog", StringComparison.OrdinalIgnoreCase);
				}
				return false;
			}
		}

		public string ReplayButtonToolTip
		{
			get
			{
				if (!IsReplayActive)
				{
					return "이 기록 리플레이";
				}
				return "리플레이 중지";
			}
		}

		public Visibility ReplayProgressVisibility
		{
			get
			{
				if (!IsReplayActive)
				{
					return Visibility.Collapsed;
				}
				return Visibility.Visible;
			}
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		private void OnPropertyChanged(string propertyName)
		{
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}

	public sealed class LocalEncounterBossSuggestion
	{
		public string BossName { get; init; } = "";

		public int Count { get; init; }

		public DateTime LatestStartUtc { get; init; }

		public string CountText => $"{Count:N0}회";
	}

	private struct POINT
	{
		public int X;

		public int Y;
	}

	private struct RECT
	{
		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
	private struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public int dwFlags;
	}

	private sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
	{
		private static readonly System.Drawing.Color MenuBackground = System.Drawing.Color.FromArgb(31, 31, 31);

		private static readonly System.Drawing.Color MenuHover = System.Drawing.Color.FromArgb(45, 45, 45);

		private static readonly System.Drawing.Color TextColor = System.Drawing.Color.FromArgb(245, 245, 245);

		private static readonly System.Drawing.Color BorderColor = System.Drawing.Color.FromArgb(70, 70, 70);

		private static readonly System.Drawing.Color SeparatorColor = System.Drawing.Color.FromArgb(82, 82, 82);

		public TrayMenuRenderer()
			: base(new TrayMenuColorTable())
		{
			base.RoundedEdges = true;
		}

		protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
		{
			using SolidBrush brush = new SolidBrush(MenuBackground);
			e.Graphics.FillRectangle(brush, new System.Drawing.Rectangle(System.Drawing.Point.Empty, e.ToolStrip.Size));
		}

		protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
		{
			ToolStripItem item = e.Item;
			using SolidBrush brush = new SolidBrush(item.Selected ? MenuHover : MenuBackground);
			int num = (item.Selected ? 3 : 2);
			int num2 = (IsLastVisibleMenuItem(item) ? 7 : num);
			System.Drawing.Rectangle bounds = new System.Drawing.Rectangle(8, num, item.Bounds.Width - 16, item.Bounds.Height - num - num2);
			e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath path = CreateRoundedRectanglePath(bounds, 6);
			e.Graphics.FillPath(brush, path);
		}

		private static bool IsLastVisibleMenuItem(ToolStripItem item)
		{
			if (item.Owner == null)
			{
				return false;
			}
			for (int num = item.Owner.Items.Count - 1; num >= 0; num--)
			{
				ToolStripItem toolStripItem = item.Owner.Items[num];
				if (toolStripItem.Visible && !(toolStripItem is ToolStripSeparator))
				{
					return toolStripItem == item;
				}
			}
			return false;
		}

		protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
		{
			System.Drawing.Rectangle rectangle = new System.Drawing.Rectangle(26, 0, e.Item.Width - 44, e.Item.Height);
			using SolidBrush brush = new SolidBrush(TextColor);
			using StringFormat format = new StringFormat
			{
				Alignment = StringAlignment.Near,
				LineAlignment = StringAlignment.Center,
				Trimming = StringTrimming.EllipsisCharacter,
				FormatFlags = StringFormatFlags.NoWrap
			};
			e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
			using Font font = new Font("Segoe UI", 9f, System.Drawing.FontStyle.Regular, GraphicsUnit.Point);
			Font font2 = e.TextFont ?? e.Item.Font ?? font;
			e.Graphics.DrawString(e.Text ?? "", font2, brush, rectangle, format);
		}

		protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
		{
			int num = e.Item.Height / 2;
			using System.Drawing.Pen pen = new System.Drawing.Pen(SeparatorColor, 1f);
			e.Graphics.DrawLine(pen, 14, num, e.Item.Width - 14, num);
		}

		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
		{
			if (e.ToolStrip is ContextMenuStrip)
			{
				using (System.Drawing.Pen pen = new System.Drawing.Pen(BorderColor, 1f))
				{
					using GraphicsPath path = CreateRoundedRectanglePath(new System.Drawing.Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 10);
					e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
					e.Graphics.DrawPath(pen, path);
					return;
				}
			}
			base.OnRenderToolStripBorder(e);
		}
	}

	private sealed class TrayMenuColorTable : ProfessionalColorTable
	{
		private static readonly System.Drawing.Color MenuBackground = System.Drawing.Color.FromArgb(31, 31, 31);

		private static readonly System.Drawing.Color MenuHover = System.Drawing.Color.FromArgb(45, 45, 45);

		private static readonly System.Drawing.Color MenuBorderColor = System.Drawing.Color.FromArgb(70, 70, 70);

		private static readonly System.Drawing.Color SeparatorColor = System.Drawing.Color.FromArgb(82, 82, 82);

		public override System.Drawing.Color ToolStripDropDownBackground => MenuBackground;

		public override System.Drawing.Color ImageMarginGradientBegin => MenuBackground;

		public override System.Drawing.Color ImageMarginGradientMiddle => MenuBackground;

		public override System.Drawing.Color ImageMarginGradientEnd => MenuBackground;

		public override System.Drawing.Color ToolStripBorder => MenuBorderColor;

		public override System.Drawing.Color MenuBorder => MenuBorderColor;

		public override System.Drawing.Color MenuItemBorder => MenuHover;

		public override System.Drawing.Color MenuItemSelected => MenuHover;

		public override System.Drawing.Color MenuItemSelectedGradientBegin => MenuHover;

		public override System.Drawing.Color MenuItemSelectedGradientEnd => MenuHover;

		public override System.Drawing.Color SeparatorDark => SeparatorColor;

		public override System.Drawing.Color SeparatorLight => SeparatorColor;
	}

	private static class BuffTimerBrushes
	{
		public static readonly System.Windows.Media.Brush ExpiredRing = Create(98, 106, 116);

		public static readonly System.Windows.Media.Brush ExpiredBadge = Create(230, 37, 43, 52);

		public static readonly System.Windows.Media.Brush CriticalRing = Create(96, 220, byte.MaxValue);

		public static readonly System.Windows.Media.Brush CriticalBadge = Create(235, 12, 43, 74);

		public static readonly System.Windows.Media.Brush WarningRing = Create(70, 166, byte.MaxValue);

		public static readonly System.Windows.Media.Brush WarningBadge = Create(235, 9, 39, 72);

		public static readonly System.Windows.Media.Brush NormalRing = Create(34, 175, byte.MaxValue);

		public static readonly System.Windows.Media.Brush NormalBadge = Create(235, 8, 33, 58);

		private static System.Windows.Media.Brush Create(byte r, byte g, byte b)
		{
			return Create(System.Windows.Media.Color.FromRgb(r, g, b));
		}

		private static System.Windows.Media.Brush Create(byte a, byte r, byte g, byte b)
		{
			return Create(System.Windows.Media.Color.FromArgb(a, r, g, b));
		}

		private static System.Windows.Media.Brush Create(System.Windows.Media.Color color)
		{
			SolidColorBrush solidColorBrush = new SolidColorBrush(color);
			solidColorBrush.Freeze();
			return solidColorBrush;
		}
	}

	private readonly struct DetailSkillTotals
	{
		public long TotalDamage { get; init; }

		public long TotalHealing { get; init; }

		public long SelfHealing { get; init; }

		public long OtherHealing { get; init; }

		public int HitCount { get; init; }

		public int HealCount { get; init; }

		public int CritCount { get; init; }

		public int NormalHitCount { get; init; }

		public int BackCount { get; init; }

		public int DoubleCount { get; init; }

		public int PerfectCount { get; init; }

		public int ParryCount { get; init; }

		public int EvadeCount { get; init; }

		public int SmiteCount { get; init; }

		public int MultiEventCount { get; init; }

		public int MinDamage { get; init; }

		public int MaxDamage { get; init; }

		public int MinHeal { get; init; }

		public int MaxHeal { get; init; }

		public int BestCode { get; init; }

		public int[] SkillCodes { get; init; }
	}

	private IPacketCaptureService _cap = CreateCaptureService(CaptureBackend.WinDivert);

	private readonly MeterEngine _engine = new MeterEngine();

	private readonly DiagnosticPacketCaptureManager _diagnosticPacketCapture = new DiagnosticPacketCaptureManager();

	private static readonly CultureInfo KoreanCulture = CultureInfo.GetCultureInfo("ko-KR");

	private const string ApiKey = "ing_meter_secret_2026";

	private volatile bool _isPaused;

	private DateTime? _pausedNowUtc;

	private bool _isLogViewMode;

	private bool _isExitRequested;

	private NotifyIcon? _trayIcon;

	private bool _detailWindowHiddenToTray;

	private static readonly Geometry PauseIconGeometry = Geometry.Parse("M4,2 L4,14 M10,2 L10,14");

	private static readonly Geometry PlayIconGeometry = Geometry.Parse("M4,2.5 L13,8 L4,13.5 Z");

	private static readonly Geometry ResetIconGeometry = Geometry.Parse("M13.4,5.5 A5.6,5.6 0 1 0 14,9 M13.4,5.5 L13.4,2.4 M13.4,5.5 L10.2,5.5");

	private static readonly Geometry StopIconGeometry = Geometry.Parse("M4,4 L12,4 L12,12 L4,12 Z");

	private static readonly Geometry MaximizeIconGeometry = Geometry.Parse("M2,2 L14,2 L14,14 L2,14 Z");

	private static readonly Geometry RestoreIconGeometry = Geometry.Parse("M4,6 L4,14 L12,14 L12,6 Z M6,4 L14,4 L14,12");

	private readonly object _sync = new object();

	private readonly Dictionary<int, UiActorState> _uiActors = new Dictionary<int, UiActorState>();

	private readonly Queue<UiBuffEvent> _allBuffEvents = new Queue<UiBuffEvent>();

	private readonly Dictionary<UiBuffStateKey, UiBuffEvent> _activeBuffEvents = new Dictionary<UiBuffStateKey, UiBuffEvent>();

	private readonly HashSet<int> _pendingUiTargetResets = new HashSet<int>();

	private readonly SkillNameMap _skillNames = new SkillNameMap();

	private readonly BuffNameMap _buffNames = new BuffNameMap();

	private RdpsSkillCatalog _rdpsSkillCatalog = RdpsSkillCatalog.Empty;

	private RdpsPartyBuffCatalog _rdpsPartyBuffCatalog = RdpsPartyBuffCatalog.Empty;

	private readonly Dictionary<int, string> _skillIconPathCache = new Dictionary<int, string>();

	private readonly Dictionary<string, IReadOnlyList<TraitSlot>> _traitSlotCache = new Dictionary<string, IReadOnlyList<TraitSlot>>(StringComparer.Ordinal);

	private static readonly Regex _nameValidRegex = new Regex("^[a-zA-Z0-9가-힣ㄱ-ㅎㅏ-ㅣ\\s_\\[\\]]+$", RegexOptions.Compiled);

	private int? _selectedActorId;

	private bool _detailRenderQueued;

	private int? _queuedDetailActorId;

	private const int DetailRenderTickInterval = 8;

	private const double DetailRdpsRefreshIntervalSeconds = 5.0;

	private const int DetailLogMaxRows = 800;

	private const int DetailDpsGraphMaxPoints = 240;

	private long _lastDetailRdpsRefreshTick;

	private int _lastDetailRdpsActorId;

	private int _lastDetailRdpsTargetId = int.MinValue;

	private string _lastAutoDetailRenderSignature = "";

	private DispatcherTimer _timer;

	private static readonly HttpClient _partyHttp = new HttpClient
	{
		Timeout = TimeSpan.FromSeconds(5L)
	};

	private long _lastCheckedPackets;

	private int _idleCounter;

	private int _tickCount;

	private string _captureStatusMessage = "Capture not started";

	private string? _captureStartFailureMessage;

	private string? _pendingCaptureBackendFallbackMessage;

	private bool _captureStartAttempted;

	private long _parsedDamageEvents;

	private long _parsedBuffEvents;

	private long _lastSnapshotDamageEvents = -1L;

	private System.Windows.Media.Color? _lastStatusColor;

	private string _lastStatusTooltip = "";

	private MobNameMap _mobNameMap = new MobNameMap();

	private readonly DungeonContentMap _dungeonContentMap = new DungeonContentMap();

	private readonly DungeonBossCatalogMap _dungeonBossCatalogMap = new DungeonBossCatalogMap();

	private DungeonContentInfo? _currentDungeonContent;

	private CombatDetailWindow? _detailWindow;

	private bool _partyOpen;

	private MainContentView _mainContentView = MainContentView.Dps;

	private MainContentView? _lastAppliedMainContentView;

	private int _lastAnimatedTopTargetId;

	private bool _isMainViewAutoMode = true;

	private DateTime _lastManualMainViewUtc = DateTime.MinValue;

	private const int ManualMainViewHoldSeconds = 12;

	private const double FixedCompactWidth = 382.0;

	private const double MinCompactWidth = 315.0;

	private const double MinHudWidth = 315.0;

	private const double MaxCompactWidth = 1400.0;

	private double _compactWidth = 382.0;

	private double _fullWidth = 1242.0;

	private double _partyWidth = 677.0;

	private double _normalHeight = 287.0;

	private double? _savedWindowLeft;

	private double? _savedWindowTop;

	private double _lastCompactDpsWidth;

	private bool _isDragging;

	private FrameworkElement? _windowDragHandleSource;

	private System.Windows.Point _windowDragHandleStart;

	private const int DefaultMaxDpsCards = 10;

	private const int MaxAutomaticAverageDpsCards = 10;

	private const int MaxAverageDpsRequestConcurrency = 2;

	private const int MaxTemporaryLookupItems = 5;

	private const int MaxVisibleStigmaBadges = 5;

	private const int AverageDpsContentWaitMs = 900;

	private static readonly TimeSpan TemporaryLookupLifetime = TimeSpan.FromSeconds(15L);

	private static readonly TimeSpan MeterPresenceHeartbeatInterval = TimeSpan.FromMinutes(5L);

	private static readonly TimeSpan MeterPresenceFreshness = TimeSpan.FromMinutes(10L);

	private const int ExtendedUserInfoSourceGroup = 1;

	private const int ExtendedUserInfoSourceInspect = 3;

	private const int ExtendedUserInfoSourceApplicant = 4;

	private const int ExtendedUserInfoSourceForce = 5;

	private readonly Dictionary<int, string> _pendingPartyRosterSnapshot = new Dictionary<int, string>();

	private int _pendingPartyRosterExpectedCount;

	private int _maxDpsCards = 10;

	private bool _maxDpsCardsForce10Applied;

	private bool _saveConfigAfterLoad;

	private bool _showActorId;

	private bool _useDummyData;

	private bool _autoBossFilter = true;

	private bool _bossOnlyMeasurement = true;

	private MeterDisplayPreset _displayPreset;

	private double _uiScale = 0.96;

	private double _textScale = 1.1;

	private MeterFontWeightMode _fontWeightMode = MeterFontWeightMode.Normal;

	private string _fontFamilyName = "Malgun Gothic";

	private bool _textShadowEnabled = true;

	private DamageShareMode _damageShareMode = DamageShareMode.BossHpPercent;

	private DamageShareGraphMode _damageShareGraphMode;

	private DpsCardNumberFormatMode _dpsCardNumberFormatMode;

	private bool _autoResetOnMapChange;

	private bool _saveEncounterLogs = true;

	private string _devKey = "";

	private int _autoBossTargetId;

	private int _activeBossTargetId;

	private int _selectedArchivedBossRecordId;

	private EncounterViewKind _encounterViewKind;

	private int _lastMapId = -1;

	private string? _selectedDetailCharacterKey;

	private string? _selectedDetailTitle;

	private int? _lastDoubleClickedActorId;

	private string? _lastDoubleClickedCharacterKey;

	private int? _contextMenuActorId;

	private readonly List<ArchivedBossRecord> _archivedBossRecords = new List<ArchivedBossRecord>();

	private int _nextArchivedBossRecordId = 1;

	private TargetFilterOption _selectedTargetFilterOption = new TargetFilterOption
	{
		Kind = TargetFilterItemKind.All
	};

	private bool _isUpdatingTargetCombo;

	private readonly List<EncounterHistoryRow> _cachedLocalEncounterHistoryRows = new List<EncounterHistoryRow>();

	private int _localEncounterHistoryLoadVersion;

	private bool _isUpdatingLocalEncounterSelection;

	private bool _isLocalEncounterHistoryLoading;

	private System.Windows.Controls.TextBox? _localEncounterBossSearchTextBox;

	private bool _localEncounterBossSuggestionsOpen;

	private bool _isApplyingLocalEncounterBossSuggestion;

	private DateTime _lastLocalEncounterManualScrollUtc = DateTime.MinValue;

	private Window? _localEncounterHistoryWindow;

	private bool _isClosingLocalEncounterHistoryWindow;

	private bool _localEncounterHistoryHiddenToTray;

	private const double LocalEncounterHistoryPanelGap = 6.0;

	private const double LocalEncounterHistoryMinWidth = 300.0;

	private const double LocalEncounterHistoryDefaultWidth = 374.0;

	private const double LocalEncounterHistoryMinHeight = 210.0;

	private const double LocalEncounterHistoryDefaultHeight = 390.0;

	private const double LocalEncounterHistoryMaxHeight = 720.0;

	private BuffTimerWindow? _buffTimerWindow;

	private bool _buffTimerEnabled;

	private bool _isClosingBuffTimerWindow;

	private bool _buffTimerHiddenToTray;

	private double? _buffTimerLeft;

	private double? _buffTimerTop;

	private double? _buffTimerWidth;

	private double? _buffTimerHeight;

	private readonly HashSet<int> _hiddenBuffTimerKeys = new HashSet<int>();

	private readonly Dictionary<int, int> _buffTimerSlotOrders = new Dictionary<int, int>();

	private int _nextBuffTimerSlotOrder;

	private DispatcherTimer? _buffTimerPlacementSaveTimer;

	private DateTime _lastBuffTimerRefreshUtc = DateTime.MinValue;

	private static readonly TimeSpan BuffTimerRefreshInterval = TimeSpan.FromMilliseconds(200L);

	private static readonly TimeSpan BuffTimerExpiredHold = TimeSpan.FromSeconds(2L);

	private const double BuffTimerDefaultWidth = 204.0;

	private const double BuffTimerDefaultHeight = 110.0;

	private readonly object _localEncounterLogLoadLock = new object();

	private int _localEncounterLogLoadVersion;

	private CancellationTokenSource? _localEncounterLogSelectionCts;

	private CancellationTokenSource? _encounterReplayCts;

	private bool _isEncounterReplayActive;

	private string _activeEncounterReplayPath = "";

	private double _encounterReplayProgressRatio;

	private int _lastEncounterReplayRenderedEvents = -1;

	private static readonly TimeSpan DpsRankReorderInterval = TimeSpan.FromMilliseconds(500L);

	private DateTime _lastDpsRankReorderUtc = DateTime.MinValue;

	private string _configPath = AppPaths.ConfigFilePath;

	private double _meterLayoutScale = 1.0;

	private double _meterUiScale = 1.0;

	private double _meterTextScale = 1.0;

	private int _meterFontSizeDelta;

	private string _combatTimeText = "전투 시간: 00:00.0";

	private string _combatTimeBadgeText = "00:00";

	private double _bossHpRatio;

	private bool _showBossCard = true;

	private bool _showDpsCardCombatTime;

	private bool _autoHideBackground;

	private bool _isMainBackgroundHovered;

	private bool _isMainResizeBorderHovered;

	private bool _showOnlyWhenAionActive;

	private bool _showInTaskbar = true;

	private CloseButtonBehavior _closeButtonBehavior;

	private CaptureBackend _captureBackend;

	private bool _captureFailureDialogShown;

	private bool _hiddenForAionInactive;

	private DateTime _lastPresenceHeartbeatUtc = DateTime.MinValue;

	private string? _lastPresenceHeartbeatKey;

	private int _locatePulseVersion;

	private string _pauseHotkey = "None";

	private string _clearHotkey = "Ctrl+R";

	private string _hudHotkey = "None";

	private string _hideHotkey = "None";

	private string _clickThroughHotkey = "None";

	private string _mainViewHotkey = "None";

	private readonly HashSet<int> _latchedHotkeyIds = new HashSet<int>();

	private bool _isSettingsOpen;

	private SettingsWindow? _settingsWindow;

	private bool _autoResetOnNewBoss = true;

	private int _lastAutoResetBossTargetId;

	private string? _lastAnnouncedLocalPlayerIdentityKey;

	private readonly HashSet<string> _characterConsentCheckedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly HashSet<string> _characterConsentCheckingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private readonly object _characterConsentStatesLock = new object();

	private readonly Dictionary<string, CharacterConsentState> _characterConsentStates = new Dictionary<string, CharacterConsentState>(StringComparer.OrdinalIgnoreCase);

	private int _lastAnnouncedZoneEntryContentCode;

	private DateTime _lastAnnouncedZoneEntryAtUtc = DateTime.MinValue;

	private DispatcherTimer? _updateCheckTimer;

	private string? _latestAvailableVersion;

	private bool _updateNotificationShown;

	private readonly AppUpdateService _updateService = new AppUpdateService();

	private const string Aion2ToolApiUrl = "https://www.aion2tool.com/api/character/search";

	private readonly Dictionary<string, int> _combatScoreCache = new Dictionary<string, int>();

	private readonly Dictionary<string, bool> _combatScoreDungeonScopeCache = new Dictionary<string, bool>(StringComparer.Ordinal);

	private readonly Dictionary<string, int> _packetCombatPowerCache = new Dictionary<string, int>(StringComparer.Ordinal);

	private readonly Dictionary<string, DateTime> _meterPresenceCacheUtc = new Dictionary<string, DateTime>(StringComparer.Ordinal);

	private readonly Dictionary<string, string> _officialStigmaCache = new Dictionary<string, string>(StringComparer.Ordinal);

	private readonly HashSet<string> _officialStigmaLoading = new HashSet<string>(StringComparer.Ordinal);

	private LookupSkillCatalog _lookupSkillCatalog = LookupSkillCatalog.Empty;

	private bool _lookupSkillDisplayEnabled = true;

	private Dictionary<string, HashSet<int>> _lookupSkillSelections = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

	private HashSet<string> _lookupSkillDisabledClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private int _lookupSkillSelectionVersion;

	private readonly HashSet<string> _combatScoreLoading = new HashSet<string>();

	private readonly HashSet<PartyMemberItem> _lookupRemovalAnimations = new HashSet<PartyMemberItem>();

	private readonly HashSet<string> _combatScoreAutoRequestedThisSession = new HashSet<string>();

	private readonly SemaphoreSlim _combatScoreRequestGate = new SemaphoreSlim(2, 2);

	private readonly SemaphoreSlim _officialApiRequestGate = new SemaphoreSlim(2, 2);

	private int _averageDpsRefreshEpoch;

	private static readonly Dictionary<string, int> ServerIdMap = new Dictionary<string, int>
	{
		{ "시엘", 1001 },
		{ "이스라펠", 1002 },
		{ "트리니엘", 1007 },
		{ "루미엘", 1008 },
		{ "바이젤", 1005 },
		{ "지켈", 1006 },
		{ "네자칸", 1003 },
		{ "카이시넬", 1004 }
	};

	private static readonly string[] _anonAdjectives = new string[30]
	{
		"용맹한", "강력한", "신비로운", "배고픈", "졸린", "귀여운", "날카로운", "단단한", "빛나는", "어두운",
		"빠른", "느린", "거대한", "작은", "차가운", "뜨거운", "영리한", "어리석은", "화난", "즐거운",
		"수줍은", "대담한", "고요한", "활기찬", "우아한", "거친", "부드러운", "투명한", "묵직한", "날랜"
	};

	private static readonly string[] _anonNouns = new string[30]
	{
		"사자", "호랑이", "독수리", "곰", "여우", "늑대", "토끼", "다람쥐", "펭귄", "고양이",
		"강아지", "너구리", "판다", "기린", "코끼리", "하마", "악어", "원숭이", "사슴", "부엉이",
		"까마귀", "매", "돌고래", "상어", "고래", "거북이", "뱀", "개구리", "햄스터", "고슴도치"
	};

	private const int GWL_EXSTYLE = -20;

	private const int WS_EX_TRANSPARENT = 32;

	private const int WS_EX_TOOLWINDOW = 128;

	private const int WS_EX_APPWINDOW = 262144;

	private static readonly nint HWND_TOPMOST = new IntPtr(-1);

	private static readonly nint HWND_NOTOPMOST = new IntPtr(-2);

	private const uint SWP_NOSIZE = 1u;

	private const uint SWP_NOMOVE = 2u;

	private const uint SWP_NOZORDER = 4u;

	private const uint SWP_NOACTIVATE = 16u;

	private const uint SWP_FRAMECHANGED = 32u;

	private double _dragOffsetX;

	private double _dragOffsetY;

	private bool _isHudMode = true;

	private bool _isApplyingHudLayout;

	private bool _hudClickThrough;

	private DispatcherTimer? _hudClickThroughTimer;

	private bool _isWindowMouseTransparent;

	private bool _isBuffTimerWindowMouseTransparent;

	private AppearanceSelection _appearance = AppearanceSelection.Default;

	private MeterSkinProfile _skinProfile = AppearanceCatalog.GetSkinProfile(MeterSkin.Default);

	private double _windowOpacity = 1.0;

	private double _hudOpacity = 0.9;

	private bool _preHudTopmost = true;

	private double _preHudWidth = 1192.0;

	private double _preHudHeight = 600.0;

	private double _hudHeight = 350.0;

	private double _hudWidth = 315.0;

	private static readonly Thickness NormalMainGridMargin = new Thickness(0.0, 0.0, 0.0, 4.0);

	private static readonly Thickness NormalTopTargetMargin = new Thickness(8.0, 4.0, 8.0, 4.0);

	private static readonly Thickness NormalTopTargetPadding = new Thickness(12.0, 2.0, 12.0, 2.0);

	private static readonly Thickness HudTopTargetMargin = new Thickness(6.0, 7.0, 6.0, 0.0);

	private static readonly Thickness HudTopTargetPadding = new Thickness(8.0, 4.0, 8.0, 4.0);

	private static readonly Thickness NormalDpsListMargin = new Thickness(8.0, 0.0, 8.0, 0.0);

	private static readonly Thickness HudDpsListMargin = new Thickness(6.0, 2.0, 6.0, 0.0);

	private static readonly Thickness NormalDpsListPadding = new Thickness(0.0, 2.0, 0.0, 2.0);

	private static readonly Thickness MinimalDpsListPadding = new Thickness(0.0, 2.0, 0.0, 2.0);

	private const double NormalContentHorizontalInset = 8.0;

	private const double HudContentHorizontalInset = 6.0;

	private const int HOTKEY_ID_PAUSE = 9000;

	private const int HOTKEY_ID_CLEAR = 9001;

	private const int HOTKEY_ID_HUD = 9002;

	private const int HOTKEY_ID_HIDE = 9003;

	private const int HOTKEY_ID_CLICK_THROUGH = 9004;

	private const int HOTKEY_ID_MAIN_VIEW = 9005;

	internal Border rootBorder;

	internal Border titleBar;

	internal Border titleBarSeparator;

	internal Grid toolBar;

	internal ColumnDefinition colDpsToolbar;

	internal ColumnDefinition colSplitterToolbar;

	internal ColumnDefinition colDetailToolbar;

	internal StackPanel headerLeftControls;

	internal StackPanel statusHeaderHitArea;

	internal Border statusHeaderIconHost;

	internal System.Windows.Controls.Image imgStatusHeaderIcon;

	internal Ellipse elStatusHeader;

	internal Border bdMainViewModeHost;

	internal System.Windows.Shapes.Rectangle mainViewAutoFrame;

	internal System.Windows.Controls.CheckBox chkAutoMainView;

	internal Border mainViewModeDivider;

	internal System.Windows.Controls.Button btnMainViewSwap;

	internal System.Windows.Controls.Button btnUpdateBadge;

	internal DropShadowEffect updateBadgeGlow;

	internal System.Windows.Shapes.Path pathUpdateArrow;

	internal System.Windows.Shapes.Path pathUpdateTray;

	internal TextBlock txtUpdateProgress;

	internal StackPanel headerActionControls;

	internal System.Windows.Controls.Button btnPause;

	internal System.Windows.Shapes.Path pathPauseIcon;

	internal System.Windows.Controls.Button btnPrimaryAction;

	internal System.Windows.Shapes.Path pathPrimaryActionIcon;

	internal ToggleButton chkHudMode;

	internal System.Windows.Controls.Button btnStatusSettings;

	internal System.Windows.Shapes.Path pathSettingsMenuGear;

	internal System.Windows.Controls.Button btnTopmost;

	internal StackPanel headerWindowControls;

	internal System.Windows.Controls.Button btnMaximize;

	internal System.Windows.Shapes.Path pathMaximizeIcon;

	internal System.Windows.Controls.Button btnLocalEncounterHistory;

	internal System.Windows.Controls.Button btnClose;

	internal Grid mainGrid;

	internal ColumnDefinition colDps;

	internal ColumnDefinition colSplitter;

	internal ColumnDefinition colDetail;

	internal Grid sideMenu;

	internal System.Windows.Controls.Button btnDetail;

	internal System.Windows.Controls.Button btnParty;

	internal ToggleButton chkShowUnknown;

	internal ToggleButton chkHideNickname;

	internal System.Windows.Controls.Button btnLoadLog;

	internal System.Windows.Controls.Button btnHome;

	internal System.Windows.Controls.Button btnSettingsSide;

	internal Ellipse elStatus;

	internal Popup popUpload;

	internal Border bdUploadPopup;

	internal Popup popApplicant;

	internal StackPanel stackApplicants;

	internal Border mainContentBorder;

	internal BloomDpsCardFrame bloomWindowFrame;

	internal CrayonPaperBackdrop crayonBackdrop;

	internal CrayonDoodleFrame crayonWindowFrame;

	internal Grid filterPanel;

	internal System.Windows.Controls.ComboBox cmbFilterClass;

	internal System.Windows.Controls.ComboBox cmbFilterTarget;

	internal Border bdLookupDungeonInfo;

	internal TextBlock txtLookupDungeonCategory;

	internal Border bdLookupDungeonDetail;

	internal TextBlock txtLookupDungeonDetail;

	internal TextBlock txtLookupDungeonName;

	internal Border borderTopTarget;

	internal ScaleTransform topTargetScale;

	internal Grid topTargetRootGrid;

	internal RowDefinition topTargetContentRow;

	internal RowDefinition topTargetHpRow;

	internal ColumnDefinition colTargetIcon;

	internal BloomDpsCardFrame bossBloomFrame;

	internal CrayonDoodleFrame bossCrayonFrame;

	internal Canvas bossNeonFrameHost;

	internal NeonDpsCardFrame bossNeonFrame;

	internal AbyssDpsCardFrame bossAbyssFrame;

	internal Border bdTargetIcon;

	internal Grid topTargetInfoGrid;

	internal RowDefinition topTargetNameTextRow;

	internal RowDefinition topTargetHpTextRow;

	internal RowDefinition topTargetNeonHpRow;

	internal Grid topTargetNameLineGrid;

	internal TextBlock txtTopTargetName;

	internal StackPanel topTargetHpSummaryStack;

	internal TextBlock txtTopTargetHpValue;

	internal TextBlock txtTopTargetHpPercent;

	internal TextBlock txtTopTargetDuration;

	internal StackPanel topTargetInlineMetricsStack;

	internal TextBlock txtTopTargetInlineDamage;

	internal TextBlock txtTopTargetInlineDuration;

	internal TextBlock txtTopTargetType;

	internal StackPanel topTargetDamageStack;

	internal TextBlock txtTopTargetDamageLabel;

	internal TextBlock txtTopTargetDamage;

	internal TextBlock txtTopTargetHits;

	internal NeonBossHpBar neonBossHpBar;

	internal Border bossHpTrack;

	internal Border bossHpFill;

	internal System.Windows.Controls.ListBox lstDps;

	internal System.Windows.Controls.ListBox lstLookup;

	internal TextBlock txtLookupEmpty;

	internal StackPanel hudLeftControls;

	internal Grid hudBrandIcon;

	internal System.Windows.Controls.Button btnUpdateBadgeHud;

	internal DropShadowEffect updateBadgeGlowHud;

	internal System.Windows.Shapes.Path pathUpdateArrowHud;

	internal System.Windows.Shapes.Path pathUpdateTrayHud;

	internal TextBlock txtUpdateProgressHud;

	internal Border bdHudMainViewModeHost;

	internal System.Windows.Shapes.Rectangle hudMainViewAutoFrame;

	internal System.Windows.Controls.CheckBox chkAutoMainViewHud;

	internal System.Windows.Controls.Button btnMainViewSwapHud;

	internal StackPanel hudControls;

	internal System.Windows.Controls.Button btnResetHud;

	internal System.Windows.Shapes.Path pathResetHudIcon;

	internal System.Windows.Controls.Button btnExitHud;

	internal System.Windows.Controls.Button btnSettingsHud;

	internal System.Windows.Shapes.Path pathSettingsHudGear;

	internal System.Windows.Controls.Button btnLocalEncounterHistoryHud;

	internal System.Windows.Controls.Button btnClickThroughHud;

	internal System.Windows.Shapes.Path pathHudClickThroughIcon;

	internal System.Windows.Controls.Button btnExitAppHud;

	internal Popup popLocalEncounterHistory;

	internal Border bdLocalEncounterHistoryPanel;

	internal TextBlock txtLocalEncounterCount;

	internal Border bdLocalEncounterBossSearch;

	internal System.Windows.Controls.TextBox txtLocalEncounterBossSearchInput;

	internal TextBlock txtLocalEncounterBossSearchPlaceholder;

	internal System.Windows.Controls.Button btnLocalEncounterBossSearchClear;

	internal System.Windows.Controls.ListBox lstLocalEncounterBossSuggestions;

	internal System.Windows.Controls.ListBox lstLocalEncounterHistory;

	internal TextBlock txtLocalEncounterEmpty;

	internal Thumb thumbLocalEncounterHistoryResize;

	internal Border borderParty;

	internal System.Windows.Controls.ListBox lstParty;

	internal Border locatePulseOverlay;

	private bool _contentLoaded;

	public ObservableCollection<DpsCardViewModel> DpsCards { get; set; } = new ObservableCollection<DpsCardViewModel>();

	public ObservableCollection<PartyMemberItem> PartyMembers { get; set; } = new ObservableCollection<PartyMemberItem>();

	public ObservableCollection<LocalEncounterPanelRow> LocalEncounterRows { get; } = new ObservableCollection<LocalEncounterPanelRow>();

	public ObservableCollection<LocalEncounterBossSuggestion> LocalEncounterBossSuggestions { get; } = new ObservableCollection<LocalEncounterBossSuggestion>();

	private bool IsBloomTheme => _skinProfile.UsesBloomLayoutFamily;

	private bool IsAbyssTheme => _skinProfile.IsAbyss;

	private bool IsAetherVeilTheme => _skinProfile.IsAetherVeil;

	private bool IsDefaultSkin => _appearance.Skin == MeterSkin.Default;

	private bool IsSoftDecorativeTheme => _skinProfile.UsesSoftDecoration;

	private bool IsNeonTheme => _skinProfile.UsesNeonDecoration;

	private double ContentHorizontalInset
	{
		get
		{
			if (!_isHudMode)
			{
				return 8.0;
			}
			return 6.0;
		}
	}

	private Thickness DpsListMargin
	{
		get
		{
			Thickness margin = ((!_isHudMode) ? NormalDpsListMargin : (IsBloomTheme ? new Thickness(0.0, 2.0, 0.0, 0.0) : HudDpsListMargin));
			return AlignContentHorizontalInset(margin);
		}
	}

	private string CurrentThemeName => _appearance.ResourceThemeName;

	private string GetAnonymousName(string originalName, int actorId)
	{
		if (string.IsNullOrWhiteSpace(originalName) || originalName.StartsWith("Actor "))
		{
			return originalName;
		}
		int num = Math.Abs((originalName + actorId).GetHashCode());
		string obj = _anonAdjectives[num % _anonAdjectives.Length];
		string text = _anonNouns[num / _anonAdjectives.Length % _anonNouns.Length];
		return obj + " " + text;
	}

	private static IPacketCaptureService CreateCaptureService(CaptureBackend backend)
	{
		if (backend != CaptureBackend.NpcapMirror)
		{
			return new WinDivertCaptureService();
		}
		return new NpcapMirrorCaptureService();
	}

	private void AttachCaptureService(IPacketCaptureService capture)
	{
		capture.StatusChanged += OnCaptureStatusChanged;
		capture.TcpPayloadReceived += OnTcpPayloadReceived;
	}

	private void DetachCaptureService(IPacketCaptureService capture)
	{
		capture.StatusChanged -= OnCaptureStatusChanged;
		capture.TcpPayloadReceived -= OnTcpPayloadReceived;
	}

	private void OnCaptureStatusChanged(string message)
	{
		_captureStatusMessage = message;
		_captureStartFailureMessage = null;
		if (!IsRoutineCaptureStatus(message))
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				ShowSystemBalloon(message);
			});
		}
	}

	private void OnTcpPayloadReceived(TcpPayload packet)
	{
		if (_isPaused)
		{
			return;
		}
		try
		{
			_diagnosticPacketCapture.Observe(packet);
			_engine.OnTcpPayload(packet.NormalizedSrcPort, packet.NormalizedDstPort, packet.Payload, packet.TimestampUtc, packet.SeqNum, packet.IsPsh);
		}
		catch
		{
		}
	}

	public MainWindow()
	{
		InitializeComponent();
		rootBorder.MouseEnter += delegate
		{
			SetMainBackgroundHovered(hovered: true);
		};
		rootBorder.MouseLeave += delegate
		{
			SetMainBackgroundHovered(hovered: false);
		};
		InitializeLocalEncounterBossSearchBox();
		_updateService.State.PropertyChanged += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(UpdateUpdateButtonVisual));
		};
		_engine.MapChangeAutoReset = _autoResetOnMapChange;
		LoadConfig();
		ApplyDeveloperWebEndpoint();
		ValidateConfiguredCaptureBackend();
		ApplyFontFamily();
		ApplyFontWeightMode();
		ApplyTextShadowPreference();
		_cap.Dispose();
		_cap = CreateCaptureService(_captureBackend);
		ApplySavedWindowPlacement();
		if (_saveConfigAfterLoad)
		{
			_saveConfigAfterLoad = false;
			SaveConfig();
		}
		ApplyShowInTaskbarPreference();
		ApplyWindowOpacity();
		ApplyBossCenteredRuntimePolicy();
		UpdatePauseButtonUI();
		UpdateLoadLogButtonUI();
		lstDps.ItemsSource = DpsCards;
		lstParty.ItemsSource = PartyMembers;
		lstLookup.ItemsSource = PartyMembers;
		lstLocalEncounterHistory.ItemsSource = LocalEncounterRows;
		lstLocalEncounterBossSuggestions.ItemsSource = LocalEncounterBossSuggestions;
		ApplyDisplayPresetVisualState(forceScale: true);
		PartyMembers.CollectionChanged += delegate
		{
			UpdateLookupEmptyState();
		};
		AttachCaptureService(_cap);
		LoadSkillNames();
		LoadMobNames();
		LoadBuffNames();
		_lookupSkillCatalog = LookupSkillCatalog.LoadFromEmbeddedResource();
		_rdpsSkillCatalog = RdpsSkillCatalog.Shared;
		_rdpsPartyBuffCatalog = RdpsPartyBuffCatalog.Shared;
		_engine.ResolveSkillName = (int code) => _skillNames.GetNameOrCode(code);
		_engine.ContainsSkillCode = (int code) => _skillNames.HasKnownIdOrBase(code);
		_engine.ResolveMobName = (int code) => _mobNameMap.GetName(code);
		_engine.ResolveMobBossStatus = (int code) => _mobNameMap.IsBoss(code);
		base.Loaded += delegate
		{
			_fullWidth = 1250.0;
			if (_isHudMode)
			{
				ApplyHudModeSelection();
			}
			else
			{
				ApplyCompactLayoutColumns();
				ApplyCompactWindowBounds();
				base.Height = _normalHeight;
			}
			btnDetail.ToolTip = "상세정보 열기";
			btnDetail.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SideMenuIconInactiveBrush");
			HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
			ApplyHotkeys();
			ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;
			base.SizeChanged += MainWindow_SizeChanged;
			base.LocationChanged += delegate
			{
				RepositionLocalEncounterHistoryPopup();
			};
			StartAutoUpdateCheck();
			base.Dispatcher.BeginInvoke(new Action(CaptureCompactDpsWidth), DispatcherPriority.Loaded);
			base.Dispatcher.BeginInvoke(new Action(UpdateBalloonPlacement), DispatcherPriority.Loaded);
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				ApplyDisplayPresetVisualState(forceScale: true);
			}, DispatcherPriority.Loaded);
			base.Dispatcher.BeginInvoke(new Action(ApplyMainContentView), DispatcherPriority.Loaded);
			if (_buffTimerEnabled)
			{
				base.Dispatcher.BeginInvoke(new Action(OpenBuffTimerWindow), DispatcherPriority.Loaded);
			}
		};
		base.Loaded += Window_Loaded;
		_engine.DamageEventParsed += OnDamageEventParsed;
		_engine.BuffEventParsed += OnBuffEventParsed;
		_engine.StigmaSkillLevelReceived += OnStigmaSkillLevelReceived;
		_engine.MobSpawnObserved += _diagnosticPacketCapture.OnMobSpawn;
		_engine.LocalUserInfoObserved += _diagnosticPacketCapture.OnLocalUserInfo;
		_engine.UserInfoResolved += delegate(int actorId)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnUserInfoResolved(actorId);
			});
		};
		_engine.ExtendedUserInfoReceived += delegate(ExtendedUserInfoEvent info)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnExtendedUserInfoReceived(info);
			});
		};
		_engine.ZoneEntryReceived += delegate(ZoneEntryEvent entry)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnZoneEntryReceived(entry);
			});
		};
		base.PreviewKeyDown += MainWindow_PreviewKeyDown;
		_timer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromMilliseconds(250L)
		};
		_timer.Tick += Timer_Tick;
		_timer.Start();
		_engine.AutoReset += delegate
		{
			List<ArchivedBossRecord> archived = CaptureArchivedBossRecords();
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				_lastAutoResetBossTargetId = 0;
				ResetUiForAutoReset(archived);
			});
		};
		_engine.LocalPlayerIdentified += delegate(string name, int actorId, int serverId)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				AnnounceLocalPlayerIfNeeded(name, serverId);
				TryQueuePresenceHeartbeat();
				RefreshVisibleMeterPresence();
				EnsureLocalCharacterConsentAsync(name, actorId, serverId);
			});
		};
		_engine.BossConfirmed += delegate(int targetId, string targetName)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				TryAutoShowDps(null, force: true);
				bool num = _engine.IsUploadSuppressedTarget(targetId);
				TargetInfo dominantBoss;
				bool flag = TryGetDominantActiveBoss(targetId, out dominantBoss);
				bool flag2 = _engine.HasOtherConfirmedBossTargetWithDamage(targetId);
				if (!num && _autoResetOnNewBoss && flag2 && !flag && targetId != _lastAutoResetBossTargetId)
				{
					_lastAutoResetBossTargetId = targetId;
					ResetCurrentSession(archiveCurrentBosses: true, clearArchivedHistory: false, startNewLog: true, preferLatestArchivedSelection: false, preserveDpsSelection: true);
					ShowSystemBalloon("새 보스 감지: " + targetName + " - DPS를 자동 초기화했습니다.");
				}
				int targetId2 = (flag ? dominantBoss.TargetId : targetId);
				if (_encounterViewKind == EncounterViewKind.ArchivedBoss)
				{
					PopulateTargetCombo();
					RefreshLocalEncounterPanelRows();
				}
				else if (_autoBossFilter || IsLocalEncounterPanelOpen())
				{
					SelectAutoBossTarget(targetId2);
					int currentLiveBossTargetId = GetCurrentLiveBossTargetId();
					SetMainContentView(MainContentView.Dps, manual: false, force: true);
					RenderTiles((currentLiveBossTargetId > 0) ? (_engine.BuildSnapshotForTarget(currentLiveBossTargetId) ?? GetSnapshotForCurrentFilter()) : GetSnapshotForCurrentFilter());
					RefreshLocalEncounterPanelRows((currentLiveBossTargetId > 0) ? GetLiveEncounterPanelKey(currentLiveBossTargetId) : null);
				}
			});
		};
		_engine.BossEnded += delegate(int targetId, string targetName, DateTime firstHit, DateTime lastHit, int mobCode, int maxHp)
		{
			if (!_isLogViewMode)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					ArchiveDefeatedBossForDisplay(targetId, targetName, mobCode);
				});
			}
		};
		_engine.BossHpReset += delegate(int targetId, string targetName)
		{
			if (!_isLogViewMode)
			{
				lock (_sync)
				{
					_pendingUiTargetResets.Add(targetId);
				}
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					HandleBossHpReset(targetId, targetName);
				});
			}
		};
		_engine.SummonMerged += delegate(int sid, int oid)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				OnSummonMerged(sid, oid);
			});
		};
		_engine.UploadSuccess += delegate(string bossName)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				ShowSystemBalloon(bossName + " DPS 데이터 업로드 완료");
				RefreshAverageDpsAfterUpload();
			});
		};
		_engine.InspectCharacterDetected += delegate(string name, string serverName, int classId)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				int aion2ServerId = PartyTracker.GetAion2ServerId(serverName);
				string combatPowerText = ((aion2ServerId > 0) ? GetLookupCombatPowerText(name, aion2ServerId) : "대기");
				PartyMemberItem partyMemberItem = PartyMembers.FirstOrDefault((PartyMemberItem p) => p.IsInspectedCharacter && IsSameCharacter(p.Name, p.ServerName, name, serverName));
				if (partyMemberItem != null)
				{
					partyMemberItem.DisplayName = GetDisplayCharacterName(name);
					partyMemberItem.Job = NormalizeJobCode(classId);
					partyMemberItem.CombatPowerText = combatPowerText;
					partyMemberItem.UiScale = _meterUiScale;
					partyMemberItem.LookupExpiresAtUtc = DateTime.UtcNow.Add(TemporaryLookupLifetime);
					PartyMembers.Remove(partyMemberItem);
					InsertTemporaryLookupItem(partyMemberItem);
					ScheduleTemporaryLookupRemoval(partyMemberItem, TemporaryLookupLifetime);
					TryAutoShowLookup();
				}
				else
				{
					PartyMemberItem item = new PartyMemberItem
					{
						Name = name,
						DisplayName = GetDisplayCharacterName(name),
						ServerName = serverName,
						Job = NormalizeJobCode(classId),
						AvgDps10Text = "조회 중...",
						CombatPowerText = combatPowerText,
						SourceText = "조회",
						IsInspectedCharacter = true,
						IsTemporaryLookup = true,
						UiScale = _meterUiScale,
						FontWeightMode = _fontWeightMode
					};
					InsertTemporaryLookupItem(item);
					TryAutoShowLookup();
					FetchCharacterDpsAsync(item);
					ScheduleTemporaryLookupRemoval(item, TemporaryLookupLifetime);
				}
			});
		};
		_engine.LocalPlayerSpawned += delegate
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
			});
		};
	}

	private void ApplyBossCenteredRuntimePolicy()
	{
		_autoBossFilter = true;
		_bossOnlyMeasurement = true;
		_autoResetOnMapChange = false;
		_autoResetOnNewBoss = true;
		_maxDpsCards = Math.Clamp(_maxDpsCards, 1, 10);
		_engine.BossOnlyMeasurement = true;
		_engine.MapChangeAutoReset = false;
	}

	private void LoadMobNames()
	{
		try
		{
			_mobNameMap.LoadFromResource();
		}
		catch (Exception ex)
		{
			Console.WriteLine("몹 데이터 로드 실패: " + ex.Message);
		}
	}

	private void LoadBuffNames()
	{
		try
		{
			_buffNames.LoadFromResource();
		}
		catch (Exception ex)
		{
			Console.WriteLine("버프 데이터 로드 실패: " + ex.Message);
		}
	}

	private static (string Name, string Server) SplitPartyMemberName(string fullName)
	{
		string item = fullName;
		string item2 = "";
		int num = fullName.IndexOf('[');
		int num2 = fullName.IndexOf(']');
		if (num > 0 && num2 > num)
		{
			item = fullName.Substring(0, num).Trim();
			item2 = fullName.Substring(num + 1, num2 - num - 1).Trim();
		}
		return (Name: item, Server: item2);
	}

	private static string FormatPartyMemberLabel(string name, string server)
	{
		if (!string.IsNullOrWhiteSpace(server))
		{
			return name + "(" + server + ")";
		}
		return name;
	}

	private static bool IsTemporaryLookupItem(PartyMemberItem item)
	{
		if (!item.IsApplicant)
		{
			return item.IsInspectedCharacter;
		}
		return true;
	}

	private static int GetTemporaryLookupPriority(PartyMemberItem item)
	{
		if (item.IsInspectedCharacter)
		{
			return 0;
		}
		if (item.IsApplicant)
		{
			return 1;
		}
		return 2;
	}

	private int GetPartyMemberInsertIndex(int partySlot = int.MaxValue)
	{
		int i;
		for (i = 0; i < PartyMembers.Count && PartyMembers[i].IsInspectedCharacter && PartyMembers[i].IsTemporaryLookup; i++)
		{
		}
		for (int j = i; j < PartyMembers.Count; j++)
		{
			if (IsTemporaryLookupItem(PartyMembers[j]))
			{
				return j;
			}
			if (partySlot != int.MaxValue && PartyMembers[j].PartySlot > partySlot)
			{
				return j;
			}
		}
		return PartyMembers.Count;
	}

	private int GetTemporaryLookupInsertIndex(PartyMemberItem item)
	{
		if (item.IsInspectedCharacter)
		{
			return 0;
		}
		int temporaryLookupPriority = GetTemporaryLookupPriority(item);
		int i;
		for (i = GetPartyMemberInsertIndex(); i < PartyMembers.Count && GetTemporaryLookupPriority(PartyMembers[i]) <= temporaryLookupPriority; i++)
		{
		}
		return i;
	}

	private void InsertPartyMemberItem(PartyMemberItem item)
	{
		item.DisplayPreset = _displayPreset;
		item.ShowLookupSkillDisplay = IsLookupSkillEnabledForItem(item);
		item.SetVisualScale(_meterLayoutScale, _meterTextScale, _meterFontSizeDelta);
		PartyMembers.Insert(GetPartyMemberInsertIndex(item.PartySlot), item);
	}

	private void InsertTemporaryLookupItem(PartyMemberItem item)
	{
		_lookupRemovalAnimations.Remove(item);
		item.IsTemporaryLookup = true;
		item.LookupExpiresAtUtc = DateTime.UtcNow.Add(TemporaryLookupLifetime);
		item.DisplayPreset = _displayPreset;
		item.ShowLookupSkillDisplay = IsLookupSkillEnabledForItem(item);
		item.SetVisualScale(_meterLayoutScale, _meterTextScale, _meterFontSizeDelta);
		PartyMembers.Insert(GetTemporaryLookupInsertIndex(item), item);
		TrimTemporaryLookupItems();
	}

	private void TrimTemporaryLookupItems()
	{
		foreach (PartyMemberItem item in (from item in PartyMembers.Where(IsTemporaryLookupItem)
			orderby item.LookupExpiresAtUtc
			select item).Take(Math.Max(0, PartyMembers.Count(IsTemporaryLookupItem) - 5)).ToList())
		{
			RemoveTemporaryLookupItem(item, animate: true);
		}
	}

	private void RepositionPartyMemberItem(PartyMemberItem item)
	{
		int num = PartyMembers.IndexOf(item);
		if (num >= 0)
		{
			PartyMembers.RemoveAt(num);
			InsertPartyMemberItem(item);
		}
	}

	private static JobClass NormalizeJobCode(int jobCode)
	{
		switch (jobCode)
		{
		case 5:
		case 6:
		case 7:
		case 8:
			return JobClass.Gladiator;
		case 9:
		case 10:
		case 11:
		case 12:
			return JobClass.Templar;
		case 13:
		case 14:
		case 15:
		case 16:
			return JobClass.Ranger;
		case 17:
		case 18:
		case 19:
		case 20:
			return JobClass.Assassin;
		case 21:
		case 22:
		case 23:
		case 24:
			return JobClass.Spiritmaster;
		case 25:
		case 26:
		case 27:
		case 28:
			return JobClass.Sorcerer;
		case 29:
		case 30:
		case 31:
		case 32:
			return JobClass.Cleric;
		case 33:
		case 34:
		case 35:
		case 36:
			return JobClass.Chanter;
		case 37:
		case 38:
		case 39:
		case 40:
			return JobClass.Brawler;
		default:
			if (Enum.IsDefined(typeof(JobClass), jobCode))
			{
				return (JobClass)jobCode;
			}
			return JobClass.None;
		}
	}

	private void ClearLookupForPartyExit()
	{
		ClearPendingPartyRosterSnapshot();
		foreach (PartyMemberItem item in PartyMembers.Where((PartyMemberItem item) => !item.IsInspectedCharacter).ToList())
		{
			PartyMembers.Remove(item);
		}
		UpdateLookupEmptyState();
		RefreshAutoMainContentView();
	}

	private void ClearPendingPartyRosterSnapshot()
	{
		_pendingPartyRosterSnapshot.Clear();
		_pendingPartyRosterExpectedCount = 0;
	}

	private void ScheduleTemporaryLookupRemoval(PartyMemberItem item, TimeSpan delay)
	{
		item.LookupExpiresAtUtc = DateTime.UtcNow.Add(delay);
		TrimTemporaryLookupItems();
		Task.Run(async delegate
		{
			try
			{
				await Task.Delay(delay);
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					ExpireTemporaryLookupItem(item);
				});
			}
			catch
			{
			}
		});
	}

	private void ExpireTemporaryLookupItem(PartyMemberItem item)
	{
		if (PartyMembers.Contains(item) && item.IsTemporaryLookup)
		{
			TimeSpan timeSpan = item.LookupExpiresAtUtc - DateTime.UtcNow;
			if (timeSpan > TimeSpan.FromMilliseconds(25L))
			{
				ScheduleTemporaryLookupRemoval(item, timeSpan);
			}
			else
			{
				RemoveTemporaryLookupItem(item, animate: true);
			}
		}
	}

	private void RemoveTemporaryLookupItemNow(PartyMemberItem item)
	{
		if (PartyMembers.Contains(item))
		{
			PartyMembers.Remove(item);
		}
		_lookupRemovalAnimations.Remove(item);
		UpdateLookupEmptyState();
		RefreshAutoMainContentView();
	}

	private void QueueTemporaryLookupRemovalFallback(PartyMemberItem item)
	{
		Task.Run(async delegate
		{
			try
			{
				await Task.Delay(TimeSpan.FromMilliseconds(450L));
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					if (PartyMembers.Contains(item) && _lookupRemovalAnimations.Contains(item))
					{
						RemoveTemporaryLookupItemNow(item);
					}
				});
			}
			catch
			{
			}
		});
	}

	private void RemoveTemporaryLookupItem(PartyMemberItem item, bool animate)
	{
		if (!PartyMembers.Contains(item))
		{
			_lookupRemovalAnimations.Remove(item);
			return;
		}
		if (!animate || lstLookup == null || lstLookup.Visibility != Visibility.Visible)
		{
			RemoveTemporaryLookupItemNow(item);
			return;
		}
		if (!_lookupRemovalAnimations.Add(item))
		{
			QueueTemporaryLookupRemovalFallback(item);
			return;
		}
		if (!(lstLookup.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem listBoxItem))
		{
			RemoveTemporaryLookupItemNow(item);
			return;
		}
		listBoxItem.IsHitTestVisible = false;
		listBoxItem.RenderTransform = new TranslateTransform();
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(180L);
		Storyboard storyboard = new Storyboard();
		DoubleAnimation doubleAnimation = new DoubleAnimation
		{
			To = 0.0,
			Duration = timeSpan,
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseIn
			}
		};
		Storyboard.SetTarget(doubleAnimation, listBoxItem);
		Storyboard.SetTargetProperty(doubleAnimation, new PropertyPath(UIElement.OpacityProperty));
		DoubleAnimation doubleAnimation2 = new DoubleAnimation
		{
			To = -14.0,
			Duration = timeSpan,
			EasingFunction = new CubicEase
			{
				EasingMode = EasingMode.EaseIn
			}
		};
		Storyboard.SetTarget(doubleAnimation2, listBoxItem.RenderTransform);
		Storyboard.SetTargetProperty(doubleAnimation2, new PropertyPath(TranslateTransform.XProperty));
		storyboard.Children.Add(doubleAnimation);
		storyboard.Children.Add(doubleAnimation2);
		storyboard.Completed += delegate
		{
			RemoveTemporaryLookupItemNow(item);
		};
		QueueTemporaryLookupRemovalFallback(item);
		storyboard.Begin();
	}

	private static bool IsActiveBossCombatSnapshot(CombatSnapshot? snap)
	{
		if (snap != null && snap.TopTargetDamage > 0 && snap.Actors.Count > 0 && snap.IsBossConfirmed)
		{
			return (DateTime.UtcNow - snap.LastEventUtc).TotalSeconds <= 8.0;
		}
		return false;
	}

	private bool IsAutoMainViewEnabled()
	{
		return _isMainViewAutoMode;
	}

	private bool IsManualMainViewHoldActive()
	{
		if (_lastManualMainViewUtc != DateTime.MinValue)
		{
			return (DateTime.UtcNow - _lastManualMainViewUtc).TotalSeconds < 12.0;
		}
		return false;
	}

	private void ClearManualMainViewHold()
	{
		_lastManualMainViewUtc = DateTime.MinValue;
	}

	private void SetMainContentView(MainContentView view, bool manual = false, bool force = false)
	{
		if (manual)
		{
			_lastManualMainViewUtc = DateTime.UtcNow;
		}
		if (force || manual || IsAutoMainViewEnabled())
		{
			_mainContentView = view;
			ApplyMainContentView();
		}
	}

	private void TryAutoShowLookup(bool allowEmpty = false)
	{
		if (IsAutoMainViewEnabled() && !_isLogViewMode && !IsManualMainViewHoldActive() && (allowEmpty || HasAutoLookupContent()) && !IsActiveBossCombatSnapshot(GetSnapshotForCurrentFilter()))
		{
			SetMainContentView(MainContentView.Lookup);
		}
	}

	private void TryAutoShowDps(CombatSnapshot? snap = null, bool force = false)
	{
		if (IsAutoMainViewEnabled() && !_isLogViewMode && (force || IsActiveBossCombatSnapshot(snap)))
		{
			ClearManualMainViewHold();
			SetMainContentView(MainContentView.Dps);
		}
	}

	private void RefreshAutoMainContentView()
	{
		if (IsAutoMainViewEnabled() && !_isLogViewMode)
		{
			if (IsActiveBossCombatSnapshot(GetSnapshotForCurrentFilter()))
			{
				ClearManualMainViewHold();
				SetMainContentView(MainContentView.Dps, manual: false, force: true);
			}
			else if (IsManualMainViewHoldActive())
			{
				ApplyMainContentView();
			}
			else if (HasAutoLookupContent())
			{
				SetMainContentView(MainContentView.Lookup, manual: false, force: true);
			}
			else
			{
				SetMainContentView(MainContentView.Dps, manual: false, force: true);
			}
		}
	}

	private bool HasAutoLookupContent()
	{
		if (!(_currentDungeonContent != null))
		{
			return PartyMembers.Any(IsTemporaryLookupItem);
		}
		return true;
	}

	private void ApplyMainContentView()
	{
		if (lstDps != null && lstLookup != null && btnMainViewSwap != null)
		{
			bool flag = _mainContentView == MainContentView.Lookup;
			bool num = _lastAppliedMainContentView.HasValue && _lastAppliedMainContentView.Value != _mainContentView;
			_lastAppliedMainContentView = _mainContentView;
			if (chkAutoMainView != null)
			{
				chkAutoMainView.IsChecked = IsAutoMainViewEnabled();
			}
			if (chkAutoMainViewHud != null)
			{
				chkAutoMainViewHud.IsChecked = IsAutoMainViewEnabled();
			}
			ApplyMainViewSwapButtonState(btnMainViewSwap, flag);
			ApplyMainViewSwapButtonState(btnMainViewSwapHud, flag);
			lstLookup.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			lstDps.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
			filterPanel.Visibility = ((!(!_isHudMode && flag)) ? Visibility.Collapsed : Visibility.Visible);
			cmbFilterTarget.IsEnabled = !flag;
			cmbFilterTarget.Visibility = Visibility.Collapsed;
			if (bdLookupDungeonInfo != null)
			{
				bdLookupDungeonInfo.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
			}
			if (flag)
			{
				borderTopTarget.Visibility = Visibility.Collapsed;
			}
			if (num)
			{
				DpsUiAnimations.PlayViewSwitch(flag ? lstLookup : lstDps, flag);
			}
			UpdateLookupDungeonInfo();
			UpdateLookupEmptyState();
			ApplyHudLockedSurfaceState();
		}
	}

	private static void ApplyMainViewSwapButtonState(System.Windows.Controls.Button? button, bool isLookup)
	{
		if (button != null)
		{
			button.Content = (isLookup ? "조회" : "DPS");
			button.Tag = (isLookup ? "Lookup" : "Dps");
		}
	}

	private void UpdateLookupEmptyState()
	{
		if (txtLookupEmpty != null)
		{
			txtLookupEmpty.Visibility = ((_mainContentView != MainContentView.Lookup || PartyMembers.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void OnUserInfoResolved(int actorId)
	{
		if (actorId > 0 && !_isPaused)
		{
			_engine.TryBuildSnapshotNow();
			RenderTiles(_useDummyData ? CreateDummySnapshot() : GetSnapshotForCurrentFilter());
		}
	}

	private void OnExtendedUserInfoReceived(ExtendedUserInfoEvent info)
	{
		if (info.EntityId == 29)
		{
			ClearLookupForPartyExit();
		}
		else if (info.EntityId == 30)
		{
			OnDungeonContentDetected(info.Source);
			TryAutoShowLookup(allowEmpty: true);
		}
		else
		{
			if (info.ServerId <= 0 || string.IsNullOrWhiteSpace(info.Nickname))
			{
				return;
			}
			string aion2ServerName = PartyTracker.GetAion2ServerName(info.ServerId);
			if (string.IsNullOrWhiteSpace(aion2ServerName))
			{
				return;
			}
			if (info.CombatPower > 0)
			{
				RememberCombatPower(info.Nickname, info.ServerId, info.CombatPower);
			}
			string lookupCombatPowerText = GetLookupCombatPowerText(info.Nickname, info.ServerId);
			if (IsPartyApplicantExtendedUserInfo(info))
			{
				UpsertPartyApplicantFromExtendedUserInfo(info, aion2ServerName, lookupCombatPowerText);
			}
			if (IsPartyMemberExtendedUserInfo(info))
			{
				UpsertPartyMemberFromExtendedUserInfo(info, aion2ServerName, lookupCombatPowerText);
				TrackPartyRosterSnapshot(info, aion2ServerName);
			}
			if (!TryGetPacketCombatPowerValue(info.Nickname, info.ServerId, out var combatPower))
			{
				return;
			}
			string combatPower2 = FormatCombatPower(combatPower);
			foreach (DpsCardViewModel dpsCard in DpsCards)
			{
				if (IsSameCharacter(dpsCard.CharacterName, dpsCard.ServerName, info.Nickname, aion2ServerName))
				{
					dpsCard.CombatPower = combatPower2;
				}
			}
			foreach (PartyMemberItem partyMember in PartyMembers)
			{
				if (IsSameCharacter(partyMember.Name, partyMember.ServerName, info.Nickname, aion2ServerName))
				{
					partyMember.CombatPowerText = lookupCombatPowerText;
				}
			}
		}
	}

	private void AnnounceLocalPlayerIfNeeded(string localName, int serverId)
	{
		if (!string.IsNullOrWhiteSpace(localName) && serverId > 0)
		{
			string text = $"{serverId}:{localName.Trim()}";
			if (!string.Equals(_lastAnnouncedLocalPlayerIdentityKey, text, StringComparison.OrdinalIgnoreCase))
			{
				_lastAnnouncedLocalPlayerIdentityKey = text;
				ShowSystemBalloon(localName + " 캐릭터 접속을 인식했습니다.");
			}
		}
	}

	private async Task EnsureLocalCharacterConsentAsync(string localName, int actorId, int serverId)
	{
		if (string.IsNullOrWhiteSpace(localName) || serverId <= 0)
		{
			return;
		}
		string identityKey = $"{serverId}:{localName.Trim()}";
		string serverName = PartyTracker.GetAion2ServerName(serverId);
		_engine.TryGetCharNo(actorId, localName, serverId, out var charNo);
		RememberLocalCharacterConsentState(localName, serverId, serverName, charNo, null);
		lock (_characterConsentStatesLock)
		{
			if (_characterConsentCheckedKeys.Contains(identityKey) || !_characterConsentCheckingKeys.Add(identityKey))
			{
				return;
			}
		}
		try
		{
			CharacterConsentClient.ConsentResult consentResult = await CharacterConsentClient.GetAsync(localName, serverId, serverName, charNo);
			if (consentResult.Success)
			{
				RememberLocalCharacterConsentState(localName, serverId, serverName, charNo, consentResult.PublicConsent);
			}
			if (!consentResult.Success)
			{
				return;
			}
			if (consentResult.PublicConsent.HasValue)
			{
				MarkLocalCharacterConsentChecked(identityKey);
				return;
			}
			bool? accepted = null;
			await base.Dispatcher.InvokeAsync(delegate
			{
				CharacterConsentWindow characterConsentWindow = new CharacterConsentWindow(localName, serverName)
				{
					Owner = this
				};
				characterConsentWindow.ShowDialog();
				if (characterConsentWindow.HasChoice)
				{
					accepted = characterConsentWindow.IsAccepted;
				}
			});
			if (accepted.HasValue && (await CharacterConsentClient.SetAsync(localName, serverId, serverName, charNo, accepted.Value)).Success)
			{
				RememberLocalCharacterConsentState(localName, serverId, serverName, charNo, accepted.Value);
				MarkLocalCharacterConsentChecked(identityKey);
			}
		}
		finally
		{
			lock (_characterConsentStatesLock)
			{
				_characterConsentCheckingKeys.Remove(identityKey);
			}
		}
	}

	private void MarkLocalCharacterConsentChecked(string identityKey)
	{
		lock (_characterConsentStatesLock)
		{
			_characterConsentCheckedKeys.Add(identityKey);
		}
	}

	private void RememberLocalCharacterConsentState(string localName, int serverId, string serverName, int charNo, bool? publicConsent)
	{
		if (string.IsNullOrWhiteSpace(localName) || serverId <= 0)
		{
			return;
		}
		string text = localName.Trim();
		string key = $"{serverId}:{text}";
		lock (_characterConsentStatesLock)
		{
			_characterConsentStates.TryGetValue(key, out CharacterConsentState value);
			_characterConsentStates[key] = new CharacterConsentState(text, serverId, string.IsNullOrWhiteSpace(serverName) ? serverId.ToString() : serverName, (charNo > 0) ? charNo : (value?.CharNo ?? 0), publicConsent ?? value?.PublicConsent, DateTime.UtcNow);
		}
	}

	private IReadOnlyList<CharacterConsentState> GetCharacterConsentStatesSnapshot()
	{
		lock (_characterConsentStatesLock)
		{
			return (from state in _characterConsentStates.Values
				orderby state.LastSeenUtc descending, state.ServerId
				select state).ThenBy<CharacterConsentState, string>((CharacterConsentState state) => state.CharacterName, StringComparer.OrdinalIgnoreCase).ToList();
		}
	}

	private async Task<bool> SetCharacterPublicConsentFromSettingsAsync(CharacterConsentState state, bool publicConsent)
	{
		if (string.IsNullOrWhiteSpace(state.CharacterName) || state.ServerId <= 0)
		{
			return false;
		}
		CharacterConsentClient.ConsentResult consentResult = await CharacterConsentClient.SetAsync(state.CharacterName, state.ServerId, state.ServerName, state.CharNo, publicConsent);
		if (consentResult.Success)
		{
			RememberLocalCharacterConsentState(state.CharacterName, state.ServerId, state.ServerName, state.CharNo, publicConsent);
			MarkLocalCharacterConsentChecked(state.Key);
		}
		return consentResult.Success;
	}

	private void OnZoneEntryReceived(ZoneEntryEvent entry)
	{
		if (entry.Kind == 1 && entry.ContentCode > 0)
		{
			OnDungeonContentDetected(entry.ContentCode);
			AnnounceZoneEntryIfNeeded(entry.ContentCode);
			TryAutoShowLookup(allowEmpty: true);
		}
	}

	private void AnnounceZoneEntryIfNeeded(int contentCode)
	{
		if (contentCode > 0 && !(_currentDungeonContent == null))
		{
			DateTime utcNow = DateTime.UtcNow;
			if (_lastAnnouncedZoneEntryContentCode != contentCode || !(utcNow - _lastAnnouncedZoneEntryAtUtc < TimeSpan.FromSeconds(3L)))
			{
				_lastAnnouncedZoneEntryContentCode = contentCode;
				_lastAnnouncedZoneEntryAtUtc = utcNow;
				ShowDungeonMovementBalloon(_currentDungeonContent);
			}
		}
	}

	private static bool IsPartyMemberExtendedUserInfo(ExtendedUserInfoEvent info)
	{
		if (info.EntityId == 2 && (info.Source == 1 || info.Source == 5) && info.Mode >= 0 && info.Slot >= 0)
		{
			return info.Slot < 64;
		}
		return false;
	}

	private static bool IsPartyApplicantExtendedUserInfo(ExtendedUserInfoEvent info)
	{
		if (info.EntityId == 7 && info.Source == 4 && info.ServerId > 0)
		{
			return !string.IsNullOrWhiteSpace(info.Nickname);
		}
		return false;
	}

	private void TrackPartyRosterSnapshot(ExtendedUserInfoEvent info, string serverName)
	{
		if (info.Mode > 0 && info.Mode <= 64 && info.Slot >= 0 && info.Slot < 64)
		{
			if (_pendingPartyRosterExpectedCount != info.Mode || info.Slot == 0)
			{
				_pendingPartyRosterSnapshot.Clear();
				_pendingPartyRosterExpectedCount = info.Mode;
			}
			_pendingPartyRosterSnapshot[info.Slot] = GetCharacterKey(info.Nickname, serverName);
			if (_pendingPartyRosterSnapshot.Count >= _pendingPartyRosterExpectedCount)
			{
				ApplyPartyRosterSnapshot();
			}
		}
	}

	private void ApplyPartyRosterSnapshot()
	{
		if (_pendingPartyRosterExpectedCount <= 0 || _pendingPartyRosterSnapshot.Count < _pendingPartyRosterExpectedCount)
		{
			return;
		}
		HashSet<string> rosterKeys = new HashSet<string>(_pendingPartyRosterSnapshot.Values, StringComparer.OrdinalIgnoreCase);
		foreach (PartyMemberItem item in (from item in PartyMembers.Where(IsPersistentPartyLookupItem)
			where !rosterKeys.Contains(GetCharacterKey(item.Name, item.ServerName))
			select item).ToList())
		{
			PartyMembers.Remove(item);
		}
		ClearPendingPartyRosterSnapshot();
		UpdateLookupEmptyState();
		TryAutoShowLookup(allowEmpty: true);
	}

	private static bool IsPersistentPartyLookupItem(PartyMemberItem item)
	{
		if (!item.IsTemporaryLookup && !item.IsApplicant)
		{
			return !item.IsInspectedCharacter;
		}
		return false;
	}

	private static string GetCharacterKey(string name, string serverName)
	{
		return serverName.Trim() + "\u001f" + name.Trim();
	}

	private void UpsertPartyMemberFromExtendedUserInfo(ExtendedUserInfoEvent info, string serverName, string combatPowerText)
	{
		PartyMemberItem partyMemberItem = PartyMembers.FirstOrDefault((PartyMemberItem p) => IsSameCharacter(p.Name, p.ServerName, info.Nickname, serverName) && !p.IsTemporaryLookup && !p.IsApplicant && !p.IsInspectedCharacter);
		string sourceText = ((info.Source == 5) ? "포스" : "파티");
		if (partyMemberItem == null)
		{
			PartyMemberItem partyMemberItem2 = PartyMembers.FirstOrDefault((PartyMemberItem p) => IsSameCharacter(p.Name, p.ServerName, info.Nickname, serverName) && p.IsApplicant);
			if (partyMemberItem2 != null)
			{
				PartyMembers.Remove(partyMemberItem2);
			}
			partyMemberItem = new PartyMemberItem
			{
				Name = info.Nickname,
				DisplayName = GetDisplayCharacterName(info.Nickname),
				ServerName = serverName,
				Job = NormalizeJobCode(info.JobCode),
				AvgDps10Text = "조회 중...",
				CombatPowerText = combatPowerText,
				SourceText = sourceText,
				PartySlot = info.Slot,
				UiScale = _meterUiScale,
				FontWeightMode = _fontWeightMode
			};
			InsertPartyMemberItem(partyMemberItem);
			FetchCharacterDpsAsync(partyMemberItem);
		}
		else
		{
			partyMemberItem.Name = info.Nickname;
			partyMemberItem.DisplayName = GetDisplayCharacterName(info.Nickname);
			partyMemberItem.ServerName = serverName;
			partyMemberItem.SourceText = sourceText;
			partyMemberItem.IsApplicant = false;
			partyMemberItem.IsTemporaryLookup = false;
			partyMemberItem.LookupExpiresAtUtc = DateTime.MaxValue;
			partyMemberItem.CombatPowerText = combatPowerText;
			partyMemberItem.UiScale = _meterUiScale;
			JobClass jobClass = NormalizeJobCode(info.JobCode);
			if (jobClass != JobClass.None)
			{
				partyMemberItem.Job = jobClass;
			}
			if (partyMemberItem.PartySlot != info.Slot)
			{
				partyMemberItem.PartySlot = info.Slot;
				RepositionPartyMemberItem(partyMemberItem);
			}
		}
		UpdateLookupEmptyState();
	}

	private void UpsertPartyApplicantFromExtendedUserInfo(ExtendedUserInfoEvent info, string serverName, string combatPowerText)
	{
		PartyMemberItem partyMemberItem = PartyMembers.FirstOrDefault((PartyMemberItem p) => IsSameCharacter(p.Name, p.ServerName, info.Nickname, serverName) && p.IsApplicant);
		if (partyMemberItem != null)
		{
			PartyMembers.Remove(partyMemberItem);
		}
		PartyMemberItem item = new PartyMemberItem
		{
			Name = info.Nickname,
			DisplayName = GetDisplayCharacterName(info.Nickname),
			ServerName = serverName,
			Job = NormalizeJobCode(info.JobCode),
			AvgDps10Text = "조회 중...",
			CombatPowerText = combatPowerText,
			SourceText = "지원",
			IsApplicant = true,
			IsTemporaryLookup = true,
			UiScale = _meterUiScale,
			FontWeightMode = _fontWeightMode
		};
		InsertTemporaryLookupItem(item);
		TryAutoShowLookup();
		UpdateLookupEmptyState();
		FetchCharacterDpsAsync(item);
		ScheduleTemporaryLookupRemoval(item, TemporaryLookupLifetime);
	}

	private void OnDungeonContentDetected(int dungeonCode)
	{
		int num = _currentDungeonContent?.Code ?? 0;
		if (dungeonCode <= 0)
		{
			_currentDungeonContent = null;
			_engine.CurrentContentCode = 0;
			UpdateLookupDungeonInfo();
			if (num != 0)
			{
				RefreshVisibleAverageDpsForCurrentContent();
			}
			return;
		}
		_currentDungeonContent = (_dungeonContentMap.TryGet(dungeonCode, out DungeonContentInfo info) ? info : new DungeonContentInfo
		{
			Code = dungeonCode,
			Category = "던전",
			Name = $"Unknown {dungeonCode}"
		});
		_engine.CurrentContentCode = _currentDungeonContent.Code;
		UpdateLookupDungeonInfo();
		if (num != _currentDungeonContent.Code)
		{
			RefreshVisibleAverageDpsForCurrentContent();
		}
	}

	private void ShowDungeonMovementBalloon(DungeonContentInfo content)
	{
		string text = BuildDungeonMovementName(content);
		if (!string.IsNullOrWhiteSpace(text))
		{
			ShowSystemBalloon(text + GetDirectionParticle(text) + " 이동하였습니다.");
		}
	}

	private static string BuildDungeonMovementName(DungeonContentInfo content)
	{
		List<string> list = new List<string>();
		if (!string.IsNullOrWhiteSpace(content.Category))
		{
			list.Add(content.Category.Trim());
		}
		int? stage = content.Stage;
		if (stage.HasValue)
		{
			int valueOrDefault = stage.GetValueOrDefault();
			if (valueOrDefault > 0)
			{
				list.Add($"{valueOrDefault}단계");
				goto IL_008a;
			}
		}
		if (!string.IsNullOrWhiteSpace(content.Difficulty))
		{
			list.Add(content.Difficulty.Trim());
		}
		goto IL_008a;
		IL_008a:
		if (!string.IsNullOrWhiteSpace(content.Name))
		{
			list.Add(content.Name.Trim());
		}
		return string.Join(" ", list);
	}

	private static string GetDirectionParticle(string text)
	{
		char c = text.Trim().LastOrDefault();
		if (c < '가' || c > '힣')
		{
			return "로";
		}
		int num = (c - 44032) % 28;
		if (num != 0 && num != 8)
		{
			return "으로";
		}
		return "로";
	}

	private void RefreshVisibleAverageDpsForCurrentContent()
	{
		ResetCombatScoreAutoBudget();
		bool flag = CanFetchAverageDpsForSnapshot(GetSnapshotForCurrentFilter());
		foreach (PartyMemberItem item in PartyMembers.ToList())
		{
			if (!string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.ServerName))
			{
				item.AvgDps10Text = "조회 중...";
				FetchCharacterDpsAsync(item, force: true);
			}
		}
		foreach (DpsCardViewModel item2 in DpsCards.ToList())
		{
			if (!string.IsNullOrWhiteSpace(item2.CharacterName) && !string.IsNullOrWhiteSpace(item2.ServerName))
			{
				if (!flag)
				{
					ClearAverageDps(item2);
				}
				else
				{
					FetchCombatScoreAsync(item2, item2.CharacterName, item2.ServerName, forceRefresh: true);
				}
			}
		}
	}

	private void RefreshAverageDpsAfterUpload()
	{
		int refreshEpoch = Interlocked.Increment(ref _averageDpsRefreshEpoch);
		lock (_combatScoreCache)
		{
			_combatScoreCache.Clear();
			_combatScoreDungeonScopeCache.Clear();
			_combatScoreAutoRequestedThisSession.Clear();
		}
		MarkVisibleAverageDpsLoading();
		RefreshAverageDpsAfterUploadDelayAsync(refreshEpoch);
	}

	private void MarkVisibleAverageDpsLoading()
	{
		bool flag = CanFetchAverageDpsForSnapshot(GetSnapshotForCurrentFilter());
		foreach (PartyMemberItem item in PartyMembers.ToList())
		{
			if (!string.IsNullOrWhiteSpace(item.Name) && !string.IsNullOrWhiteSpace(item.ServerName))
			{
				item.AvgDps10Text = "조회 중...";
				item.IsDungeonAverageDps = false;
			}
		}
		foreach (DpsCardViewModel item2 in DpsCards.ToList())
		{
			if (!string.IsNullOrWhiteSpace(item2.CharacterName) && !string.IsNullOrWhiteSpace(item2.ServerName))
			{
				if (!flag)
				{
					ClearAverageDps(item2);
					continue;
				}
				item2.CombatScore = "조회 중...";
				item2.IsDungeonAverageDps = false;
			}
		}
	}

	private async Task RefreshAverageDpsAfterUploadDelayAsync(int refreshEpoch)
	{
		await Task.Delay(1200);
		if (refreshEpoch == Volatile.Read(in _averageDpsRefreshEpoch))
		{
			await base.Dispatcher.InvokeAsync(RefreshVisibleAverageDpsForCurrentContent);
		}
	}

	private bool IsAverageDpsRefreshStale(int requestEpoch)
	{
		return requestEpoch != Volatile.Read(in _averageDpsRefreshEpoch);
	}

	private async Task<DungeonContentInfo?> WaitForAverageDpsContentAsync(bool force)
	{
		DungeonContentInfo currentDungeonContent = _currentDungeonContent;
		if (force || ((object)currentDungeonContent != null && currentDungeonContent.Code > 0))
		{
			return currentDungeonContent;
		}
		await Task.Delay(900);
		return _currentDungeonContent;
	}

	private string GetAverageDpsBossCodeScope(DungeonContentInfo? content)
	{
		if (!_dungeonBossCatalogMap.TryGetBossCodes(content, out int[] bossCodes) || bossCodes.Length == 0)
		{
			return "";
		}
		return string.Join(",", bossCodes);
	}

	private bool IsAverageDpsResponseStale(string requestBossCodeScope)
	{
		return !string.Equals(requestBossCodeScope, GetAverageDpsBossCodeScope(_currentDungeonContent), StringComparison.Ordinal);
	}

	private void UpdateLookupDungeonInfo()
	{
		if (txtLookupDungeonName == null || txtLookupDungeonCategory == null || bdLookupDungeonDetail == null || txtLookupDungeonDetail == null)
		{
			return;
		}
		if (_currentDungeonContent == null)
		{
			txtLookupDungeonCategory.Text = "조회";
			bdLookupDungeonDetail.Visibility = Visibility.Collapsed;
			txtLookupDungeonDetail.Text = "";
			txtLookupDungeonName.Text = "던전 정보 없음";
			txtLookupDungeonName.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextMutedBrush");
			return;
		}
		txtLookupDungeonCategory.Text = (string.IsNullOrWhiteSpace(_currentDungeonContent.Category) ? "던전" : _currentDungeonContent.Category);
		string text = "";
		int? stage = _currentDungeonContent.Stage;
		if (stage.HasValue)
		{
			int valueOrDefault = stage.GetValueOrDefault();
			if (valueOrDefault > 0)
			{
				text = $"{valueOrDefault}단계";
				goto IL_011c;
			}
		}
		if (!string.IsNullOrWhiteSpace(_currentDungeonContent.Difficulty))
		{
			text = _currentDungeonContent.Difficulty;
		}
		goto IL_011c;
		IL_011c:
		txtLookupDungeonDetail.Text = text;
		bdLookupDungeonDetail.Visibility = (string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible);
		txtLookupDungeonName.Text = _currentDungeonContent.Name;
		txtLookupDungeonName.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimaryBrush");
	}

	private void MainViewSwap_Click(object sender, RoutedEventArgs e)
	{
		ToggleMainContentView();
	}

	private void ToggleMainContentView()
	{
		MainContentView view = ((_mainContentView == MainContentView.Lookup) ? MainContentView.Dps : MainContentView.Lookup);
		SetMainContentView(view, manual: true, force: true);
	}

	private void AutoMainView_Click(object sender, RoutedEventArgs e)
	{
		_isMainViewAutoMode = ((sender as System.Windows.Controls.CheckBox)?.IsChecked == true) ?? false;
		ClearManualMainViewHold();
		if (_isMainViewAutoMode)
		{
			RefreshAutoMainContentView();
		}
		else
		{
			ApplyMainContentView();
		}
	}

	private string GetDisplayCharacterName(string name)
	{
		if (chkHideNickname.IsChecked != true || name.StartsWith("Actor "))
		{
			return name;
		}
		if (name.Length == 1)
		{
			return "*";
		}
		if (name.Length == 2)
		{
			return name.Substring(0, 1) + "*";
		}
		return name.Substring(0, 1) + new string('*', name.Length - 2) + name.Substring(name.Length - 1);
	}

	private async Task FetchCharacterDpsAsync(PartyMemberItem item, bool force = false)
	{
		try
		{
			int requestEpoch = Volatile.Read(in _averageDpsRefreshEpoch);
			int serverId = PartyTracker.GetAion2ServerId(item.ServerName);
			if (serverId == 0)
			{
				item.AvgDps10Text = "서버 미지원";
				item.IsDungeonAverageDps = false;
				item.SetStigmaStatus(_lookupSkillDisplayEnabled ? "스킬 조회 불가" : "");
				return;
			}
			item.IsMeterUserOnline = ShouldShowMeterUserMarker(item.Name, serverId);
			if (TryGetPacketCombatPowerValue(item.Name, serverId, out var combatPower))
			{
				item.CombatPowerText = combatPower.ToString("N0");
			}
			if (IsLookupSkillEnabledForItem(item))
			{
				FetchOfficialStigmaSummaryAsync(item, serverId);
			}
			else
			{
				item.SetStigmaStatus("");
			}
			double avg = 0.0;
			try
			{
				DungeonContentInfo dungeonContentInfo = await WaitForAverageDpsContentAsync(force);
				string requestBossCodeScope = GetAverageDpsBossCodeScope(dungeonContentInfo);
				string requestUri = BuildCharacterApiUrl(item.Name, serverId, dungeonContentInfo);
				HttpResponseMessage httpResponseMessage = await _partyHttp.GetAsync(requestUri);
				if (IsAverageDpsRefreshStale(requestEpoch))
				{
					return;
				}
				if (IsAverageDpsResponseStale(requestBossCodeScope))
				{
					item.AvgDps10Text = "조회 중...";
					base.Dispatcher.BeginInvoke((Action)delegate
					{
						FetchCharacterDpsAsync(item, force: true);
					});
				}
				else if (httpResponseMessage.IsSuccessStatusCode)
				{
					using JsonDocument jsonDocument = JsonDocument.Parse(await httpResponseMessage.Content.ReadAsStringAsync());
					if (jsonDocument.RootElement.TryGetProperty("success", out var value) && value.GetBoolean())
					{
						if (jsonDocument.RootElement.TryGetProperty("character", out var value2))
						{
							if (TryReadAverageDps(jsonDocument.RootElement, value2, out var avg2, out var isDungeonAverage))
							{
								avg = avg2;
								item.IsDungeonAverageDps = isDungeonAverage;
							}
							else
							{
								item.IsDungeonAverageDps = false;
							}
							if (TryReadMeterPresence(value2, out var seenUtc))
							{
								RememberMeterPresence(item.Name, serverId, seenUtc);
								item.IsMeterUserOnline = DateTime.UtcNow - seenUtc <= MeterPresenceFreshness;
							}
							item.AvgDps10Text = ((avg > 0.0) ? avg.ToString("N0") : "기록없음");
							item.CombatPowerText = GetLookupCombatPowerText(item.Name, serverId);
						}
						else
						{
							item.AvgDps10Text = "기록없음";
							item.IsDungeonAverageDps = false;
							item.CombatPowerText = GetLookupCombatPowerText(item.Name, serverId);
						}
					}
					else
					{
						item.AvgDps10Text = "기록없음";
						item.IsDungeonAverageDps = false;
						item.CombatPowerText = GetLookupCombatPowerText(item.Name, serverId);
					}
				}
				else
				{
					item.AvgDps10Text = "기록없음";
					item.IsDungeonAverageDps = false;
					item.CombatPowerText = GetLookupCombatPowerText(item.Name, serverId);
				}
			}
			catch
			{
				if (!IsAverageDpsRefreshStale(requestEpoch))
				{
					item.AvgDps10Text = "기록없음";
					item.IsDungeonAverageDps = false;
					item.CombatPowerText = GetLookupCombatPowerText(item.Name, serverId);
				}
			}
		}
		catch (Exception ex)
		{
			item.AvgDps10Text = "조회 실패";
			item.IsDungeonAverageDps = false;
			int aion2ServerId = PartyTracker.GetAion2ServerId(item.ServerName);
			if (aion2ServerId > 0)
			{
				item.CombatPowerText = GetLookupCombatPowerText(item.Name, aion2ServerId);
			}
			Console.WriteLine("[FetchDps] Error: " + ex.Message);
		}
	}

	private void ClearOfficialSkillSummaryCache()
	{
		lock (_combatScoreCache)
		{
			_officialStigmaCache.Clear();
			_officialStigmaLoading.Clear();
		}
	}

	private void RefreshOfficialSkillSummaries(bool force)
	{
		if (!_lookupSkillDisplayEnabled)
		{
			ApplyLookupSkillDisplayEnabledToItems();
			return;
		}
		foreach (PartyMemberItem item in PartyMembers.ToList())
		{
			int aion2ServerId = PartyTracker.GetAion2ServerId(item.ServerName);
			if (aion2ServerId > 0 && !string.IsNullOrWhiteSpace(item.Name) && IsLookupSkillEnabledForItem(item))
			{
				FetchOfficialStigmaSummaryAsync(item, aion2ServerId, force);
			}
		}
	}

	private void ApplyLookupSkillDisplayEnabledToItems()
	{
		foreach (PartyMemberItem partyMember in PartyMembers)
		{
			if (!(partyMember.ShowLookupSkillDisplay = IsLookupSkillEnabledForItem(partyMember)))
			{
				partyMember.SetStigmaStatus("");
			}
		}
	}

	private bool IsLookupSkillEnabledForItem(PartyMemberItem item)
	{
		return IsLookupSkillEnabledForJob(item.Job);
	}

	private bool IsLookupSkillEnabledForJob(JobClass job)
	{
		if (!_lookupSkillDisplayEnabled)
		{
			return false;
		}
		if (job == JobClass.None)
		{
			return true;
		}
		LookupSkillClass lookupSkillClass = _lookupSkillCatalog.FindClassByJob(job);
		if (lookupSkillClass != null)
		{
			return !_lookupSkillDisabledClasses.Contains(lookupSkillClass.Key);
		}
		return true;
	}

	private async Task FetchOfficialStigmaSummaryAsync(PartyMemberItem item, int serverId, bool force = false)
	{
		int lookupSkillSelectionVersion = Volatile.Read(in _lookupSkillSelectionVersion);
		if (!IsLookupSkillEnabledForItem(item))
		{
			item.ShowLookupSkillDisplay = false;
			item.SetStigmaStatus("");
			return;
		}
		item.ShowLookupSkillDisplay = true;
		if (serverId <= 0 || string.IsNullOrWhiteSpace(item.Name))
		{
			item.SetStigmaStatus("스킬 조회 불가");
			return;
		}
		string cacheKey = GetCombatScoreCacheKey(item.Name, serverId);
		lock (_combatScoreCache)
		{
			if (!force && _officialStigmaCache.TryGetValue(cacheKey, out string value))
			{
				if (_lookupSkillDisplayEnabled)
				{
					ApplyStigmaSummary(item, value ?? "스티그마 없음");
				}
				return;
			}
			if (_officialStigmaLoading.Contains(cacheKey))
			{
				item.SetStigmaStatus("스킬 조회 중...");
				return;
			}
			_officialStigmaLoading.Add(cacheKey);
		}
		if (!_lookupSkillDisplayEnabled)
		{
			return;
		}
		item.SetStigmaStatus("스킬 조회 중...");
		try
		{
			await _officialApiRequestGate.WaitAsync();
			try
			{
				string text = await FetchOfficialCharacterIdAsync(item.Name, serverId);
				if (lookupSkillSelectionVersion != Volatile.Read(in _lookupSkillSelectionVersion) || !_lookupSkillDisplayEnabled)
				{
					return;
				}
				if (string.IsNullOrWhiteSpace(text))
				{
					item.SetStigmaStatus("캐릭터 없음");
					return;
				}
				(string, JobClass) tuple = await FetchOfficialStigmaSummaryByCharacterIdAsync(text, serverId, item.Job);
				if (lookupSkillSelectionVersion != Volatile.Read(in _lookupSkillSelectionVersion))
				{
					return;
				}
				var (text2, _) = tuple;
				if (item.Job == JobClass.None && tuple.Item2 != JobClass.None)
				{
					item.Job = tuple.Item2;
				}
				if (!IsLookupSkillEnabledForItem(item))
				{
					item.ShowLookupSkillDisplay = false;
					item.SetStigmaStatus("");
					return;
				}
				lock (_combatScoreCache)
				{
					_officialStigmaCache[cacheKey] = text2;
				}
				ApplyStigmaSummary(item, text2);
			}
			finally
			{
				_officialApiRequestGate.Release();
			}
		}
		catch
		{
			if (_lookupSkillDisplayEnabled)
			{
				item.SetStigmaStatus("스킬 조회 실패");
			}
		}
		finally
		{
			lock (_combatScoreCache)
			{
				_officialStigmaLoading.Remove(cacheKey);
			}
		}
	}

	private async Task<string?> FetchOfficialCharacterIdAsync(string characterName, int serverId)
	{
		string requestUri = $"https://aion2.plaync.com/ko-kr/api/search/aion2/search/v2/character?keyword={Uri.EscapeDataString(characterName)}&serverId={serverId}&page=1&size=5";
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
		request.Headers.TryAddWithoutValidation("User-Agent", "INGMeter/1.3");
		using HttpResponseMessage response = await _partyHttp.SendAsync(request);
		if (!response.IsSuccessStatusCode)
		{
			return null;
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		if (!jsonDocument.RootElement.TryGetProperty("list", out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
		{
			return null;
		}
		string text = null;
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (item.TryGetProperty("characterId", out var value2))
			{
				string text2 = value2.GetString();
				if (string.IsNullOrWhiteSpace(text2))
				{
					text2 = value2.ToString();
				}
				if (text == null)
				{
					text = text2;
				}
				if (item.TryGetProperty("name", out var value3) && string.Equals(CleanOfficialCharacterName(value3.GetString()), characterName, StringComparison.Ordinal))
				{
					return text2;
				}
			}
		}
		return text;
	}

	private async Task<(string Summary, JobClass InferredJob)> FetchOfficialStigmaSummaryByCharacterIdAsync(string characterId, int serverId, JobClass knownJob)
	{
		string value = Uri.EscapeDataString(Uri.UnescapeDataString(characterId));
		string requestUri = $"https://aion2.plaync.com/api/character/equipment?lang=ko&characterId={value}&serverId={serverId}";
		using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, requestUri);
		request.Headers.TryAddWithoutValidation("User-Agent", "INGMeter/1.3");
		using HttpResponseMessage response = await _partyHttp.SendAsync(request);
		if (!response.IsSuccessStatusCode)
		{
			return (Summary: "스킬 조회 실패", InferredJob: knownJob);
		}
		using JsonDocument jsonDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
		return BuildOfficialLookupSkillSummary(jsonDocument.RootElement, knownJob);
	}

	private (string Summary, JobClass InferredJob) BuildOfficialLookupSkillSummary(JsonElement root, JobClass knownJob)
	{
		if (!TryGetNestedProperty(root, out var value, "skill", "skillList") || value.ValueKind != JsonValueKind.Array)
		{
			return (Summary: "스티그마 없음", InferredJob: knownJob);
		}
		HashSet<int> skillIds = (from skill in value.EnumerateArray()
			select TryReadSkillId(skill, out var id) ? id : 0 into id
			where id > 0
			select id).ToHashSet();
		LookupSkillClass lookupSkillClass = _lookupSkillCatalog.FindClassByJob(knownJob) ?? _lookupSkillCatalog.InferClassFromSkillIds(skillIds);
		JobClass item = lookupSkillClass?.Job ?? knownJob;
		if (lookupSkillClass != null && _lookupSkillSelections.TryGetValue(lookupSkillClass.Key, out HashSet<int> value2) && value2.Count > 0)
		{
			return (Summary: BuildSelectedLookupSkillSummary(value, lookupSkillClass, value2), InferredJob: item);
		}
		return (Summary: BuildOfficialStigmaSummary(root), InferredJob: item);
	}

	private static string BuildSelectedLookupSkillSummary(JsonElement skillList, LookupSkillClass catalogClass, IReadOnlySet<int> selectedSkillIds)
	{
		Dictionary<int, (string Name, int Level, bool Acquired)> officialSkills = new Dictionary<int, (string, int, bool)>();
		foreach (JsonElement item4 in skillList.EnumerateArray())
		{
			if (TryReadSkillId(item4, out var id))
			{
				string item = "";
				if (TryGetPropertyIgnoreCase(item4, "name", out var value))
				{
					item = value.GetString()?.Trim() ?? "";
				}
				int item2 = 0;
				if (TryGetPropertyIgnoreCase(item4, "skillLevel", out var value2) && TryReadDouble(value2, out var number))
				{
					item2 = Math.Max(0, (int)Math.Round(number));
				}
				bool item3 = true;
				if (TryGetPropertyIgnoreCase(item4, "acquired", out var value3) && TryReadDouble(value3, out var number2))
				{
					item3 = number2 > 0.0;
				}
				officialSkills[id] = (item, item2, item3);
			}
		}
		List<LookupSkillInfo> list = catalogClass.AllSkills.Where((LookupSkillInfo skill) => selectedSkillIds.Contains(skill.Id)).ToList();
		if (list.Count == 0)
		{
			return "스킬 없음";
		}
		return string.Join(" · ", list.Select(delegate(LookupSkillInfo skill)
		{
			if (officialSkills.TryGetValue(skill.Id, out (string, int, bool) value4))
			{
				string text;
				if (!string.IsNullOrWhiteSpace(value4.Item1))
				{
					(text, _, _) = value4;
				}
				else
				{
					text = skill.Name;
				}
				string value5 = text;
				int value6 = (value4.Item3 ? value4.Item2 : 0);
				return $"{value5} {value6}";
			}
			return skill.Name + " 0";
		}));
	}

	private static bool TryReadSkillId(JsonElement skill, out int id)
	{
		id = 0;
		if (!TryGetPropertyIgnoreCase(skill, "id", out var value))
		{
			return false;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out id))
		{
			return id > 0;
		}
		if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
		{
			return id > 0;
		}
		return false;
	}

	private static string BuildOfficialStigmaSummary(JsonElement root)
	{
		if (!TryGetNestedProperty(root, out var value, "skill", "skillList") || value.ValueKind != JsonValueKind.Array)
		{
			return "스티그마 없음";
		}
		List<(string, int, bool)> list = new List<(string, int, bool)>();
		foreach (JsonElement item3 in value.EnumerateArray())
		{
			if (!TryGetPropertyIgnoreCase(item3, "category", out var value2) || !string.Equals(value2.GetString(), "Dp", StringComparison.OrdinalIgnoreCase) || (TryGetPropertyIgnoreCase(item3, "acquired", out var value3) && TryReadDouble(value3, out var number) && number <= 0.0))
			{
				continue;
			}
			JsonElement value4;
			double number2;
			bool item = TryGetPropertyIgnoreCase(item3, "equip", out value4) && TryReadDouble(value4, out number2) && number2 > 0.0;
			if (!TryGetPropertyIgnoreCase(item3, "name", out var value5))
			{
				continue;
			}
			string text = value5.GetString();
			if (!string.IsNullOrWhiteSpace(text))
			{
				int item2 = 0;
				if (TryGetPropertyIgnoreCase(item3, "skillLevel", out var value6) && TryReadDouble(value6, out var number3))
				{
					item2 = (int)Math.Round(number3);
				}
				list.Add((text, item2, item));
			}
		}
		if (list.Count == 0)
		{
			return "스티그마 없음";
		}
		List<(string, int, bool)> list2 = (from x in list
			where x.Equipped
			orderby x.Level descending
			select x).ThenBy<(string, int, bool), string>(((string Name, int Level, bool Equipped) x) => x.Name, StringComparer.Ordinal).Take(5).ToList();
		if (list2.Count < 5)
		{
			list2.AddRange((from x in list
				where !x.Equipped
				orderby x.Level descending
				select x).ThenBy<(string, int, bool), string>(((string Name, int Level, bool Equipped) x) => x.Name, StringComparer.Ordinal).Take(5 - list2.Count));
		}
		return string.Join(" · ", from x in list2.OrderByDescending<(string, int, bool), int>(((string Name, int Level, bool Equipped) x) => x.Level).ThenBy<(string, int, bool), string>(((string Name, int Level, bool Equipped) x) => x.Name, StringComparer.Ordinal)
			select $"{x.Name} {x.Level}");
	}

	private static void ApplyStigmaSummary(PartyMemberItem item, string summary)
	{
		List<StigmaBadgeItem> list = ParseStigmaBadges(summary);
		if (list.Count == 0)
		{
			item.SetStigmaStatus(IsParsedStigmaSummary(summary) ? "스티그마 없음" : summary);
		}
		else
		{
			item.SetStigmaBadges(list, summary);
		}
	}

	private static bool IsParsedStigmaSummary(string summary)
	{
		if (string.IsNullOrWhiteSpace(summary))
		{
			return false;
		}
		return summary.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any((string part) => Regex.IsMatch(part.Trim(), "^(?:Lv)?(?:\\d+\\s+.+|.+?\\s+\\d+)$"));
	}

	private static List<StigmaBadgeItem> ParseStigmaBadges(string summary)
	{
		List<StigmaBadgeItem> list = new List<StigmaBadgeItem>();
		if (string.IsNullOrWhiteSpace(summary) || summary.Contains("없음", StringComparison.Ordinal) || summary.Contains("조회", StringComparison.Ordinal) || summary.Contains("불가", StringComparison.Ordinal) || summary.Contains("실패", StringComparison.Ordinal))
		{
			return list;
		}
		string[] array = summary.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		for (int i = 0; i < array.Length; i++)
		{
			Match match = Regex.Match(array[i].Trim(), "^(?:Lv)?(?:(?<level>\\d+)\\s+(?<name>.+)|(?<name2>.+?)\\s+(?<level2>\\d+))$");
			if (match.Success)
			{
				string text = (match.Groups["name"].Success ? match.Groups["name"].Value.Trim() : match.Groups["name2"].Value.Trim());
				string s = (match.Groups["level"].Success ? match.Groups["level"].Value : match.Groups["level2"].Value);
				if (!string.IsNullOrWhiteSpace(text) && int.TryParse(s, out var result))
				{
					(string, string, string) stigmaBadgeColors = GetStigmaBadgeColors(result);
					StigmaBadgeItem obj = new StigmaBadgeItem
					{
						Name = text,
						Level = result
					};
					(obj.BackgroundBrush, obj.BorderBrush, obj.ForegroundBrush) = stigmaBadgeColors;
					list.Add(obj);
				}
			}
		}
		return list;
	}

	private static (string Background, string Border, string Foreground) GetStigmaBadgeColors(int level)
	{
		if (level >= 10)
		{
			if (level < 20)
			{
				if (level >= 15)
				{
					return (Background: "#422006", Border: "#f59e0b", Foreground: "#fde68a");
				}
				return (Background: "#172554", Border: "#3b82f6", Foreground: "#bfdbfe");
			}
			return (Background: "#3b1431", Border: "#ec4899", Foreground: "#fbcfe8");
		}
		if (level >= 5)
		{
			return (Background: "#143728", Border: "#10b981", Foreground: "#bbf7d0");
		}
		return (Background: "#1f2937", Border: "#64748b", Foreground: "#e5e7eb");
	}

	private static string CleanOfficialCharacterName(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "";
		}
		return WebUtility.HtmlDecode(Regex.Replace(value, "<.*?>", "")).Trim();
	}

	private void ShowBalloonMessage(string message)
	{
		Border border = new Border
		{
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(4.0),
			Padding = new Thickness(8.0, 4.0, 8.0, 4.0),
			Margin = new Thickness(0.0, 0.0, 0.0, 4.0),
			Opacity = 0.0,
			RenderTransform = new TranslateTransform(0.0, 20.0),
			IsHitTestVisible = false
		};
		border.SetResourceReference(Border.BackgroundProperty, "ThemePanelBackgroundBrush");
		border.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
		TextBlock textBlock = new TextBlock
		{
			Text = message,
			FontSize = 11.0,
			FontWeight = FontWeights.SemiBold
		};
		textBlock.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimaryBrush");
		border.Child = textBlock;
		stackApplicants.Children.Add(border);
		UpdateBalloonPlacement();
		popApplicant.IsOpen = true;
		DoubleAnimation animation = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(300L));
		DoubleAnimation animation2 = new DoubleAnimation(20.0, 0.0, TimeSpan.FromMilliseconds(300L));
		border.BeginAnimation(UIElement.OpacityProperty, animation);
		border.RenderTransform.BeginAnimation(TranslateTransform.YProperty, animation2);
		DispatcherTimer timer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(5L)
		};
		timer.Tick += delegate
		{
			timer.Stop();
			DoubleAnimation doubleAnimation = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(300L));
			doubleAnimation.Completed += delegate
			{
				stackApplicants.Children.Remove(border);
				UpdateBalloonPlacement();
				if (stackApplicants.Children.Count == 0)
				{
					popApplicant.IsOpen = false;
				}
			};
			border.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
		};
		timer.Start();
	}

	private void UpdateBalloonPlacement()
	{
		if (base.IsLoaded)
		{
			stackApplicants.UpdateLayout();
			double actualHeight = stackApplicants.ActualHeight;
			popApplicant.HorizontalOffset = 10.0;
			popApplicant.VerticalOffset = Math.Max(10.0, rootBorder.ActualHeight - actualHeight - 10.0);
			double num = bdUploadPopup.ActualHeight;
			if (num <= 0.0)
			{
				num = 28.0;
			}
			popUpload.HorizontalOffset = 10.0;
			popUpload.VerticalOffset = Math.Max(10.0, rootBorder.ActualHeight - num - 10.0);
		}
	}

	private void ShowSystemBalloon(string message)
	{
		ShowBalloonMessage(message ?? "");
	}

	private static bool IsRoutineCaptureStatus(string message)
	{
		if (!message.StartsWith("Running (WinDivert", StringComparison.OrdinalIgnoreCase) && !message.StartsWith("Running (Npcap", StringComparison.OrdinalIgnoreCase) && !message.StartsWith("AION2 flow detected.", StringComparison.OrdinalIgnoreCase))
		{
			return string.Equals(message, "Stopped", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private string BuildStatusTooltipDetail()
	{
		if (!string.IsNullOrWhiteSpace(_captureStartFailureMessage))
		{
			return "\n캡처 오류: " + _captureStartFailureMessage;
		}
		return "";
	}

	private bool TryGetDominantActiveBoss(int confirmedTargetId, out TargetInfo dominantBoss)
	{
		dominantBoss = null;
		List<TargetInfo> activeBossesByDamage = GetActiveBossesByDamage();
		if (activeBossesByDamage.Count < 2)
		{
			return false;
		}
		if (!activeBossesByDamage.Any((TargetInfo t) => t.TargetId != confirmedTargetId))
		{
			return false;
		}
		dominantBoss = activeBossesByDamage[0];
		return true;
	}

	private bool TryGetDominantActiveBoss(out TargetInfo dominantBoss)
	{
		dominantBoss = null;
		List<TargetInfo> activeBossesByDamage = GetActiveBossesByDamage();
		if (activeBossesByDamage.Count == 0)
		{
			return false;
		}
		dominantBoss = activeBossesByDamage[0];
		return true;
	}

	private List<TargetInfo> GetActiveBossesByDamage()
	{
		DateTime nowUtc = DateTime.UtcNow;
		return (from t in _engine.GetAllTargets()
			where t.TotalDamage > 0
			where (nowUtc - t.LastHit).TotalSeconds <= 10.0
			orderby t.TotalDamage descending, t.LastHit descending
			select t).ToList();
	}

	private void SelectAutoBossTarget(int targetId)
	{
		int num = ResolveLiveBossFocusTarget(targetId);
		if (num > 0)
		{
			SetLiveBossEncounter(num);
		}
		else
		{
			SetLiveBossEncounterFallback();
		}
		PopulateTargetCombo();
	}

	private int GetCurrentLiveBossTargetId(IReadOnlyList<TargetInfo>? targets = null)
	{
		if (_encounterViewKind != EncounterViewKind.LiveBoss)
		{
			return 0;
		}
		int num = ResolveLiveBossFocusTarget(_activeBossTargetId, targets);
		if (num > 0)
		{
			if (_activeBossTargetId != num || _selectedTargetFilterOption.Kind != TargetFilterItemKind.LiveBoss || _selectedTargetFilterOption.TargetId != num)
			{
				SetLiveBossEncounter(num, syncCombo: false);
			}
			return num;
		}
		if (_activeBossTargetId != 0 || _selectedTargetFilterOption.Kind == TargetFilterItemKind.LiveBoss)
		{
			SetLiveBossEncounterFallback(syncCombo: false);
		}
		return 0;
	}

	private int ResolveLiveBossFocusTarget(int preferredTargetId = 0, IReadOnlyList<TargetInfo>? targets = null)
	{
		if (targets == null)
		{
			targets = _engine.GetAllTargets();
		}
		List<TargetInfo> list = targets.Where((TargetInfo t) => t.TotalDamage > 0 || t.TargetId == preferredTargetId || t.TargetId == _activeBossTargetId).ToList();
		if (list.Count == 0)
		{
			return 0;
		}
		List<TargetInfo> list2 = (from t in list
			where (DateTime.UtcNow - t.LastHit).TotalSeconds <= 10.0
			orderby t.TotalDamage descending, t.LastHit descending
			select t).ToList();
		if (list2.Count >= 2)
		{
			return list2[0].TargetId;
		}
		if (preferredTargetId > 0 && list.Any((TargetInfo t) => t.TargetId == preferredTargetId))
		{
			return preferredTargetId;
		}
		if (_activeBossTargetId > 0 && list.Any((TargetInfo t) => t.TargetId == _activeBossTargetId))
		{
			return _activeBossTargetId;
		}
		if (list2.Count == 1)
		{
			return list2[0].TargetId;
		}
		return (from t in list
			orderby t.TotalDamage descending, t.LastHit descending
			select t).First().TargetId;
	}

	private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Escape && IsLocalEncounterPanelOpen())
		{
			CloseLocalEncounterHistoryPanel();
			e.Handled = true;
			return;
		}
		System.Windows.Controls.TextBox? localEncounterBossSearchTextBox = _localEncounterBossSearchTextBox;
		if (localEncounterBossSearchTextBox == null || !localEncounterBossSearchTextBox.IsKeyboardFocusWithin)
		{
			if (IsLocalEncounterPanelOpen())
			{
				e.Handled = TryHandleLocalEncounterHistoryNavigationKey(e.Key) || IsSuppressedLocalEncounterHistoryKey(e.Key);
			}
			else if ((e.Key == Key.Prior || e.Key == Key.Next) && Keyboard.Modifiers == ModifierKeys.None && !IsTextInputFocused())
			{
				MoveTargetFilterSelection((e.Key == Key.Next) ? 1 : (-1));
				e.Handled = true;
			}
		}
	}

	private static bool IsTextInputFocused()
	{
		IInputElement focusedElement = Keyboard.FocusedElement;
		if (!(focusedElement is System.Windows.Controls.TextBox) && !(focusedElement is PasswordBox))
		{
			return focusedElement is System.Windows.Controls.Primitives.TextBoxBase;
		}
		return true;
	}

	private void MoveTargetFilterSelection(int direction)
	{
		if (cmbFilterTarget == null || cmbFilterTarget.Visibility != Visibility.Visible || cmbFilterTarget.Items.Count == 0)
		{
			return;
		}
		int num = cmbFilterTarget.SelectedIndex;
		if (num < 0)
		{
			num = 0;
		}
		int num2 = num;
		ComboBoxItem comboBoxItem;
		TargetFilterOption option;
		do
		{
			num2 += direction;
			if (num2 < 0 || num2 >= cmbFilterTarget.Items.Count)
			{
				return;
			}
			comboBoxItem = cmbFilterTarget.Items[num2] as ComboBoxItem;
		}
		while (comboBoxItem == null || !TryGetTargetFilterOption(comboBoxItem.Tag, out option) || option.Kind == TargetFilterItemKind.ClearHistory);
		if (TryGetTargetFilterOption(comboBoxItem.Tag, out TargetFilterOption option2))
		{
			SetSelectedTargetFilterOption(option2);
			RenderTiles(GetSnapshotForCurrentFilter());
			if (IsCombatDetailWindowOpen())
			{
				RenderDetailForCurrentEncounter();
			}
			RefreshLocalEncounterPanelRows(GetLocalEncounterPanelKeyFromCurrentFilter());
		}
	}

	private List<ArchivedBossRecord> CaptureArchivedBossRecords()
	{
		List<ArchivedBossRecord> list = new List<ArchivedBossRecord>();
		try
		{
			foreach (TargetInfo item in from t in _engine.GetAllTargets()
				orderby t.LastHit descending
				select t)
			{
				CombatSnapshot combatSnapshot = _engine.BuildSnapshotForTarget(item.TargetId);
				if (!(combatSnapshot == null) && combatSnapshot.TopTargetDamage > 0 && combatSnapshot.Actors.Count != 0 && (combatSnapshot.TopTargetHits > 1 || !(combatSnapshot.TopTargetDuration.TotalSeconds < 2.0)))
				{
					list.Add(new ArchivedBossRecord
					{
						ArchivedRecordId = _nextArchivedBossRecordId++,
						TargetId = item.TargetId,
						BossMobCode = item.MobCode,
						TargetName = (string.IsNullOrWhiteSpace(item.Name) ? $"#{item.TargetId}" : item.Name),
						DungeonText = ResolveEncounterDungeonText(item.MobCode, "던전 정보 없음"),
						LocalPlayerDpsText = FormatLocalPlayerDps(combatSnapshot),
						DisplayTimeLocal = combatSnapshot.SessionStartUtc.ToLocalTime(),
						Snapshot = combatSnapshot,
						UiActors = CloneTargetUiActors(item.TargetId)
					});
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private void ArchiveDefeatedBossForDisplay(int targetId, string targetName, int bossMobCode)
	{
		try
		{
			CombatSnapshot combatSnapshot = _engine.BuildSnapshotForTarget(targetId);
			if (combatSnapshot == null || combatSnapshot.TopTargetDamage <= 0 || combatSnapshot.Actors.Count == 0)
			{
				return;
			}
			ArchivedBossRecord archivedBossRecord = new ArchivedBossRecord
			{
				ArchivedRecordId = _nextArchivedBossRecordId++,
				TargetId = targetId,
				BossMobCode = bossMobCode,
				TargetName = (string.IsNullOrWhiteSpace(targetName) ? combatSnapshot.TopTargetName : targetName),
				DungeonText = ResolveEncounterDungeonText(bossMobCode, "던전 정보 없음"),
				LocalPlayerDpsText = FormatLocalPlayerDps(combatSnapshot),
				DisplayTimeLocal = combatSnapshot.SessionStartUtc.ToLocalTime(),
				Snapshot = combatSnapshot,
				UiActors = CloneTargetUiActors(targetId)
			};
			TargetFilterOption selectedTargetFilterOption = GetSelectedTargetFilterOption();
			bool flag = (!_autoBossFilter || _autoBossTargetId != targetId) && selectedTargetFilterOption.Kind == TargetFilterItemKind.LiveBoss && selectedTargetFilterOption.TargetId == targetId;
			AddArchivedBossRecords(new ArchivedBossRecord[1] { archivedBossRecord });
			ArchivedBossRecord archivedBossRecord2 = FindArchivedBossRecord(targetId, combatSnapshot);
			PopulateTargetCombo();
			if (flag && archivedBossRecord2 != null && TrySelectArchivedBossRecord(archivedBossRecord2.ArchivedRecordId))
			{
				RenderTiles(archivedBossRecord2.Snapshot);
				if (IsCombatDetailWindowOpen())
				{
					RenderDetailForCurrentEncounter();
				}
			}
		}
		catch
		{
		}
	}

	private void HandleBossHpReset(int targetId, string targetName)
	{
		_lastAutoResetBossTargetId = 0;
		PopulateTargetCombo();
		if (_autoBossFilter)
		{
			SelectAutoBossTarget(targetId);
		}
		RenderTiles(GetSnapshotForCurrentFilter());
		string text = (string.IsNullOrWhiteSpace(targetName) ? $"#{targetId}" : targetName);
		ShowSystemBalloon(text + " HP 리셋 후 새 전투를 시작합니다.");
	}

	private void ClearUiDamageForTarget(int targetId)
	{
		lock (_sync)
		{
			ClearUiDamageForTargetLocked(targetId);
		}
	}

	private void ClearUiDamageForTargetLocked(int targetId)
	{
		foreach (UiActorState value in _uiActors.Values)
		{
			value.RemoveTargetEvents(targetId);
		}
	}

	private void ResetUiForAutoReset(IReadOnlyList<ArchivedBossRecord> archived)
	{
		AddArchivedBossRecords(archived);
		ClearUI(preserveDpsSelection: true);
		_isLogViewMode = false;
		SetLiveBossEncounterFallback(syncCombo: false);
		borderTopTarget.Visibility = Visibility.Collapsed;
		PopulateTargetCombo();
		if (_archivedBossRecords.Count > 0)
		{
			TrySelectArchivedBossRecord(_archivedBossRecords[0].ArchivedRecordId);
		}
		else
		{
			SetSelectedTargetFilterOption(new TargetFilterOption
			{
				Kind = TargetFilterItemKind.All
			});
		}
		UpdatePauseButtonUI();
		UpdateLoadLogButtonUI();
	}

	private void ResetCurrentSession(bool archiveCurrentBosses, bool clearArchivedHistory, bool startNewLog, bool preferLatestArchivedSelection, bool preserveDpsSelection = false)
	{
		CancelEncounterReplay();
		List<ArchivedBossRecord> records = (archiveCurrentBosses ? CaptureArchivedBossRecords() : new List<ArchivedBossRecord>());
		if (clearArchivedHistory)
		{
			_archivedBossRecords.Clear();
			_nextArchivedBossRecordId = 1;
		}
		AddArchivedBossRecords(records);
		ClearUI(preserveDpsSelection);
		_engine.ResetSession(startNewLog);
		_isLogViewMode = false;
		SetLiveBossEncounterFallback(syncCombo: false);
		_lastAutoResetBossTargetId = 0;
		borderTopTarget.Visibility = Visibility.Collapsed;
		PopulateTargetCombo();
		if (!clearArchivedHistory && preferLatestArchivedSelection && _archivedBossRecords.Count > 0)
		{
			if (!TrySelectArchivedBossRecord(_archivedBossRecords[0].ArchivedRecordId))
			{
				SetSelectedTargetFilterOption(new TargetFilterOption
				{
					Kind = TargetFilterItemKind.All
				});
			}
		}
		else
		{
			SetSelectedTargetFilterOption(new TargetFilterOption
			{
				Kind = TargetFilterItemKind.All
			});
		}
		UpdatePauseButtonUI();
		UpdateLoadLogButtonUI();
	}

	private void ClearArchivedBossHistory()
	{
		ResetCurrentSession(archiveCurrentBosses: false, clearArchivedHistory: true, startNewLog: true, preferLatestArchivedSelection: false);
	}

	private void AddArchivedBossRecords(IEnumerable<ArchivedBossRecord> records)
	{
		bool flag = false;
		foreach (ArchivedBossRecord record in records.OrderBy((ArchivedBossRecord r) => r.Snapshot.LastEventUtc))
		{
			int num = _archivedBossRecords.FindIndex((ArchivedBossRecord existing) => IsSameArchivedBossPull(existing, record));
			if (num >= 0)
			{
				if (IsArchivedBossRecordMoreComplete(record, _archivedBossRecords[num]))
				{
					_archivedBossRecords[num] = CopyArchivedBossRecord(record, _archivedBossRecords[num].ArchivedRecordId);
					flag = true;
				}
			}
			else
			{
				_archivedBossRecords.Insert(0, record);
				flag = true;
			}
		}
		if (flag)
		{
			_archivedBossRecords.Sort((ArchivedBossRecord a, ArchivedBossRecord b) => b.Snapshot.LastEventUtc.CompareTo(a.Snapshot.LastEventUtc));
		}
	}

	private static ArchivedBossRecord CopyArchivedBossRecord(ArchivedBossRecord source, int archivedRecordId)
	{
		return new ArchivedBossRecord
		{
			ArchivedRecordId = archivedRecordId,
			TargetId = source.TargetId,
			BossMobCode = source.BossMobCode,
			TargetName = source.TargetName,
			DungeonText = source.DungeonText,
			LocalPlayerDpsText = source.LocalPlayerDpsText,
			SourceFullPath = source.SourceFullPath,
			DisplayTimeLocal = source.DisplayTimeLocal,
			Snapshot = source.Snapshot,
			UiActors = source.UiActors
		};
	}

	private static bool IsArchivedBossRecordMoreComplete(ArchivedBossRecord candidate, ArchivedBossRecord existing)
	{
		if (candidate.Snapshot.TopTargetDamage != existing.Snapshot.TopTargetDamage)
		{
			return candidate.Snapshot.TopTargetDamage > existing.Snapshot.TopTargetDamage;
		}
		if (candidate.Snapshot.TopTargetHits != existing.Snapshot.TopTargetHits)
		{
			return candidate.Snapshot.TopTargetHits > existing.Snapshot.TopTargetHits;
		}
		int num = CountArchivedDetailEvents(candidate);
		int num2 = CountArchivedDetailEvents(existing);
		if (num != num2)
		{
			return num > num2;
		}
		bool flag = !string.IsNullOrWhiteSpace(candidate.SourceFullPath);
		bool flag2 = !string.IsNullOrWhiteSpace(existing.SourceFullPath);
		if (flag != flag2)
		{
			return flag;
		}
		return candidate.Snapshot.LastEventUtc > existing.Snapshot.LastEventUtc;
	}

	private static int CountArchivedDetailEvents(ArchivedBossRecord record)
	{
		return record.UiActors.Values.Sum((UiActorState actor) => actor.Recent.Count + actor.BuffEvents.Count);
	}

	private static bool HasArchivedDetailEvents(ArchivedBossRecord record)
	{
		return CountArchivedDetailEvents(record) > 0;
	}

	private static bool IsSameArchivedBossPull(ArchivedBossRecord a, ArchivedBossRecord b)
	{
		if (!string.IsNullOrWhiteSpace(a.SourceFullPath) && !string.IsNullOrWhiteSpace(b.SourceFullPath) && string.Equals(NormalizeLogPath(a.SourceFullPath), NormalizeLogPath(b.SourceFullPath), StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (a.TargetId == b.TargetId && a.Snapshot.SessionStartUtc == b.Snapshot.SessionStartUtc)
		{
			return true;
		}
		string text = NormalizeBossRecordName(a.TargetName);
		string text2 = NormalizeBossRecordName(b.TargetName);
		if (text.Length == 0 || text2.Length == 0 || !string.Equals(text, text2, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		double num = Math.Abs((a.Snapshot.SessionStartUtc - b.Snapshot.SessionStartUtc).TotalSeconds);
		double num2 = Math.Abs((a.Snapshot.LastEventUtc - b.Snapshot.LastEventUtc).TotalSeconds);
		if (num <= 3.0)
		{
			return num2 <= 5.0;
		}
		return false;
	}

	private static string NormalizeBossRecordName(string name)
	{
		if (!string.IsNullOrWhiteSpace(name))
		{
			return Regex.Replace(name.Trim(), "\\s+", " ");
		}
		return "";
	}

	private ArchivedBossRecord? FindArchivedBossRecord(int targetId, CombatSnapshot snapshot)
	{
		return _archivedBossRecords.FirstOrDefault((ArchivedBossRecord r) => r.TargetId == targetId && r.Snapshot.SessionStartUtc == snapshot.SessionStartUtc && r.Snapshot.LastEventUtc == snapshot.LastEventUtc) ?? (from r in _archivedBossRecords
			where r.TargetId == targetId
			orderby r.Snapshot.LastEventUtc descending
			select r).FirstOrDefault();
	}

	private ArchivedBossRecord? FindArchivedBossRecordBySourcePath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		string normalized = NormalizeLogPath(path);
		return _archivedBossRecords.FirstOrDefault((ArchivedBossRecord record) => !string.IsNullOrWhiteSpace(record.SourceFullPath) && string.Equals(NormalizeLogPath(record.SourceFullPath), normalized, StringComparison.OrdinalIgnoreCase));
	}

	private bool HasArchivedSnapshotForLiveTarget(TargetInfo target)
	{
		return _archivedBossRecords.Any((ArchivedBossRecord r) => r.TargetId == target.TargetId && r.Snapshot.LastEventUtc >= target.LastHit.AddMilliseconds(-250.0));
	}

	private Dictionary<int, UiActorState> CloneTargetUiActors(int targetId)
	{
		Dictionary<int, UiActorState> dictionary = new Dictionary<int, UiActorState>();
		lock (_sync)
		{
			foreach (KeyValuePair<int, UiActorState> entry in _uiActors)
			{
				if (!entry.Value.Targets.TryGetValue(targetId, out UiActorTargetState value) || value.TotalDamage <= 0)
				{
					continue;
				}
				UiActorState uiActorState = entry.Value.CloneForTargetDetail(targetId);
				DateTime firstUtc = value.FirstUtc;
				DateTime lastUtc = value.LastUtc;
				foreach (UiBuffEvent item in DeduplicateBuffEvents(entry.Value.BuffEvents.Concat(_allBuffEvents.Where((UiBuffEvent b) => IsBuffEventRelatedToActor(b, entry.Key)))))
				{
					(DateTime, DateTime) interval = BuffIntervalUtilities.GetInterval(item.TimestampUtc, item.DurationMs, item.StartedAtMs, item.ExpiresAtMs);
					if (interval.Item2 > firstUtc && interval.Item1 < lastUtc)
					{
						uiActorState.ApplyBuff(item);
					}
				}
				dictionary[entry.Key] = uiActorState;
			}
			return dictionary;
		}
	}

	private static string BuildBossFilterLabel(string targetName, DateTime timeLocal)
	{
		string value = (string.IsNullOrWhiteSpace(targetName) ? "이름 없는 보스" : targetName);
		return $"\ud83d\udc51 {value} {timeLocal:HH:mm}";
	}

	private static string GetTargetFilterKey(TargetFilterOption option)
	{
		return option.Kind switch
		{
			TargetFilterItemKind.All => "all", 
			TargetFilterItemKind.LiveBoss => $"live:{option.TargetId}", 
			TargetFilterItemKind.ArchivedBoss => $"arch:{option.ArchivedRecordId}", 
			TargetFilterItemKind.ClearHistory => "clear", 
			_ => "all", 
		};
	}

	private static bool TryGetTargetFilterOption(object? tag, out TargetFilterOption option)
	{
		if (tag is TargetFilterOption targetFilterOption)
		{
			option = targetFilterOption;
			return true;
		}
		if (tag is int num)
		{
			option = new TargetFilterOption
			{
				Kind = ((num != 0) ? TargetFilterItemKind.LiveBoss : TargetFilterItemKind.All),
				TargetId = num
			};
			return true;
		}
		if (tag is string s && int.TryParse(s, out var result))
		{
			option = new TargetFilterOption
			{
				Kind = ((result != 0) ? TargetFilterItemKind.LiveBoss : TargetFilterItemKind.All),
				TargetId = result
			};
			return true;
		}
		option = new TargetFilterOption
		{
			Kind = TargetFilterItemKind.All
		};
		return false;
	}

	private TargetFilterOption GetSelectedTargetFilterOption()
	{
		return _selectedTargetFilterOption;
	}

	private bool SetSelectedTargetFilterOption(TargetFilterOption option, bool syncCombo = true)
	{
		if (option.Kind == TargetFilterItemKind.ClearHistory)
		{
			return false;
		}
		TargetFilterOption targetFilterOption = NormalizeTargetFilterOption(option);
		return targetFilterOption.Kind switch
		{
			TargetFilterItemKind.LiveBoss => SetLiveBossEncounter(targetFilterOption.TargetId, syncCombo), 
			TargetFilterItemKind.ArchivedBoss => SetArchivedEncounter(targetFilterOption.ArchivedRecordId, syncCombo), 
			_ => SetLiveBossEncounterFallback(syncCombo), 
		};
	}

	private bool SetLiveBossEncounter(int targetId, bool syncCombo = true)
	{
		if (targetId <= 0 || !_engine.IsConfirmedBossTarget(targetId))
		{
			return false;
		}
		TargetFilterOption targetFilterOption = new TargetFilterOption
		{
			Kind = TargetFilterItemKind.LiveBoss,
			TargetId = targetId
		};
		_encounterViewKind = EncounterViewKind.LiveBoss;
		_activeBossTargetId = targetId;
		_selectedArchivedBossRecordId = 0;
		_autoBossTargetId = targetId;
		_selectedTargetFilterOption = targetFilterOption;
		if (syncCombo)
		{
			SyncTargetComboSelection(GetTargetFilterKey(targetFilterOption));
		}
		return true;
	}

	private bool SetLiveBossEncounterFallback(bool syncCombo = true)
	{
		TargetFilterOption targetFilterOption = new TargetFilterOption
		{
			Kind = TargetFilterItemKind.All
		};
		_encounterViewKind = EncounterViewKind.LiveBoss;
		_activeBossTargetId = 0;
		_selectedArchivedBossRecordId = 0;
		_autoBossTargetId = 0;
		_selectedTargetFilterOption = targetFilterOption;
		if (syncCombo)
		{
			SyncTargetComboSelection(GetTargetFilterKey(targetFilterOption));
		}
		return true;
	}

	private bool SetArchivedEncounter(int archivedRecordId, bool syncCombo = true)
	{
		ArchivedBossRecord archivedBossRecord = _archivedBossRecords.FirstOrDefault((ArchivedBossRecord r) => r.ArchivedRecordId == archivedRecordId);
		if (archivedBossRecord == null)
		{
			return false;
		}
		TargetFilterOption targetFilterOption = new TargetFilterOption
		{
			Kind = TargetFilterItemKind.ArchivedBoss,
			TargetId = archivedBossRecord.TargetId,
			ArchivedRecordId = archivedBossRecord.ArchivedRecordId
		};
		_encounterViewKind = EncounterViewKind.ArchivedBoss;
		_selectedArchivedBossRecordId = archivedBossRecord.ArchivedRecordId;
		_activeBossTargetId = 0;
		_autoBossTargetId = 0;
		_selectedTargetFilterOption = targetFilterOption;
		if (syncCombo)
		{
			SyncTargetComboSelection(GetTargetFilterKey(targetFilterOption));
		}
		return true;
	}

	private static TargetFilterOption NormalizeTargetFilterOption(TargetFilterOption option)
	{
		switch (option.Kind)
		{
		case TargetFilterItemKind.LiveBoss:
			if (option.TargetId > 0)
			{
				return new TargetFilterOption
				{
					Kind = TargetFilterItemKind.LiveBoss,
					TargetId = option.TargetId
				};
			}
			break;
		case TargetFilterItemKind.ArchivedBoss:
			if (option.ArchivedRecordId > 0)
			{
				return new TargetFilterOption
				{
					Kind = TargetFilterItemKind.ArchivedBoss,
					TargetId = option.TargetId,
					ArchivedRecordId = option.ArchivedRecordId
				};
			}
			break;
		}
		return new TargetFilterOption
		{
			Kind = TargetFilterItemKind.All
		};
	}

	private bool SyncTargetComboSelection(string selectedKey)
	{
		if (cmbFilterTarget == null)
		{
			return false;
		}
		_isUpdatingTargetCombo = true;
		try
		{
			for (int i = 0; i < cmbFilterTarget.Items.Count; i++)
			{
				if (cmbFilterTarget.Items[i] is ComboBoxItem comboBoxItem && TryGetTargetFilterOption(comboBoxItem.Tag, out TargetFilterOption option) && string.Equals(GetTargetFilterKey(option), selectedKey, StringComparison.Ordinal))
				{
					cmbFilterTarget.SelectedIndex = i;
					return true;
				}
			}
		}
		finally
		{
			_isUpdatingTargetCombo = false;
		}
		return false;
	}

	private ArchivedBossRecord? GetSelectedArchivedBossRecord()
	{
		if (_encounterViewKind != EncounterViewKind.ArchivedBoss || _selectedArchivedBossRecordId <= 0)
		{
			return null;
		}
		return _archivedBossRecords.FirstOrDefault((ArchivedBossRecord r) => r.ArchivedRecordId == _selectedArchivedBossRecordId);
	}

	private bool TrySelectArchivedBossRecord(int archivedRecordId)
	{
		ArchivedBossRecord archivedBossRecord = _archivedBossRecords.FirstOrDefault((ArchivedBossRecord r) => r.ArchivedRecordId == archivedRecordId);
		if (archivedBossRecord == null)
		{
			return false;
		}
		return SetSelectedTargetFilterOption(new TargetFilterOption
		{
			Kind = TargetFilterItemKind.ArchivedBoss,
			TargetId = archivedBossRecord.TargetId,
			ArchivedRecordId = archivedRecordId
		});
	}

	private bool TargetComboMatches(IReadOnlyList<TargetFilterEntry> desiredEntries)
	{
		if (cmbFilterTarget.Items.Count != desiredEntries.Count)
		{
			return false;
		}
		for (int i = 0; i < desiredEntries.Count; i++)
		{
			if (!(cmbFilterTarget.Items[i] is ComboBoxItem comboBoxItem))
			{
				return false;
			}
			if (!TryGetTargetFilterOption(comboBoxItem.Tag, out TargetFilterOption option))
			{
				return false;
			}
			if (!string.Equals(comboBoxItem.Content?.ToString(), desiredEntries[i].Label, StringComparison.Ordinal) || !string.Equals(GetTargetFilterKey(option), GetTargetFilterKey(desiredEntries[i].Option), StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	private void BtnHome_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo(WebEndpoint.Url("/"))
			{
				UseShellExecute = true
			});
		}
		catch
		{
		}
	}

	private void LookupCharacterLink_Click(object sender, RoutedEventArgs e)
	{
		if (sender is System.Windows.Controls.Button { Tag: PartyMemberItem tag } && !string.IsNullOrWhiteSpace(tag.Name) && !string.IsNullOrWhiteSpace(tag.ServerName))
		{
			string fileName = BuildAionIngCharacterPageUrl(tag.ServerName, tag.Name);
			try
			{
				Process.Start(new ProcessStartInfo(fileName)
				{
					UseShellExecute = true
				});
			}
			catch
			{
				ShowSystemBalloon("아이온잉 캐릭터 페이지를 열 수 없습니다.");
			}
			e.Handled = true;
		}
	}

	private static string BuildAionIngCharacterPageUrl(string serverName, string characterName)
	{
		return WebEndpoint.Url("/aion2/character.php?server=" + Uri.EscapeDataString(serverName.Trim()) + "&name=" + Uri.EscapeDataString(characterName.Trim()));
	}

	private void StartAutoUpdateCheck()
	{
		if (_updateCheckTimer == null)
		{
			_updateCheckTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMinutes(10L)
			};
			_updateCheckTimer.Tick += async delegate
			{
				await RefreshUpdateIndicatorAsync(showBalloonOnNewVersion: true);
			};
			_updateCheckTimer.Start();
			RunInitialUpdateNoticeAsync();
		}
	}

	private async Task RunInitialUpdateNoticeAsync()
	{
		await Task.Delay(2500);
		await RefreshUpdateIndicatorAsync(showBalloonOnNewVersion: true);
	}

	private async Task RefreshUpdateIndicatorAsync(bool showBalloonOnNewVersion)
	{
		try
		{
			await _updateService.CheckAsync(notifyWhenCurrent: false);
			await base.Dispatcher.BeginInvoke((Action)delegate
			{
				bool flag = IsDeveloperUpdatePreviewEnabled();
				AppUpdateState state = _updateService.State;
				if (state.IsUpdateAvailable)
				{
					bool flag2 = !string.Equals(_latestAvailableVersion, state.LatestVersion, StringComparison.OrdinalIgnoreCase);
					_latestAvailableVersion = state.LatestVersion;
					SetUpdateBadgeVisibility(Visibility.Visible, state.IsReadyToInstall ? ("v" + state.LatestVersion + " 설치 및 재시작") : ("새 버전 v" + state.LatestVersion + " 다운로드"));
					UpdateUpdateButtonVisual();
					if (showBalloonOnNewVersion && (flag2 || !_updateNotificationShown))
					{
						_updateNotificationShown = true;
						ShowSystemBalloon("새 버전 v" + state.LatestVersion + " 업데이트가 있습니다. 다운로드 버튼을 눌러주세요.");
					}
				}
				else
				{
					SetUpdateBadgeVisibility((!flag) ? Visibility.Collapsed : Visibility.Visible, flag ? "개발자 미리보기: 업데이트 배지" : "새 버전이 있습니다");
					UpdateUpdateButtonVisual();
					_latestAvailableVersion = null;
					_updateNotificationShown = false;
				}
			});
		}
		catch
		{
		}
	}

	private bool IsDeveloperUpdatePreviewEnabled()
	{
		return WebEndpoint.IsDeveloperSecurityKey(_devKey);
	}

	private void ApplyDeveloperWebEndpoint()
	{
		bool useTestHost = WebEndpoint.UseTestHost;
		WebEndpoint.SetDeveloperSecurityKey(_devKey);
		_updateService.RefreshFeedUrl();
		if (useTestHost != WebEndpoint.UseTestHost)
		{
			ClearAionIngWebCaches();
		}
	}

	private void ClearAionIngWebCaches()
	{
		lock (_combatScoreCache)
		{
			_combatScoreCache.Clear();
			_combatScoreDungeonScopeCache.Clear();
			_meterPresenceCacheUtc.Clear();
			_combatScoreLoading.Clear();
			_combatScoreAutoRequestedThisSession.Clear();
		}
	}

	private void ApplyUpdateBadgeVisibility()
	{
		if (btnUpdateBadge != null || btnUpdateBadgeHud != null)
		{
			bool flag = IsDeveloperUpdatePreviewEnabled();
			AppUpdateState state = _updateService.State;
			if (state.IsUpdateAvailable)
			{
				SetUpdateBadgeVisibility(Visibility.Visible, state.IsReadyToInstall ? ("v" + state.LatestVersion + " 설치 및 재시작") : ("새 버전 v" + state.LatestVersion + " 다운로드"));
			}
			else
			{
				SetUpdateBadgeVisibility((!flag) ? Visibility.Collapsed : Visibility.Visible, flag ? "개발자 미리보기: 업데이트 배지" : "새 버전이 있습니다");
			}
			UpdateUpdateButtonVisual();
		}
	}

	private void SetUpdateBadgeVisibility(Visibility visibility, string toolTip)
	{
		if (btnUpdateBadge != null)
		{
			btnUpdateBadge.Visibility = visibility;
			btnUpdateBadge.ToolTip = toolTip;
		}
		if (btnUpdateBadgeHud != null)
		{
			btnUpdateBadgeHud.Visibility = visibility;
			btnUpdateBadgeHud.ToolTip = toolTip;
		}
	}

	private void SetUpdateBadgeEnabled(bool enabled)
	{
		if (btnUpdateBadge != null)
		{
			btnUpdateBadge.IsEnabled = enabled;
		}
		if (btnUpdateBadgeHud != null)
		{
			btnUpdateBadgeHud.IsEnabled = enabled;
		}
	}

	private void SetUpdateBadgeToolTip(string toolTip)
	{
		if (btnUpdateBadge != null)
		{
			btnUpdateBadge.ToolTip = toolTip;
		}
		if (btnUpdateBadgeHud != null)
		{
			btnUpdateBadgeHud.ToolTip = toolTip;
		}
	}

	private void SetUpdateProgressText(string text)
	{
		if (txtUpdateProgress != null)
		{
			txtUpdateProgress.Text = text;
		}
		if (txtUpdateProgressHud != null)
		{
			txtUpdateProgressHud.Text = text;
		}
	}

	private void SetUpdateIconOpacity(double opacity)
	{
		if (pathUpdateArrow != null)
		{
			pathUpdateArrow.Opacity = opacity;
		}
		if (pathUpdateTray != null)
		{
			pathUpdateTray.Opacity = opacity;
		}
		if (pathUpdateArrowHud != null)
		{
			pathUpdateArrowHud.Opacity = opacity;
		}
		if (pathUpdateTrayHud != null)
		{
			pathUpdateTrayHud.Opacity = opacity;
		}
	}

	private void UpdateUpdateButtonVisual()
	{
		AppUpdateState state = _updateService.State;
		if (pathUpdateArrow != null && pathUpdateTray != null && txtUpdateProgress != null)
		{
			SetUpdateProgressText("");
			SetUpdateBadgeEnabled(enabled: true);
			SetUpdateIconOpacity(1.0);
			SetUpdateBadgeAttention(enabled: false);
			if (state.IsDownloading)
			{
				SetUpdateProgressText($"{state.DownloadProgress}%");
				SetUpdateBadgeToolTip($"업데이트 다운로드 중 {state.DownloadProgress}%");
			}
			else if (state.IsChecking)
			{
				SetUpdateProgressText("...");
				SetUpdateIconOpacity(0.45);
				SetUpdateBadgeToolTip("업데이트 확인 중");
			}
			else if (state.IsReadyToInstall)
			{
				SetUpdateProgressText("OK");
				SetUpdateBadgeToolTip("v" + state.LatestVersion + " 설치 및 재시작");
				SetUpdateBadgeAttention(enabled: true);
			}
			else if (state.IsUpdateAvailable)
			{
				SetUpdateBadgeAttention(enabled: true);
			}
		}
	}

	private void SetUpdateBadgeAttention(bool enabled)
	{
		SetUpdateBadgeAttention(updateBadgeGlow, enabled);
		SetUpdateBadgeAttention(updateBadgeGlowHud, enabled);
	}

	private static void SetUpdateBadgeAttention(DropShadowEffect? glow, bool enabled)
	{
		if (glow != null)
		{
			if (!enabled)
			{
				glow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
				glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
				glow.Opacity = 0.0;
				glow.BlurRadius = 0.0;
				return;
			}
			DoubleAnimation animation = new DoubleAnimation
			{
				From = 0.35,
				To = 0.95,
				Duration = TimeSpan.FromMilliseconds(900L),
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseInOut
				}
			};
			DoubleAnimation animation2 = new DoubleAnimation
			{
				From = 5.0,
				To = 12.0,
				Duration = TimeSpan.FromMilliseconds(900L),
				AutoReverse = true,
				RepeatBehavior = RepeatBehavior.Forever,
				EasingFunction = new SineEase
				{
					EasingMode = EasingMode.EaseInOut
				}
			};
			glow.BeginAnimation(DropShadowEffect.OpacityProperty, animation);
			glow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, animation2);
		}
	}

	private async void BtnUpdate_Click(object sender, RoutedEventArgs e)
	{
		if (_updateService.State.IsDownloading || _updateService.State.IsChecking)
		{
			return;
		}
		if (_updateService.State.IsReadyToInstall)
		{
			_updateService.ApplyAndRestart();
			return;
		}
		await _updateService.CheckAsync(notifyWhenCurrent: true);
		if (_updateService.State.IsUpdateAvailable)
		{
			SetUpdateBadgeEnabled(enabled: false);
			try
			{
				await _updateService.DownloadAsync();
				if (_updateService.State.IsReadyToInstall)
				{
					_updateService.ApplyAndRestart();
					return;
				}
			}
			finally
			{
				SetUpdateBadgeEnabled(enabled: true);
				UpdateUpdateButtonVisual();
			}
		}
		if (!string.IsNullOrWhiteSpace(_updateService.State.Message))
		{
			ShowSystemBalloon(_updateService.State.Message);
		}
		await RefreshUpdateIndicatorAsync(showBalloonOnNewVersion: false);
	}

	private async void Window_Loaded(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(_pendingCaptureBackendFallbackMessage))
		{
			ShowSystemBalloon(_pendingCaptureBackendFallbackMessage);
			_pendingCaptureBackendFallbackMessage = null;
		}
		_captureStartAttempted = true;
		if (ShowCapturePermissionRequiredIfNeeded())
		{
			return;
		}
		try
		{
			await Task.Run(delegate
			{
				_cap.StartAuto();
			});
			_diagnosticPacketCapture.RefreshRequestsAsync();
		}
		catch (Exception ex)
		{
			string message = CreateCaptureStartFailureMessage(ex, _captureBackend);
			ShowCaptureStartFailureMessage(message);
		}
	}

	private void ValidateConfiguredCaptureBackend()
	{
		if (_captureBackend == CaptureBackend.NpcapMirror && !NpcapMirrorCaptureService.TryValidateAvailable(out string message))
		{
			_captureBackend = CaptureBackend.WinDivert;
			_pendingCaptureBackendFallbackMessage = message;
		}
	}

	private void RestartCaptureService()
	{
		IPacketCaptureService cap = _cap;
		DetachCaptureService(cap);
		try
		{
			cap.Stop();
		}
		catch
		{
		}
		try
		{
			cap.Dispose();
		}
		catch
		{
		}
		_cap = CreateCaptureService(_captureBackend);
		AttachCaptureService(_cap);
		IPacketCaptureService capture = _cap;
		CaptureBackend captureBackend = _captureBackend;
		_captureStatusMessage = "Capture restarting";
		_captureStartFailureMessage = null;
		if (!_captureStartAttempted || ShowCapturePermissionRequiredIfNeeded())
		{
			return;
		}
		Task.Run(delegate
		{
			capture.StartAuto();
		}).ContinueWith(delegate(Task task)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				if (task.Exception != null)
				{
					string message = CreateCaptureStartFailureMessage(task.Exception.GetBaseException(), captureBackend);
					ShowCaptureStartFailureMessage(message);
				}
				else
				{
					_diagnosticPacketCapture.RefreshRequestsAsync();
				}
			});
		}, TaskScheduler.Default);
	}

	private bool ShowCapturePermissionRequiredIfNeeded()
	{
		if (_captureBackend != CaptureBackend.WinDivert || IsAdministrator())
		{
			return false;
		}
		ShowCaptureStartFailureMessage("관리자 권한 실행이 승인되지 않았거나 차단되어 WinDivert 캡처를 시작하지 않았습니다.\n\n앱은 계속 사용할 수 있지만 실시간 측정은 시작되지 않습니다.\nPC방 보안 프로그램이 WinDivert를 막는 환경이라면 설정 > 캡처 방식에서 Npcap을 선택해 주세요.");
		return true;
	}

	private void ShowCaptureStartFailureMessage(string message)
	{
		_captureStartFailureMessage = message;
		_captureStatusMessage = message;
		ShowSystemBalloon(message);
		if (!_captureFailureDialogShown)
		{
			_captureFailureDialogShown = true;
			ThemedMessageBox.Show(this, message, "INGMeter 측정 시작 실패", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private static string CreateCaptureStartFailureMessage(Exception ex, CaptureBackend backend)
	{
		string message = ex.Message;
		if (backend == CaptureBackend.NpcapMirror)
		{
			if (NpcapMirrorCaptureService.IsNpcapDependencyFailure(ex))
			{
				return "Npcap이 설치되어 있지 않아 Npcap 캡처를 시작할 수 없습니다.";
			}
			return "Npcap 캡처 시작 실패: " + message;
		}
		if (message.Contains("WinDivert open failed", StringComparison.OrdinalIgnoreCase))
		{
			if (message.Contains("Win32Error=5", StringComparison.OrdinalIgnoreCase) || message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || message.Contains("액세스", StringComparison.OrdinalIgnoreCase))
			{
				return "WinDivert 캡처를 시작하지 못했습니다. 관리자 권한 실행이 승인되지 않았거나 PC방 보안 프로그램이 드라이버 실행을 차단했을 수 있습니다. 앱은 계속 실행되지만 실시간 측정은 시작되지 않습니다. 설정에서 Npcap 캡처 방식을 선택해 볼 수 있습니다.";
			}
			if (message.Contains("Win32Error=577", StringComparison.OrdinalIgnoreCase) || message.Contains("Win32Error=1275", StringComparison.OrdinalIgnoreCase))
			{
				return "WinDivert 캡처를 시작하지 못했습니다. Windows 보안 정책이나 PC방 보안 프로그램이 WinDivert 드라이버 로드를 차단했을 수 있습니다. 앱은 계속 실행되지만 실시간 측정은 시작되지 않습니다. 설정에서 Npcap 캡처 방식을 선택해 볼 수 있습니다.";
			}
			return "패킷 캡처 시작 실패: " + message;
		}
		if (message.Contains("Npcap", StringComparison.OrdinalIgnoreCase))
		{
			return "Npcap 캡처 시작 실패: " + message;
		}
		if (ex is FileNotFoundException)
		{
			return "측정을 시작하지 못했습니다. WinDivert 파일이 앱 폴더에 없습니다. 파일을 복구하거나, 설정에서 Npcap을 선택해 주세요.";
		}
		if (message.Contains("Unable to load DLL", StringComparison.OrdinalIgnoreCase) || message.Contains("DLL", StringComparison.OrdinalIgnoreCase))
		{
			return "측정을 시작하지 못했습니다. WinDivert 파일을 불러오지 못했습니다. 앱을 관리자 권한으로 실행하거나, 설정에서 Npcap을 선택해 주세요.";
		}
		return "패킷 캡처 시작 실패: " + message;
	}

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	private static bool IsOwnWindowProcess(nint hWnd)
	{
		try
		{
			GetWindowThreadProcessId(hWnd, out var lpdwProcessId);
			return lpdwProcessId == (uint)Process.GetCurrentProcess().Id;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsAion2WindowProcess(nint hWnd)
	{
		try
		{
			GetWindowThreadProcessId(hWnd, out var lpdwProcessId);
			if (lpdwProcessId == 0)
			{
				return false;
			}
			using Process process = Process.GetProcessById((int)lpdwProcessId);
			return (process.ProcessName ?? string.Empty).StartsWith("AION2", StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	private bool ShouldHideBecauseAionInactive()
	{
		if (!_showOnlyWhenAionActive || _isSettingsOpen || _isLogViewMode)
		{
			return false;
		}
		nint foregroundWindow = GetForegroundWindow();
		if (foregroundWindow == IntPtr.Zero)
		{
			return false;
		}
		if (!IsAion2WindowProcess(foregroundWindow))
		{
			return !IsOwnWindowProcess(foregroundWindow);
		}
		return false;
	}

	private void UpdateAionActiveVisibility()
	{
		bool flag = ShouldHideBecauseAionInactive();
		if (flag != _hiddenForAionInactive)
		{
			_hiddenForAionInactive = flag;
			base.IsHitTestVisible = !flag;
			if (flag)
			{
				SetWindowMouseTransparent(enabled: true);
			}
			else if (!_isHudMode || !_hudClickThrough)
			{
				SetWindowMouseTransparent(enabled: false);
			}
			ApplyWindowOpacity();
		}
	}

	protected override void OnSourceInitialized(EventArgs e)
	{
		base.OnSourceInitialized(e);
		if (PresentationSource.FromVisual(this) is HwndSource hwndSource)
		{
			hwndSource.AddHook(WndProc);
		}
		ApplyShowInTaskbarPreference();
	}

	[DllImport("user32.dll")]
	private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll", CharSet = CharSet.Auto)]
	private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

	[DllImport("user32.dll")]
	private static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int GetWindowLong(nint hWnd, int nIndex);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

	private void ResetSnapState()
	{
	}

	private void ApplySnapToMovingRect(nint hwnd, ref RECT rect)
	{
		if (!_isDragging || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
		{
			return;
		}
		nint num = MonitorFromWindow(hwnd, 2u);
		if (num == IntPtr.Zero)
		{
			return;
		}
		MONITORINFO lpmi = new MONITORINFO
		{
			cbSize = Marshal.SizeOf(typeof(MONITORINFO))
		};
		if (GetMonitorInfo(num, ref lpmi))
		{
			RECT rcMonitor = lpmi.rcMonitor;
			int num2 = rect.right - rect.left;
			int num3 = rect.bottom - rect.top;
			double num4 = rect.left;
			double num5 = rect.top;
			if (GetCursorPos(out var lpPoint))
			{
				num4 = (double)lpPoint.X - _dragOffsetX;
				num5 = (double)lpPoint.Y - _dragOffsetY;
			}
			double num6 = num4 + (double)num2;
			double num7 = num5 + (double)num3;
			if (Math.Abs(num4 - (double)rcMonitor.left) <= 20.0)
			{
				num4 = rcMonitor.left;
				num6 = num4 + (double)num2;
			}
			else if (Math.Abs(num6 - (double)rcMonitor.right) <= 20.0)
			{
				num6 = rcMonitor.right;
				num4 = num6 - (double)num2;
			}
			if (Math.Abs(num5 - (double)rcMonitor.top) <= 20.0)
			{
				num5 = rcMonitor.top;
				num7 = num5 + (double)num3;
			}
			else if (Math.Abs(num7 - (double)rcMonitor.bottom) <= 20.0)
			{
				num7 = rcMonitor.bottom;
				num5 = num7 - (double)num3;
			}
			rect.left = (int)Math.Round(num4);
			rect.top = (int)Math.Round(num5);
			rect.right = (int)Math.Round(num6);
			rect.bottom = (int)Math.Round(num7);
		}
	}

	private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
	{
		switch (msg)
		{
		case 532:
		case 561:
		{
			bool isDragging = _isDragging;
			_isDragging = true;
			ResetSnapState();
			if (GetCursorPos(out var lpPoint) && GetWindowRect(hwnd, out var lpRect))
			{
				_dragOffsetX = lpPoint.X - lpRect.left;
				_dragOffsetY = lpPoint.Y - lpRect.top;
			}
			if (!isDragging)
			{
				ApplyMainSurfaceOpacity(_autoHideBackground);
			}
			break;
		}
		case 562:
			_isDragging = false;
			ResetSnapState();
			_dragOffsetX = 0.0;
			_dragOffsetY = 0.0;
			ApplyMainSurfaceOpacity(_autoHideBackground);
			break;
		case 534:
			if (lParam != IntPtr.Zero)
			{
				RECT rect = Marshal.PtrToStructure<RECT>(lParam);
				ApplySnapToMovingRect(hwnd, ref rect);
				Marshal.StructureToPtr(rect, lParam, fDeleteOld: false);
			}
			break;
		}
		if (msg == 132)
		{
			RefreshMainResizeBorderHover(hwnd);
		}
		if (msg == 132 && _isHudMode && _hudClickThrough && !IsCursorOverHudControls())
		{
			handled = true;
			return new IntPtr(-1);
		}
		return IntPtr.Zero;
	}

	protected override void OnClosing(CancelEventArgs e)
	{
		if (!_isExitRequested && TryHandleCloseAsTrayOrExit())
		{
			e.Cancel = true;
			return;
		}
		_isClosingBuffTimerWindow = true;
		CaptureBuffTimerWindowPlacement();
		base.OnClosing(e);
	}

	protected override void OnClosed(EventArgs e)
	{
		if (_isHudMode)
		{
			_hudHeight = base.Height;
		}
		_isClosingLocalEncounterHistoryWindow = true;
		try
		{
			_localEncounterHistoryWindow?.Close();
		}
		catch
		{
		}
		_isClosingBuffTimerWindow = true;
		CaptureBuffTimerWindowPlacement();
		try
		{
			_buffTimerWindow?.Close();
		}
		catch
		{
		}
		_hudClickThroughTimer?.Stop();
		_buffTimerPlacementSaveTimer?.Stop();
		SetWindowMouseTransparent(enabled: false);
		SaveConfig();
		_engine.DamageEventParsed -= OnDamageEventParsed;
		_engine.BuffEventParsed -= OnBuffEventParsed;
		_engine.MobSpawnObserved -= _diagnosticPacketCapture.OnMobSpawn;
		_engine.LocalUserInfoObserved -= _diagnosticPacketCapture.OnLocalUserInfo;
		try
		{
			_diagnosticPacketCapture.Dispose();
		}
		catch
		{
		}
		try
		{
			_cap.Dispose();
		}
		catch
		{
		}
		try
		{
			((IDisposable)_engine)?.Dispose();
		}
		catch
		{
		}
		DisposeTrayIcon();
		base.OnClosed(e);
	}

	private bool TryHandleCloseAsTrayOrExit()
	{
		if (_closeButtonBehavior == CloseButtonBehavior.MinimizeToTray)
		{
			HideToTray();
			return true;
		}
		if (_closeButtonBehavior == CloseButtonBehavior.Exit)
		{
			_isExitRequested = true;
			return false;
		}
		switch (ThemedMessageBox.Show(this, "닫기 버튼을 누르면 프로그램을 트레이로 숨길까요?\n\n예: 트레이로 숨기고 계속 측정\n아니요: 프로그램 종료\n취소: 아무 동작 안 함\n\n선택은 다음부터 자동으로 적용됩니다.", "닫기 버튼 동작", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
		{
		case MessageBoxResult.Yes:
			_closeButtonBehavior = CloseButtonBehavior.MinimizeToTray;
			SaveConfig();
			HideToTray();
			return true;
		case MessageBoxResult.No:
			_closeButtonBehavior = CloseButtonBehavior.Exit;
			_isExitRequested = true;
			SaveConfig();
			return false;
		default:
			return true;
		}
	}

	private void HideToTray()
	{
		EnsureTrayIcon();
		CaptureBuffTimerWindowPlacement();
		SaveConfig();
		_detailWindowHiddenToTray = _detailWindow?.IsVisible ?? false;
		if (_detailWindowHiddenToTray)
		{
			_detailWindow?.Hide();
		}
		_localEncounterHistoryHiddenToTray = _localEncounterHistoryWindow?.IsVisible ?? false;
		if (_localEncounterHistoryHiddenToTray)
		{
			_localEncounterHistoryWindow?.Hide();
		}
		_buffTimerHiddenToTray = _buffTimerWindow?.IsVisible ?? false;
		if (_buffTimerHiddenToTray)
		{
			_buffTimerWindow?.Hide();
		}
		Hide();
		UpdateTrayIconVisibility();
	}

	private void ShowFromTray()
	{
		ShowWithoutActivation();
		if (_detailWindowHiddenToTray && _detailWindow != null)
		{
			_detailWindow.Show();
			_detailWindowHiddenToTray = false;
		}
		if (_localEncounterHistoryHiddenToTray && _localEncounterHistoryWindow != null)
		{
			UpdateLocalEncounterHistoryPlacement();
			PositionLocalEncounterHistoryWindow(_localEncounterHistoryWindow);
			_localEncounterHistoryWindow.Show();
			_localEncounterHistoryHiddenToTray = false;
		}
		if (_buffTimerHiddenToTray && _buffTimerWindow != null)
		{
			_buffTimerWindow.Show();
			RefreshBuffTimerWindow(force: true);
			_buffTimerHiddenToTray = false;
		}
		UpdateTrayIconVisibility();
	}

	private void ExitFromTray()
	{
		_isExitRequested = true;
		if (_trayIcon != null)
		{
			_trayIcon.Visible = false;
		}
		Close();
	}

	private void OpenSettingsFromTray()
	{
		ShowFromTray();
		OpenSettingsWindow();
	}

	private void LocateAppFromTray()
	{
		ShowFromTray();
		if (_hiddenForAionInactive)
		{
			_hiddenForAionInactive = false;
			base.IsHitTestVisible = true;
			if (!_isHudMode || !_hudClickThrough)
			{
				SetWindowMouseTransparent(enabled: false);
			}
			ApplyWindowOpacity();
		}
		EnsureWindowVisibleInWorkArea();
		BringWindowForwardForLocate();
		PulseLocateWindowAsync();
	}

	public void LocateFromExternalActivation()
	{
		LocateAppFromTray();
	}

	private void UpdateTrayIconVisibility()
	{
		if (!_isExitRequested)
		{
			bool flag = !_showInTaskbar || !base.IsVisible;
			if (flag)
			{
				EnsureTrayIcon();
			}
			if (_trayIcon != null)
			{
				_trayIcon.Visible = flag;
			}
		}
	}

	private void EnsureWindowVisibleInWorkArea()
	{
		if (base.WindowState != WindowState.Maximized)
		{
			if (base.WindowState == WindowState.Minimized)
			{
				base.WindowState = WindowState.Normal;
			}
			Rect currentMonitorWorkAreaDip = GetCurrentMonitorWorkAreaDip();
			double num = ((base.ActualWidth > 0.0) ? base.ActualWidth : base.Width);
			double num2 = ((base.ActualHeight > 0.0) ? base.ActualHeight : base.Height);
			if (double.IsNaN(num) || num <= 0.0)
			{
				num = ((base.MinWidth > 0.0) ? base.MinWidth : 320.0);
			}
			if (double.IsNaN(num2) || num2 <= 0.0)
			{
				num2 = ((base.MinHeight > 0.0) ? base.MinHeight : 240.0);
			}
			if (base.Left < currentMonitorWorkAreaDip.Right - 80.0 && base.Left + num > currentMonitorWorkAreaDip.Left + 80.0 && base.Top < currentMonitorWorkAreaDip.Bottom - 50.0 && base.Top + num2 > currentMonitorWorkAreaDip.Top + 50.0)
			{
				base.Left = Math.Clamp(base.Left, currentMonitorWorkAreaDip.Left - num + 80.0, currentMonitorWorkAreaDip.Right - 80.0);
				base.Top = Math.Clamp(base.Top, currentMonitorWorkAreaDip.Top, currentMonitorWorkAreaDip.Bottom - 50.0);
			}
			else
			{
				base.Left = currentMonitorWorkAreaDip.Left + Math.Max(0.0, (currentMonitorWorkAreaDip.Width - num) / 2.0);
				base.Top = currentMonitorWorkAreaDip.Top + Math.Max(0.0, (currentMonitorWorkAreaDip.Height - num2) / 2.0);
			}
		}
	}

	private void BringWindowForwardForLocate()
	{
		bool topmost = base.Topmost;
		base.Topmost = true;
		Activate();
		base.Topmost = topmost;
		Focus();
	}

	private async Task PulseLocateWindowAsync()
	{
		int pulseVersion = ++_locatePulseVersion;
		double dimOpacity = Math.Clamp(base.Opacity * 0.55, 0.2, 1.0);
		SolidColorBrush borderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 179, 0));
		locatePulseOverlay.Margin = (_isHudMode ? new Thickness(3.0, 3.0, 3.0, 9.0) : new Thickness(3.0));
		locatePulseOverlay.BorderBrush = borderBrush;
		locatePulseOverlay.BorderThickness = new Thickness(2.0);
		try
		{
			for (int i = 0; i < 3; i++)
			{
				if (pulseVersion != _locatePulseVersion)
				{
					break;
				}
				locatePulseOverlay.Visibility = Visibility.Visible;
				locatePulseOverlay.Opacity = 1.0;
				base.Opacity = 1.0;
				await Task.Delay(140);
				locatePulseOverlay.Opacity = 0.0;
				base.Opacity = dimOpacity;
				await Task.Delay(110);
			}
		}
		finally
		{
			if (pulseVersion == _locatePulseVersion)
			{
				locatePulseOverlay.Visibility = Visibility.Collapsed;
				locatePulseOverlay.Opacity = 1.0;
				ApplyWindowOpacity();
			}
		}
	}

	private void EnsureTrayIcon()
	{
		if (_trayIcon != null)
		{
			return;
		}
		ContextMenuStrip menu = new ContextMenuStrip
		{
			BackColor = System.Drawing.Color.FromArgb(31, 31, 31),
			ForeColor = System.Drawing.Color.FromArgb(245, 245, 245),
			Renderer = new TrayMenuRenderer(),
			ShowImageMargin = false,
			Padding = new Padding(0, 5, 0, 5),
			MinimumSize = new System.Drawing.Size(184, 0)
		};
		menu.Opened += delegate
		{
			ApplyTrayMenuRoundedRegion(menu);
		};
		menu.SizeChanged += delegate
		{
			ApplyTrayMenuRoundedRegion(menu);
		};
		ToolStripMenuItem toolStripMenuItem = CreateTrayMenuItem("열기");
		ToolStripMenuItem toolStripMenuItem2 = CreateTrayMenuItem("앱 위치 확인하기");
		ToolStripMenuItem toolStripMenuItem3 = CreateTrayMenuItem("설정");
		ToolStripMenuItem toolStripMenuItem4 = CreateTrayMenuItem("종료");
		toolStripMenuItem4.Margin = new Padding(0, 0, 0, 5);
		toolStripMenuItem.Click += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(ShowFromTray));
		};
		toolStripMenuItem2.Click += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(LocateAppFromTray));
		};
		toolStripMenuItem3.Click += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(OpenSettingsFromTray));
		};
		toolStripMenuItem4.Click += delegate
		{
			base.Dispatcher.BeginInvoke(new Action(ExitFromTray));
		};
		menu.Items.Add(toolStripMenuItem);
		menu.Items.Add(toolStripMenuItem2);
		menu.Items.Add(CreateTraySeparator());
		menu.Items.Add(toolStripMenuItem3);
		menu.Items.Add(CreateTraySeparator());
		menu.Items.Add(toolStripMenuItem4);
		_trayIcon = new NotifyIcon
		{
			Icon = ResolveTrayIcon(),
			Text = "INGMeter",
			ContextMenuStrip = menu,
			Visible = (!_showInTaskbar || !base.IsVisible)
		};
		_trayIcon.MouseDoubleClick += delegate(object? _, System.Windows.Forms.MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left)
			{
				base.Dispatcher.BeginInvoke(new Action(ShowFromTray));
			}
		};
	}

	private static ToolStripMenuItem CreateTrayMenuItem(string text)
	{
		return new ToolStripMenuItem(text)
		{
			AutoSize = false,
			Width = 184,
			Height = 32,
			BackColor = System.Drawing.Color.FromArgb(31, 31, 31),
			ForeColor = System.Drawing.Color.FromArgb(245, 245, 245),
			Padding = new Padding(0),
			Margin = new Padding(0),
			DisplayStyle = ToolStripItemDisplayStyle.Text,
			TextAlign = ContentAlignment.MiddleLeft,
			TextDirection = ToolStripTextDirection.Horizontal
		};
	}

	private static ToolStripSeparator CreateTraySeparator()
	{
		return new ToolStripSeparator
		{
			Margin = new Padding(0, 4, 0, 4)
		};
	}

	private static void ApplyTrayMenuRoundedRegion(ContextMenuStrip menu)
	{
		if (menu.Width <= 0 || menu.Height <= 0)
		{
			return;
		}
		menu.Region?.Dispose();
		using GraphicsPath path = CreateRoundedRectanglePath(new System.Drawing.Rectangle(0, 0, menu.Width, menu.Height), 10);
		menu.Region = new Region(path);
	}

	private static GraphicsPath CreateRoundedRectanglePath(System.Drawing.Rectangle bounds, int radius)
	{
		int num = Math.Max(1, radius * 2);
		GraphicsPath graphicsPath = new GraphicsPath();
		System.Drawing.Rectangle rect = new System.Drawing.Rectangle(bounds.Left, bounds.Top, num, num);
		graphicsPath.AddArc(rect, 180f, 90f);
		rect.X = bounds.Right - num - 1;
		graphicsPath.AddArc(rect, 270f, 90f);
		rect.Y = bounds.Bottom - num - 1;
		graphicsPath.AddArc(rect, 0f, 90f);
		rect.X = bounds.Left;
		graphicsPath.AddArc(rect, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private Icon ResolveTrayIcon()
	{
		try
		{
			string text = Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrWhiteSpace(text) && File.Exists(text))
			{
				Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(text);
				if (icon != null)
				{
					return icon;
				}
			}
		}
		catch
		{
		}
		return SystemIcons.Application;
	}

	private void DisposeTrayIcon()
	{
		if (_trayIcon == null)
		{
			return;
		}
		try
		{
			_trayIcon.Visible = false;
			_trayIcon.Dispose();
		}
		catch
		{
		}
		finally
		{
			_trayIcon = null;
		}
	}

	private void Timer_Tick(object? sender, EventArgs e)
	{
		RefreshMainResizeBorderHover();
		if (_isDragging)
		{
			return;
		}
		long totalPackets = _cap.TotalPackets;
		bool num = totalPackets > _lastCheckedPackets;
		long num2 = Interlocked.Read(in _parsedDamageEvents);
		long parsedBuffEvents = Interlocked.Read(in _parsedBuffEvents);
		_tickCount++;
		bool flag = _useDummyData;
		if (_tickCount % 4 == 0)
		{
			UpdateAionActiveVisibility();
			TryQueuePresenceHeartbeat();
			RefreshVisibleMeterPresence();
		}
		if (!_isPaused && !_isLogViewMode && !_isEncounterReplayActive)
		{
			bool num3 = num2 != _lastSnapshotDamageEvents;
			bool flag2 = _tickCount % 4 == 0;
			if (num3 || flag2)
			{
				_engine.TryBuildSnapshotNow();
				_lastSnapshotDamageEvents = num2;
				flag = true;
			}
		}
		if (num)
		{
			_idleCounter = 0;
		}
		else
		{
			_idleCounter++;
		}
		bool num4 = !_isLogViewMode;
		string localPlayerName = _engine.LocalPlayerName;
		bool isLocalPlayerLinked = _engine.IsLocalPlayerLinked;
		if (num4 && !isLocalPlayerLinked && !string.IsNullOrWhiteSpace(localPlayerName))
		{
			_engine.TryRelinkLocalPlayer();
			isLocalPlayerLinked = _engine.IsLocalPlayerLinked;
		}
		bool flag3 = _idleCounter > 4;
		string text = BuildStatusTooltipDetail();
		System.Windows.Media.Color color;
		string toolTip;
		if (isLocalPlayerLinked)
		{
			if (flag3)
			{
				color = System.Windows.Media.Color.FromRgb(byte.MaxValue, 179, 0);
				toolTip = "상태: 대기중\n" + localPlayerName + " 연동 완료\n전투 패킷 대기 중" + text;
			}
			else
			{
				color = System.Windows.Media.Color.FromRgb(51, 204, 51);
				toolTip = "상태: 분석중\n" + localPlayerName + " 전투 데이터를 수집하고 있습니다." + text;
			}
		}
		else if (!string.IsNullOrWhiteSpace(localPlayerName))
		{
			color = System.Windows.Media.Color.FromRgb(byte.MaxValue, 179, 0);
			toolTip = "상태: 맵 이동 대기\n" + localPlayerName + " 인식 완료\n전투 연동을 기다리는 중입니다." + text;
		}
		else if (totalPackets > 0)
		{
			color = System.Windows.Media.Color.FromRgb(byte.MaxValue, 179, 0);
			toolTip = "상태: 인식중\n아이온2 패킷 감지 중" + text;
		}
		else
		{
			color = System.Windows.Media.Color.FromRgb(204, 51, 51);
			toolTip = "상태: 대기중\n아이온2 패킷 대기 중" + text;
		}
		UpdateStatusSettingsButtonUI(color, toolTip);
		_lastCheckedPackets = totalPackets;
		if (flag && !_isEncounterReplayActive)
		{
			PopulateTargetCombo();
			CombatSnapshot snap = (_useDummyData ? CreateDummySnapshot() : GetSnapshotForCurrentFilter());
			RenderTiles(snap);
		}
		int currentMapId = _engine.CurrentMapId;
		if (currentMapId > 0)
		{
			_lastMapId = currentMapId;
		}
		if (_buffTimerEnabled)
		{
			RefreshBuffTimerWindow();
		}
		int? num5 = ResolveSelectedDetailActorId();
		if (!_isPaused && num5.HasValue && IsCombatDetailWindowOpen() && _tickCount % 8 == 0 && ShouldQueueAutomaticDetailRender(num5.Value, num2, parsedBuffEvents))
		{
			QueueActorDetailRender(num5.Value);
		}
	}

	private void PopulateTargetCombo()
	{
		try
		{
			IReadOnlyList<TargetInfo> readOnlyList = null;
			if (_encounterViewKind == EncounterViewKind.LiveBoss)
			{
				readOnlyList = _engine.GetAllTargets();
				GetCurrentLiveBossTargetId(readOnlyList);
			}
			string targetFilterKey = GetTargetFilterKey(GetSelectedTargetFilterOption());
			List<TargetFilterEntry> list = new List<TargetFilterEntry>
			{
				new TargetFilterEntry
				{
					Option = new TargetFilterOption
					{
						Kind = TargetFilterItemKind.All
					},
					Label = "전체 대상"
				}
			};
			if (readOnlyList == null)
			{
				readOnlyList = _engine.GetAllTargets();
			}
			List<TargetInfo> list2 = (from t in readOnlyList
				where t.TotalDamage > 0
				orderby t.TotalDamage descending, t.LastHit descending
				select t).ToList();
			HashSet<int> liveTargetIds = new HashSet<int>(list2.Select((TargetInfo t) => t.TargetId));
			foreach (TargetInfo item in list2)
			{
				string targetName = ((string.IsNullOrWhiteSpace(item.Name) || item.Name == item.TargetId.ToString()) ? $"#{item.TargetId}" : item.Name);
				list.Add(new TargetFilterEntry
				{
					Option = new TargetFilterOption
					{
						Kind = TargetFilterItemKind.LiveBoss,
						TargetId = item.TargetId
					},
					Label = BuildBossFilterLabel(targetName, item.FirstHit.ToLocalTime())
				});
			}
			foreach (ArchivedBossRecord item2 in from r in _archivedBossRecords
				where !liveTargetIds.Contains(r.TargetId)
				orderby r.DisplayTimeLocal descending
				select r)
			{
				list.Add(new TargetFilterEntry
				{
					Option = new TargetFilterOption
					{
						Kind = TargetFilterItemKind.ArchivedBoss,
						TargetId = item2.TargetId,
						ArchivedRecordId = item2.ArchivedRecordId
					},
					Label = BuildBossFilterLabel(item2.TargetName, item2.DisplayTimeLocal)
				});
			}
			if (_archivedBossRecords.Count > 0)
			{
				list.Add(new TargetFilterEntry
				{
					Option = new TargetFilterOption
					{
						Kind = TargetFilterItemKind.ClearHistory
					},
					Label = "\ud83d\uddd1 보스 기록 전체 초기화"
				});
			}
			if (TargetComboMatches(list))
			{
				SyncTargetComboSelection(targetFilterKey);
				return;
			}
			_isUpdatingTargetCombo = true;
			cmbFilterTarget.Items.Clear();
			foreach (TargetFilterEntry item3 in list)
			{
				cmbFilterTarget.Items.Add(new ComboBoxItem
				{
					Content = item3.Label,
					Tag = item3.Option
				});
			}
			SyncTargetComboSelection(targetFilterKey);
		}
		catch
		{
		}
		finally
		{
			_isUpdatingTargetCombo = false;
			RefreshLocalEncounterPanelRows();
		}
	}

	private bool IsLocalEncounterPanelOpen()
	{
		Window? localEncounterHistoryWindow = _localEncounterHistoryWindow;
		if (localEncounterHistoryWindow == null || !localEncounterHistoryWindow.IsVisible)
		{
			return popLocalEncounterHistory?.IsOpen ?? false;
		}
		return true;
	}

	private void BtnLocalEncounterHistory_Click(object sender, RoutedEventArgs e)
	{
		ToggleLocalEncounterHistoryPanel();
	}

	private void ToggleLocalEncounterHistoryPanel()
	{
		if (IsLocalEncounterPanelOpen())
		{
			CloseLocalEncounterHistoryPanel();
			return;
		}
		Window window = EnsureLocalEncounterHistoryWindow();
		UpdateLocalEncounterHistoryPlacement();
		PositionLocalEncounterHistoryWindow(window);
		window.Topmost = base.Topmost;
		window.Show();
		window.Activate();
		RefreshLocalEncounterPanelRows();
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			UpdateLocalEncounterHistoryPlacement();
			EnsureLocalEncounterHistoryWindowVisible(window);
			lstLocalEncounterHistory?.Focus();
			Keyboard.Focus(lstLocalEncounterHistory);
		}, DispatcherPriority.Loaded);
		LoadLocalEncounterHistoryRowsAsync();
	}

	private void LocalEncounterHistoryClose_Click(object sender, RoutedEventArgs e)
	{
		CloseLocalEncounterHistoryPanel();
	}

	private void CloseLocalEncounterHistoryPanel()
	{
		Window? localEncounterHistoryWindow = _localEncounterHistoryWindow;
		if (localEncounterHistoryWindow != null && localEncounterHistoryWindow.IsVisible)
		{
			_localEncounterHistoryWindow.Hide();
		}
		if (popLocalEncounterHistory != null)
		{
			popLocalEncounterHistory.IsOpen = false;
		}
		_localEncounterBossSuggestionsOpen = false;
		RefreshLocalEncounterBossSuggestions();
	}

	private Window EnsureLocalEncounterHistoryWindow()
	{
		if (_localEncounterHistoryWindow != null)
		{
			return _localEncounterHistoryWindow;
		}
		if (popLocalEncounterHistory != null)
		{
			popLocalEncounterHistory.IsOpen = false;
			popLocalEncounterHistory.Child = null;
		}
		Window window = new Window
		{
			Title = "전투 기록 : INGMeter",
			Owner = this,
			WindowStartupLocation = WindowStartupLocation.Manual,
			WindowStyle = WindowStyle.None,
			AllowsTransparency = true,
			Background = System.Windows.Media.Brushes.Transparent,
			ShowInTaskbar = false,
			ResizeMode = ResizeMode.NoResize,
			SizeToContent = SizeToContent.WidthAndHeight,
			Topmost = base.Topmost,
			UseLayoutRounding = true,
			SnapsToDevicePixels = true,
			Content = bdLocalEncounterHistoryPanel
		};
		window.PreviewKeyDown += LocalEncounterHistoryWindow_PreviewKeyDown;
		window.Closing += delegate(object? _, CancelEventArgs e)
		{
			if (!_isClosingLocalEncounterHistoryWindow)
			{
				e.Cancel = true;
				CloseLocalEncounterHistoryPanel();
			}
		};
		_localEncounterHistoryWindow = window;
		return window;
	}

	private void LocalEncounterHistoryPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.OriginalSource == sender)
		{
			e.Handled = true;
		}
	}

	private void LocalEncounterHistoryPanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		DependencyObject source = e.OriginalSource as DependencyObject;
		if (!IsEventInside(source, bdLocalEncounterBossSearch) && !IsEventInside(source, lstLocalEncounterBossSuggestions))
		{
			CloseLocalEncounterBossSuggestions();
		}
	}

	private void LocalEncounterHistoryTitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton != MouseButton.Left)
		{
			return;
		}
		Window? localEncounterHistoryWindow = _localEncounterHistoryWindow;
		if (localEncounterHistoryWindow != null && localEncounterHistoryWindow.IsVisible)
		{
			try
			{
				_localEncounterHistoryWindow.DragMove();
			}
			catch
			{
			}
			e.Handled = true;
		}
	}

	private void LocalEncounterHistoryWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			CloseLocalEncounterHistoryPanel();
			e.Handled = true;
			return;
		}
		System.Windows.Controls.TextBox? localEncounterBossSearchTextBox = _localEncounterBossSearchTextBox;
		if (localEncounterBossSearchTextBox == null || !localEncounterBossSearchTextBox.IsKeyboardFocusWithin)
		{
			e.Handled = TryHandleLocalEncounterHistoryNavigationKey(e.Key) || IsSuppressedLocalEncounterHistoryKey(e.Key);
		}
	}

	private void RepositionLocalEncounterHistoryPopup()
	{
		Popup popup = popLocalEncounterHistory;
		if (popup != null && popup.IsOpen)
		{
			double horizontalOffset = popLocalEncounterHistory.HorizontalOffset;
			popLocalEncounterHistory.HorizontalOffset = horizontalOffset + 0.1;
			popLocalEncounterHistory.HorizontalOffset = horizontalOffset;
		}
	}

	private void UpdateLocalEncounterHistoryPlacement()
	{
		if (bdLocalEncounterHistoryPanel != null && rootBorder != null && !(rootBorder.ActualWidth <= 0.0) && !(rootBorder.ActualHeight <= 0.0))
		{
			Rect currentMonitorWorkAreaDip = GetCurrentMonitorWorkAreaDip();
			Rect elementScreenBoundsDip = GetElementScreenBoundsDip(rootBorder);
			double num = bdLocalEncounterHistoryPanel.Width;
			if (double.IsNaN(num) || num <= 0.0)
			{
				num = ((bdLocalEncounterHistoryPanel.ActualWidth > 0.0) ? bdLocalEncounterHistoryPanel.ActualWidth : 374.0);
			}
			double val = Math.Max(300.0, currentMonitorWorkAreaDip.Width - 16.0);
			double num2 = Math.Min(300.0, val);
			double num3 = Math.Min(374.0, val);
			bdLocalEncounterHistoryPanel.MinWidth = num2;
			bdLocalEncounterHistoryPanel.MaxWidth = num3;
			bdLocalEncounterHistoryPanel.Width = Math.Clamp(num, num2, num3);
			Window? localEncounterHistoryWindow = _localEncounterHistoryWindow;
			double num4 = ((localEncounterHistoryWindow != null && localEncounterHistoryWindow.IsVisible) ? _localEncounterHistoryWindow.Top : elementScreenBoundsDip.Top);
			double num5 = Math.Min(720.0, Math.Max(210.0, currentMonitorWorkAreaDip.Bottom - num4 - 8.0));
			bdLocalEncounterHistoryPanel.MaxHeight = num5;
			double num6 = bdLocalEncounterHistoryPanel.Height;
			if (double.IsNaN(num6) || num6 <= 0.0)
			{
				num6 = ((bdLocalEncounterHistoryPanel.ActualHeight > 0.0) ? bdLocalEncounterHistoryPanel.ActualHeight : 390.0);
			}
			bdLocalEncounterHistoryPanel.Height = Math.Clamp(num6, 210.0, num5);
		}
	}

	private void PositionLocalEncounterHistoryWindow(Window window)
	{
		Rect currentMonitorWorkAreaDip = GetCurrentMonitorWorkAreaDip();
		Rect elementScreenBoundsDip = GetElementScreenBoundsDip(rootBorder);
		double localEncounterHistoryPanelWidth = GetLocalEncounterHistoryPanelWidth();
		double localEncounterHistoryPanelHeight = GetLocalEncounterHistoryPanelHeight();
		double max = Math.Max(currentMonitorWorkAreaDip.Left, currentMonitorWorkAreaDip.Right - localEncounterHistoryPanelWidth);
		double max2 = Math.Max(currentMonitorWorkAreaDip.Top, currentMonitorWorkAreaDip.Bottom - localEncounterHistoryPanelHeight);
		double num = elementScreenBoundsDip.Right + 6.0;
		if (num + localEncounterHistoryPanelWidth > currentMonitorWorkAreaDip.Right)
		{
			num = elementScreenBoundsDip.Left - localEncounterHistoryPanelWidth - 6.0;
		}
		if (num < currentMonitorWorkAreaDip.Left)
		{
			num = Math.Clamp(elementScreenBoundsDip.Left, currentMonitorWorkAreaDip.Left, max);
		}
		window.Left = Math.Clamp(num, currentMonitorWorkAreaDip.Left, max);
		window.Top = Math.Clamp(elementScreenBoundsDip.Top, currentMonitorWorkAreaDip.Top, max2);
	}

	private void EnsureLocalEncounterHistoryWindowVisible(Window window)
	{
		Rect currentMonitorWorkAreaDip = GetCurrentMonitorWorkAreaDip();
		double localEncounterHistoryPanelWidth = GetLocalEncounterHistoryPanelWidth();
		double localEncounterHistoryPanelHeight = GetLocalEncounterHistoryPanelHeight();
		window.Left = Math.Clamp(window.Left, currentMonitorWorkAreaDip.Left, Math.Max(currentMonitorWorkAreaDip.Left, currentMonitorWorkAreaDip.Right - localEncounterHistoryPanelWidth));
		window.Top = Math.Clamp(window.Top, currentMonitorWorkAreaDip.Top, Math.Max(currentMonitorWorkAreaDip.Top, currentMonitorWorkAreaDip.Bottom - localEncounterHistoryPanelHeight));
	}

	private void SetBuffTimerEnabled(bool enabled)
	{
		if (_buffTimerEnabled != enabled)
		{
			_buffTimerEnabled = enabled;
			if (enabled)
			{
				OpenBuffTimerWindow();
			}
			else
			{
				CloseBuffTimerWindow();
			}
			SaveConfig();
		}
	}

	private void OpenBuffTimerWindow()
	{
		if (!_buffTimerEnabled)
		{
			return;
		}
		if (_buffTimerWindow != null)
		{
			if (!_buffTimerWindow.IsVisible)
			{
				_buffTimerWindow.Show();
			}
			RefreshBuffTimerWindow(force: true);
			return;
		}
		BuffTimerWindow window = new BuffTimerWindow
		{
			Owner = this,
			Topmost = base.Topmost,
			ShowInTaskbar = false
		};
		window.HideBuffRequested += BuffTimerWindow_HideBuffRequested;
		window.RestoreBuffRequested += BuffTimerWindow_RestoreBuffRequested;
		_buffTimerWindow = window;
		_isBuffTimerWindowMouseTransparent = false;
		ApplyBuffTimerWindowPlacement(window);
		ApplyBuffTimerWindowOpacity();
		ApplyBuffTimerLockedBackgroundState();
		window.LocationChanged += BuffTimerWindow_PlacementChanged;
		window.SizeChanged += BuffTimerWindow_PlacementChanged;
		window.Closed += delegate
		{
			window.LocationChanged -= BuffTimerWindow_PlacementChanged;
			window.SizeChanged -= BuffTimerWindow_PlacementChanged;
			window.HideBuffRequested -= BuffTimerWindow_HideBuffRequested;
			window.RestoreBuffRequested -= BuffTimerWindow_RestoreBuffRequested;
			CaptureBuffTimerWindowPlacement(window);
			if (_buffTimerWindow == window)
			{
				_buffTimerWindow = null;
			}
			_isBuffTimerWindowMouseTransparent = false;
			if (!_isClosingBuffTimerWindow && _buffTimerEnabled)
			{
				_buffTimerEnabled = false;
				SaveConfig();
			}
		};
		window.Show();
		UpdateHudClickThroughState();
		RefreshBuffTimerWindow(force: true);
	}

	private void CloseBuffTimerWindow()
	{
		if (_buffTimerWindow == null)
		{
			return;
		}
		CaptureBuffTimerWindowPlacement();
		try
		{
			_buffTimerWindow.Close();
		}
		catch
		{
			_buffTimerWindow = null;
		}
	}

	private void BuffTimerWindow_HideBuffRequested(object? sender, int buffKey)
	{
		if (buffKey > 0 && _hiddenBuffTimerKeys.Add(buffKey))
		{
			SaveConfig();
			RefreshBuffTimerWindow(force: true);
		}
	}

	private void BuffTimerWindow_RestoreBuffRequested(object? sender, int buffKey)
	{
		if (buffKey > 0 && _hiddenBuffTimerKeys.Remove(buffKey))
		{
			SaveConfig();
			RefreshBuffTimerWindow(force: true);
		}
	}

	private void RefreshBuffTimerWindow(bool force = false)
	{
		if (!_buffTimerEnabled)
		{
			return;
		}
		if (_buffTimerWindow == null)
		{
			OpenBuffTimerWindow();
			return;
		}
		DateTime utcNow = DateTime.UtcNow;
		if (force || !(utcNow - _lastBuffTimerRefreshUtc < BuffTimerRefreshInterval))
		{
			_lastBuffTimerRefreshUtc = utcNow;
			(IReadOnlyList<BuffTimerRow>, IReadOnlyList<BuffTimerRow>) tuple = BuildBuffTimerRows(utcNow);
			_buffTimerWindow.SetRows(tuple.Item1, tuple.Item2);
		}
	}

	private (IReadOnlyList<BuffTimerRow> Visible, IReadOnlyList<BuffTimerRow> Hidden) BuildBuffTimerRows(DateTime now)
	{
		int? localPlayerActorId = _engine.LocalPlayerActorId;
		if (localPlayerActorId.HasValue)
		{
			int valueOrDefault = localPlayerActorId.GetValueOrDefault();
			if (valueOrDefault > 0)
			{
				List<UiBuffEvent> list;
				List<UiBuffEvent> list2;
				lock (_sync)
				{
					PruneActiveBuffEvents(now);
					list = _activeBuffEvents.Values.ToList();
					list2 = _allBuffEvents.ToList();
				}
				List<BuffTimerRow> list3 = new List<BuffTimerRow>();
				List<BuffTimerRow> list4 = new List<BuffTimerRow>();
				HashSet<int> seenBuffKeys = new HashSet<int>();
				foreach (UiBuffEvent item in list)
				{
					if (TryCreateBuffTimerRow(item, valueOrDefault, now, list2, allowExpired: false, out int buffKey, out BuffTimerRow row) && row != null && seenBuffKeys.Add(buffKey))
					{
						AddBuffTimerRow(buffKey, row, list3, list4);
					}
				}
				foreach (IGrouping<int, UiBuffEvent> item2 in from buff in list2
					group buff by (buff.BuffId <= 0) ? buff.SkillId : buff.BuffId into @group
					where @group.Key > 0 && !seenBuffKeys.Contains(@group.Key)
					select @group)
				{
					foreach (UiBuffEvent item3 in item2.OrderByDescending(GetBuffTimerIntervalEnd))
					{
						if (TryCreateBuffTimerRow(item3, valueOrDefault, now, list2, allowExpired: true, out int buffKey2, out BuffTimerRow row2) && row2 != null && seenBuffKeys.Add(buffKey2))
						{
							AddBuffTimerRow(buffKey2, row2, list3, list4);
							break;
						}
					}
				}
				foreach (int item4 in _hiddenBuffTimerKeys.Where((int key) => key > 0 && !seenBuffKeys.Contains(key)))
				{
					if (TryCreateHiddenBuffTimerRow(item4, out BuffTimerRow row3) && row3 != null)
					{
						list4.Add(row3);
					}
				}
				IReadOnlyList<BuffTimerRow> readOnlyList = SortBuffTimerRows(list3);
				IReadOnlyList<BuffTimerRow> readOnlyList2 = SortBuffTimerRows(list4);
				PruneBuffTimerSlotOrders(readOnlyList, readOnlyList2);
				return (Visible: readOnlyList, Hidden: readOnlyList2);
			}
		}
		return (Visible: Array.Empty<BuffTimerRow>(), Hidden: Array.Empty<BuffTimerRow>());
	}

	private void AddBuffTimerRow(int buffKey, BuffTimerRow row, ICollection<BuffTimerRow> visibleRows, ICollection<BuffTimerRow> hiddenRows)
	{
		if (_hiddenBuffTimerKeys.Contains(buffKey))
		{
			hiddenRows.Add(row);
		}
		else
		{
			visibleRows.Add(row);
		}
	}

	private IReadOnlyList<BuffTimerRow> SortBuffTimerRows(IEnumerable<BuffTimerRow> rows)
	{
		return (from row in rows
			orderby GetBuffTimerSlotOrder(row.Key), row.Name
			select row).ToList();
	}

	private int GetBuffTimerSlotOrder(int buffKey)
	{
		if (!_buffTimerSlotOrders.TryGetValue(buffKey, out var value))
		{
			value = _nextBuffTimerSlotOrder++;
			_buffTimerSlotOrders[buffKey] = value;
		}
		return value;
	}

	private void PruneBuffTimerSlotOrders(IEnumerable<BuffTimerRow> visibleRows, IEnumerable<BuffTimerRow> hiddenRows)
	{
		HashSet<int> hashSet = (from row in visibleRows.Concat(hiddenRows)
			select row.Key).ToHashSet();
		foreach (int item in _buffTimerSlotOrders.Keys.ToList())
		{
			if (!hashSet.Contains(item))
			{
				_buffTimerSlotOrders.Remove(item);
			}
		}
		if (_buffTimerSlotOrders.Count == 0)
		{
			_nextBuffTimerSlotOrder = 0;
		}
	}

	private bool TryCreateHiddenBuffTimerRow(int buffKey, out BuffTimerRow? row)
	{
		row = null;
		if (!_buffNames.TryGet(buffKey, out BuffInfo info) || !IsVisiblePlayerBuff(info) || IsConsumableBuff(info))
		{
			return false;
		}
		string text = ((!string.IsNullOrWhiteSpace(info?.Name)) ? info.Name : $"Buff {buffKey}");
		row = new BuffTimerRow
		{
			Key = buffKey,
			IconPath = GetSkillIconPath(buffKey),
			Name = text,
			TimeText = "",
			TooltipText = text + "\nHidden",
			Progress = 0.0,
			IconOpacity = 0.45,
			SortSeconds = double.MaxValue,
			IsExpired = true,
			RingBrush = BuffTimerBrushes.ExpiredRing,
			BadgeBrush = BuffTimerBrushes.ExpiredBadge
		};
		return true;
	}

	private bool TryCreateBuffTimerRow(UiBuffEvent buff, int localActorId, DateTime now, IReadOnlyList<UiBuffEvent> recentBuffs, bool allowExpired, out int buffKey, out BuffTimerRow? row)
	{
		row = null;
		buffKey = ((buff.BuffId > 0) ? buff.BuffId : buff.SkillId);
		if (buffKey <= 0 || !_buffNames.TryGet(buffKey, out BuffInfo info) || !IsVisiblePlayerBuff(info) || IsConsumableBuff(info) || !IsSelfSkillBuffForTimer(buff, localActorId, now, recentBuffs) || !BuffIntervalUtilities.HasInterval(buff.DurationMs, buff.ExpiresAtMs))
		{
			return false;
		}
		(DateTime, DateTime) interval = BuffIntervalUtilities.GetInterval(buff.TimestampUtc, buff.DurationMs, buff.StartedAtMs, buff.ExpiresAtMs);
		double totalSeconds = (interval.Item2 - now).TotalSeconds;
		bool flag = totalSeconds <= 0.0;
		if (!allowExpired && flag)
		{
			return false;
		}
		if (allowExpired && (!flag || now - interval.Item2 > BuffTimerExpiredHold))
		{
			return false;
		}
		double num = Math.Max(1.0, (interval.Item2 - interval.Item1).TotalSeconds);
		double progress = (flag ? 0.0 : Math.Clamp(totalSeconds / num, 0.0, 1.0));
		string text = FormatBuffTimerTime(totalSeconds);
		string text2 = ((!string.IsNullOrWhiteSpace(info?.Name)) ? info.Name : $"Buff {buffKey}");
		string text3 = ((buff.SkillLevel > 0) ? $" Lv.{buff.SkillLevel}" : "");
		(System.Windows.Media.Brush Ring, System.Windows.Media.Brush Badge) tuple = CreateBuffTimerBrushes(totalSeconds, flag);
		System.Windows.Media.Brush item = tuple.Ring;
		System.Windows.Media.Brush item2 = tuple.Badge;
		bool isCritical = !flag && totalSeconds <= 3.0;
		int skillCode = ((buff.SkillId > 0) ? buff.SkillId : buffKey);
		row = new BuffTimerRow
		{
			Key = buffKey,
			IconPath = GetSkillIconPath(skillCode),
			Name = text2,
			TimeText = text,
			TooltipText = (flag ? (text2 + text3 + "\n만료됨") : (text2 + text3 + "\n남은 시간 " + text)),
			Progress = progress,
			IconOpacity = (flag ? 0.45 : 1.0),
			SortSeconds = (flag ? double.MaxValue : Math.Max(0.0, totalSeconds)),
			IsExpired = flag,
			IsCritical = isCritical,
			RingBrush = item,
			BadgeBrush = item2
		};
		return true;
	}

	private bool IsSelfSkillBuffForTimer(UiBuffEvent buff, int localActorId, DateTime now, IReadOnlyList<UiBuffEvent> recentBuffs)
	{
		if (buff.TargetId == localActorId && buff.OwnerId == localActorId)
		{
			return true;
		}
		if (ResolveBuffTimerActorId(buff.TargetId) != localActorId)
		{
			return false;
		}
		int num = ResolveBuffTimerActorId(buff.OwnerId);
		if (num == localActorId)
		{
			return true;
		}
		if (IsPlausibleActorId(num))
		{
			return false;
		}
		int buffKey = ((buff.BuffId > 0) ? buff.BuffId : buff.SkillId);
		return recentBuffs.Any((UiBuffEvent recent) => ((recent.BuffId > 0) ? recent.BuffId : recent.SkillId) == buffKey && recent.Kind.Equals("BuffApplied", StringComparison.OrdinalIgnoreCase) && IsRawOrResolvedSelfBuff(recent, localActorId) && BuffIntervalUtilities.HasInterval(recent.DurationMs, recent.ExpiresAtMs) && BuffIntervalUtilities.GetInterval(recent.TimestampUtc, recent.DurationMs, recent.StartedAtMs, recent.ExpiresAtMs).End >= now - BuffTimerExpiredHold);
	}

	private bool IsRawOrResolvedSelfBuff(UiBuffEvent buff, int localActorId)
	{
		if (buff.TargetId == localActorId && buff.OwnerId == localActorId)
		{
			return true;
		}
		if (ResolveBuffTimerActorId(buff.TargetId) == localActorId)
		{
			return ResolveBuffTimerActorId(buff.OwnerId) == localActorId;
		}
		return false;
	}

	private int ResolveBuffTimerActorId(int actorId)
	{
		if (actorId <= 0)
		{
			return 0;
		}
		return _engine.Names.ResolveActorId(actorId);
	}

	private static bool IsPlausibleActorId(int actorId)
	{
		if (actorId > 0)
		{
			return actorId <= 99999;
		}
		return false;
	}

	private static DateTime GetBuffTimerIntervalEnd(UiBuffEvent buff)
	{
		if (!BuffIntervalUtilities.HasInterval(buff.DurationMs, buff.ExpiresAtMs))
		{
			return buff.TimestampUtc;
		}
		return BuffIntervalUtilities.GetInterval(buff.TimestampUtc, buff.DurationMs, buff.StartedAtMs, buff.ExpiresAtMs).End;
	}

	private static string FormatBuffTimerTime(double remainingSeconds)
	{
		int num = (int)Math.Ceiling(Math.Max(0.0, remainingSeconds));
		if (num >= 3600)
		{
			return $"{num / 3600}:{num % 3600 / 60:00}:{num % 60:00}";
		}
		if (num >= 60)
		{
			return $"{num / 60}:{num % 60:00}";
		}
		return num.ToString(CultureInfo.InvariantCulture);
	}

	private static (System.Windows.Media.Brush Ring, System.Windows.Media.Brush Badge) CreateBuffTimerBrushes(double remainingSeconds, bool expired)
	{
		if (expired)
		{
			return (Ring: BuffTimerBrushes.ExpiredRing, Badge: BuffTimerBrushes.ExpiredBadge);
		}
		if (remainingSeconds <= 3.0)
		{
			return (Ring: BuffTimerBrushes.CriticalRing, Badge: BuffTimerBrushes.CriticalBadge);
		}
		if (remainingSeconds <= 10.0)
		{
			return (Ring: BuffTimerBrushes.WarningRing, Badge: BuffTimerBrushes.WarningBadge);
		}
		return (Ring: BuffTimerBrushes.NormalRing, Badge: BuffTimerBrushes.NormalBadge);
	}

	private void ApplyBuffTimerWindowPlacement(BuffTimerWindow window)
	{
		double num = _buffTimerWidth ?? 204.0;
		double num2 = _buffTimerHeight ?? 110.0;
		double valueOrDefault = _buffTimerLeft.GetValueOrDefault();
		double valueOrDefault2 = _buffTimerTop.GetValueOrDefault();
		int num3;
		Rect rect;
		if (_buffTimerLeft.HasValue && _buffTimerTop.HasValue && !double.IsNaN(valueOrDefault))
		{
			num3 = ((!double.IsNaN(valueOrDefault2)) ? 1 : 0);
			if (num3 != 0)
			{
				rect = GetMonitorWorkAreaDipFromPoint(new System.Windows.Point(valueOrDefault + num / 2.0, valueOrDefault2 + num2 / 2.0));
				goto IL_00a0;
			}
		}
		else
		{
			num3 = 0;
		}
		rect = GetCurrentMonitorWorkAreaDip();
		goto IL_00a0;
		IL_00a0:
		Rect rect2 = rect;
		double num4 = Math.Clamp(num, window.MinWidth, Math.Max(window.MinWidth, rect2.Width));
		double num5 = Math.Clamp(num2, window.MinHeight, Math.Max(window.MinHeight, rect2.Height));
		window.Width = num4;
		window.Height = num5;
		if (num3 != 0)
		{
			window.Left = Math.Clamp(valueOrDefault, rect2.Left, Math.Max(rect2.Left, rect2.Right - num4));
			window.Top = Math.Clamp(valueOrDefault2, rect2.Top, Math.Max(rect2.Top, rect2.Bottom - num5));
		}
		else
		{
			PositionBuffTimerWindow(window);
		}
	}

	private void PositionBuffTimerWindow(Window window)
	{
		Rect currentMonitorWorkAreaDip = GetCurrentMonitorWorkAreaDip();
		Rect elementScreenBoundsDip = GetElementScreenBoundsDip(rootBorder);
		double num = ((window.Width > 0.0) ? window.Width : 204.0);
		double num2 = ((window.Height > 0.0) ? window.Height : 110.0);
		double num3 = 8.0;
		double num4 = elementScreenBoundsDip.Right + num3;
		if (num4 + num > currentMonitorWorkAreaDip.Right)
		{
			num4 = elementScreenBoundsDip.Left - num - num3;
		}
		if (num4 < currentMonitorWorkAreaDip.Left)
		{
			num4 = Math.Clamp(elementScreenBoundsDip.Left, currentMonitorWorkAreaDip.Left, Math.Max(currentMonitorWorkAreaDip.Left, currentMonitorWorkAreaDip.Right - num));
		}
		window.Left = Math.Clamp(num4, currentMonitorWorkAreaDip.Left, Math.Max(currentMonitorWorkAreaDip.Left, currentMonitorWorkAreaDip.Right - num));
		window.Top = Math.Clamp(elementScreenBoundsDip.Top, currentMonitorWorkAreaDip.Top, Math.Max(currentMonitorWorkAreaDip.Top, currentMonitorWorkAreaDip.Bottom - num2));
	}

	private void CaptureBuffTimerWindowPlacement()
	{
		if (_buffTimerWindow != null)
		{
			CaptureBuffTimerWindowPlacement(_buffTimerWindow);
		}
	}

	private void BuffTimerWindow_PlacementChanged(object? sender, EventArgs e)
	{
		if (sender is Window window && window == _buffTimerWindow)
		{
			CaptureBuffTimerWindowPlacement(window);
			ScheduleBuffTimerPlacementSave();
		}
	}

	private void ScheduleBuffTimerPlacementSave()
	{
		if (_buffTimerPlacementSaveTimer == null)
		{
			_buffTimerPlacementSaveTimer = CreateBuffTimerPlacementSaveTimer();
		}
		_buffTimerPlacementSaveTimer.Stop();
		_buffTimerPlacementSaveTimer.Start();
	}

	private DispatcherTimer CreateBuffTimerPlacementSaveTimer()
	{
		DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background)
		{
			Interval = TimeSpan.FromMilliseconds(350L)
		};
		timer.Tick += delegate
		{
			timer.Stop();
			if (_buffTimerWindow != null && !_isClosingBuffTimerWindow)
			{
				CaptureBuffTimerWindowPlacement();
				SaveConfig();
			}
		};
		return timer;
	}

	private void CaptureBuffTimerWindowPlacement(Window window)
	{
		if (window.WindowState == WindowState.Normal)
		{
			if (!double.IsNaN(window.Left))
			{
				_buffTimerLeft = window.Left;
			}
			if (!double.IsNaN(window.Top))
			{
				_buffTimerTop = window.Top;
			}
			double num = ((window.ActualWidth > 0.0) ? window.ActualWidth : window.Width);
			double num2 = ((window.ActualHeight > 0.0) ? window.ActualHeight : window.Height);
			if (!double.IsNaN(num) && num > 0.0)
			{
				_buffTimerWidth = Math.Max(window.MinWidth, num);
			}
			if (!double.IsNaN(num2) && num2 > 0.0)
			{
				_buffTimerHeight = Math.Max(window.MinHeight, num2);
			}
		}
	}

	private double GetLocalEncounterHistoryPanelWidth()
	{
		double num = bdLocalEncounterHistoryPanel?.Width ?? 0.0;
		if (double.IsNaN(num) || num <= 0.0)
		{
			Border border = bdLocalEncounterHistoryPanel;
			num = ((border != null && border.ActualWidth > 0.0) ? bdLocalEncounterHistoryPanel.ActualWidth : 374.0);
		}
		return num;
	}

	private double GetLocalEncounterHistoryPanelHeight()
	{
		double num = bdLocalEncounterHistoryPanel?.Height ?? 0.0;
		if (double.IsNaN(num) || num <= 0.0)
		{
			Border border = bdLocalEncounterHistoryPanel;
			num = ((border != null && border.ActualHeight > 0.0) ? bdLocalEncounterHistoryPanel.ActualHeight : 390.0);
		}
		return num;
	}

	private Rect GetElementScreenBoundsDip(FrameworkElement element)
	{
		System.Windows.Media.Matrix matrix = PresentationSource.FromVisual(element)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
		System.Windows.Point point = matrix.Transform(element.PointToScreen(new System.Windows.Point(0.0, 0.0)));
		System.Windows.Point point2 = matrix.Transform(element.PointToScreen(new System.Windows.Point(element.ActualWidth, element.ActualHeight)));
		return new Rect(point, point2);
	}

	private void LocalEncounterHistoryResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
	{
		if (bdLocalEncounterHistoryPanel != null)
		{
			UpdateLocalEncounterHistoryPlacement();
			double num = bdLocalEncounterHistoryPanel.Height;
			if (double.IsNaN(num) || num <= 0.0)
			{
				num = ((bdLocalEncounterHistoryPanel.ActualHeight > 0.0) ? bdLocalEncounterHistoryPanel.ActualHeight : 390.0);
			}
			double max = ((bdLocalEncounterHistoryPanel.MaxHeight > 0.0) ? bdLocalEncounterHistoryPanel.MaxHeight : 720.0);
			bdLocalEncounterHistoryPanel.Height = Math.Clamp(num + e.VerticalChange, 210.0, max);
			RepositionLocalEncounterHistoryPopup();
		}
	}

	private void LocalEncounterHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_isUpdatingLocalEncounterSelection && lstLocalEncounterHistory?.SelectedItem is LocalEncounterPanelRow row)
		{
			OpenLocalEncounterHistoryRow(row);
		}
	}

	private void LocalEncounterHistory_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt && lstLocalEncounterHistory != null && FindParent<ListBoxItem>(VisualTreeHelper.HitTest(lstLocalEncounterHistory, e.GetPosition(lstLocalEncounterHistory))?.VisualHit)?.DataContext is LocalEncounterPanelRow { IsEncounterRow: not false } localEncounterPanelRow)
		{
			if (lstLocalEncounterHistory.SelectedItem == localEncounterPanelRow)
			{
				OpenLocalEncounterHistoryRow(localEncounterPanelRow);
			}
			else
			{
				lstLocalEncounterHistory.SelectedItem = localEncounterPanelRow;
			}
			e.Handled = true;
		}
	}

	private void OpenLocalEncounterHistoryRow(LocalEncounterPanelRow row)
	{
		lstLocalEncounterHistory.Focus();
		Keyboard.Focus(lstLocalEncounterHistory);
		CancelEncounterReplay();
		CancelQueuedLocalEncounterLogLoad(invalidate: true);
		try
		{
			if (!row.IsEncounterRow)
			{
				return;
			}
			SetMainContentView(MainContentView.Dps, manual: true, force: true);
			if (row.Kind == LocalEncounterPanelRowKind.Stored)
			{
				if (!string.IsNullOrWhiteSpace(row.FullPath))
				{
					QueueLocalEncounterLogLoad(row.FullPath);
				}
			}
			else if ((row.Kind == LocalEncounterPanelRowKind.Live) ? SetSelectedTargetFilterOption(new TargetFilterOption
			{
				Kind = TargetFilterItemKind.LiveBoss,
				TargetId = row.TargetId
			}) : TrySelectArchivedBossRecord(row.ArchivedRecordId))
			{
				PopulateTargetCombo();
				RenderTiles(GetSnapshotForCurrentFilter());
				if (IsCombatDetailWindowOpen())
				{
					RenderDetailForCurrentEncounter();
				}
			}
		}
		catch (Exception ex)
		{
			ShowSystemBalloon("전투 기록을 열 수 없습니다: " + ex.Message);
		}
	}

	private void LocalEncounterHistory_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		e.Handled = TryHandleLocalEncounterHistoryNavigationKey(e.Key) || IsSuppressedLocalEncounterHistoryKey(e.Key);
	}

	private void LocalEncounterBossSuggestion_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (lstLocalEncounterBossSuggestions?.SelectedItem is LocalEncounterBossSuggestion localEncounterBossSuggestion)
		{
			_localEncounterBossSuggestionsOpen = false;
			SetLocalEncounterBossSearchText(localEncounterBossSuggestion.BossName);
			RefreshLocalEncounterBossSuggestions();
			RefreshLocalEncounterPanelRows(null, revealSelectedRow: false);
			lstLocalEncounterBossSuggestions.SelectedItem = null;
		}
	}

	private void InitializeLocalEncounterBossSearchBox()
	{
		if (txtLocalEncounterBossSearchInput != null)
		{
			_localEncounterBossSearchTextBox = txtLocalEncounterBossSearchInput;
			_localEncounterBossSearchTextBox.TextChanged += LocalEncounterBossSearch_TextChanged;
			_localEncounterBossSearchTextBox.GotKeyboardFocus += delegate
			{
				UpdateLocalEncounterSearchPlaceholder();
				UpdateLocalEncounterSearchClearButton();
			};
			_localEncounterBossSearchTextBox.LostKeyboardFocus += delegate
			{
				UpdateLocalEncounterSearchPlaceholder();
				UpdateLocalEncounterSearchClearButton();
			};
			UpdateLocalEncounterSearchText();
			UpdateLocalEncounterSearchPlaceholder();
			UpdateLocalEncounterSearchClearButton();
		}
	}

	private void LocalEncounterBossSearch_TextChanged(object sender, TextChangedEventArgs e)
	{
		bool openSuggestions = !_isApplyingLocalEncounterBossSuggestion;
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			if (openSuggestions)
			{
				_localEncounterBossSuggestionsOpen = true;
			}
			UpdateLocalEncounterSearchText();
			UpdateLocalEncounterSearchPlaceholder();
			UpdateLocalEncounterSearchClearButton();
			RefreshLocalEncounterBossSuggestions();
			RefreshLocalEncounterPanelRows(null, revealSelectedRow: false);
		});
	}

	private void LocalEncounterSearch_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		_localEncounterBossSuggestionsOpen = true;
		RefreshLocalEncounterBossSuggestions();
		UpdateLocalEncounterSearchClearButton();
		DependencyObject source = e.OriginalSource as DependencyObject;
		if (!IsEventInside(source, _localEncounterBossSearchTextBox) && !IsEventInside(source, btnLocalEncounterBossSearchClear))
		{
			_localEncounterBossSearchTextBox?.Focus();
			e.Handled = true;
		}
	}

	private void LocalEncounterSearch_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
	{
		UpdateLocalEncounterSearchClearButton();
	}

	private void LocalEncounterSearch_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
	{
		UpdateLocalEncounterSearchClearButton();
	}

	private void LocalEncounterBossSearchClear_Click(object sender, RoutedEventArgs e)
	{
		if (_localEncounterBossSearchTextBox != null)
		{
			_localEncounterBossSuggestionsOpen = true;
			_localEncounterBossSearchTextBox.Text = "";
			_localEncounterBossSearchTextBox.Focus();
			UpdateLocalEncounterSearchClearButton();
			RefreshLocalEncounterBossSuggestions();
			RefreshLocalEncounterPanelRows(null, revealSelectedRow: false);
			e.Handled = true;
		}
	}

	private void SetLocalEncounterBossSearchText(string text)
	{
		if (_localEncounterBossSearchTextBox == null)
		{
			return;
		}
		_isApplyingLocalEncounterBossSuggestion = true;
		try
		{
			_localEncounterBossSearchTextBox.Text = text;
			_localEncounterBossSearchTextBox.SelectionStart = _localEncounterBossSearchTextBox.Text.Length;
			_localEncounterBossSearchTextBox.Focus();
			UpdateLocalEncounterSearchText();
			UpdateLocalEncounterSearchPlaceholder();
			UpdateLocalEncounterSearchClearButton();
		}
		finally
		{
			_isApplyingLocalEncounterBossSuggestion = false;
		}
	}

	private void UpdateLocalEncounterSearchText()
	{
		UpdateLocalEncounterSearchClearButton();
	}

	private void UpdateLocalEncounterSearchPlaceholder()
	{
		if (txtLocalEncounterBossSearchPlaceholder != null)
		{
			txtLocalEncounterBossSearchPlaceholder.Visibility = ((!string.IsNullOrEmpty(_localEncounterBossSearchTextBox?.Text)) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void UpdateLocalEncounterSearchClearButton()
	{
		if (btnLocalEncounterBossSearchClear != null)
		{
			int num;
			if (!string.IsNullOrEmpty(_localEncounterBossSearchTextBox?.Text))
			{
				Border border = bdLocalEncounterBossSearch;
				num = (((border != null && border.IsMouseOver) || (_localEncounterBossSearchTextBox?.IsKeyboardFocusWithin ?? false)) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			bool flag = (byte)num != 0;
			btnLocalEncounterBossSearchClear.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	private void CloseLocalEncounterBossSuggestions()
	{
		if (!_localEncounterBossSuggestionsOpen)
		{
			System.Windows.Controls.ListBox listBox = lstLocalEncounterBossSuggestions;
			if (listBox == null || listBox.Visibility != Visibility.Visible)
			{
				return;
			}
		}
		_localEncounterBossSuggestionsOpen = false;
		RefreshLocalEncounterBossSuggestions();
	}

	private void RefreshLocalEncounterBossSuggestions()
	{
		if (lstLocalEncounterBossSuggestions == null)
		{
			return;
		}
		LocalEncounterBossSuggestions.Clear();
		if (!_localEncounterBossSuggestionsOpen || !IsLocalEncounterPanelOpen())
		{
			lstLocalEncounterBossSuggestions.Visibility = Visibility.Collapsed;
			return;
		}
		string filter = GetLocalEncounterBossFilter();
		foreach (LocalEncounterBossSuggestion item in (from item in _cachedLocalEncounterHistoryRows.Where((EncounterHistoryRow row) => !string.IsNullOrWhiteSpace(row.BossName)).GroupBy<EncounterHistoryRow, string>((EncounterHistoryRow row) => NormalizeBossRecordName(row.BossName), StringComparer.OrdinalIgnoreCase).Select(delegate(IGrouping<string, EncounterHistoryRow> @group)
			{
				EncounterHistoryRow encounterHistoryRow = @group.OrderByDescending((EncounterHistoryRow row) => row.StartUtc).First();
				return new LocalEncounterBossSuggestion
				{
					BossName = encounterHistoryRow.BossName,
					Count = @group.Count(),
					LatestStartUtc = encounterHistoryRow.StartUtc
				};
			})
			where MatchesLocalEncounterBossFilter(item.BossName, filter)
			orderby item.Count descending, item.LatestStartUtc descending
			select item).ThenBy<LocalEncounterBossSuggestion, string>((LocalEncounterBossSuggestion item) => item.BossName, StringComparer.OrdinalIgnoreCase).Take(8).ToList())
		{
			LocalEncounterBossSuggestions.Add(item);
		}
		lstLocalEncounterBossSuggestions.Visibility = ((LocalEncounterBossSuggestions.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
	}

	private static bool IsSuppressedLocalEncounterHistoryKey(Key key)
	{
		if (key != Key.Prior && key != Key.Next && key != Key.Home)
		{
			return key == Key.End;
		}
		return true;
	}

	private bool TryHandleLocalEncounterHistoryNavigationKey(Key key)
	{
		if (LocalEncounterRows.Count == 0 || !LocalEncounterRows.Any((LocalEncounterPanelRow row) => row.IsEncounterRow))
		{
			return false;
		}
		int? num = key switch
		{
			Key.Up => GetLocalEncounterHistoryNextIndex(-1), 
			Key.Down => GetLocalEncounterHistoryNextIndex(1), 
			_ => null, 
		};
		if (!num.HasValue || lstLocalEncounterHistory == null)
		{
			return false;
		}
		lstLocalEncounterHistory.Focus();
		Keyboard.Focus(lstLocalEncounterHistory);
		lstLocalEncounterHistory.SelectedIndex = num.Value;
		lstLocalEncounterHistory.ScrollIntoView(lstLocalEncounterHistory.SelectedItem);
		_lastLocalEncounterManualScrollUtc = DateTime.UtcNow;
		return true;
	}

	private void LocalEncounterHistory_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		_lastLocalEncounterManualScrollUtc = DateTime.UtcNow;
	}

	private int? GetLocalEncounterHistoryNextIndex(int delta)
	{
		if (lstLocalEncounterHistory == null || LocalEncounterRows.Count == 0)
		{
			return null;
		}
		int num = lstLocalEncounterHistory.SelectedIndex;
		if (num < 0)
		{
			num = ((delta < 0) ? LocalEncounterRows.Count : (-1));
		}
		int num2 = Math.Sign(delta);
		int num3 = Math.Abs(delta);
		int i = num;
		while (num3 > 0)
		{
			for (i += num2; i >= 0 && i < LocalEncounterRows.Count && !LocalEncounterRows[i].IsEncounterRow; i += num2)
			{
			}
			if (i < 0 || i >= LocalEncounterRows.Count)
			{
				return null;
			}
			num3--;
		}
		if (i != lstLocalEncounterHistory.SelectedIndex)
		{
			return i;
		}
		return null;
	}

	private void QueueLocalEncounterLogLoad(string path)
	{
		int version = Interlocked.Increment(ref _localEncounterLogLoadVersion);
		CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
		CancellationTokenSource localEncounterLogSelectionCts;
		lock (_localEncounterLogLoadLock)
		{
			localEncounterLogSelectionCts = _localEncounterLogSelectionCts;
			_localEncounterLogSelectionCts = cancellationTokenSource;
		}
		localEncounterLogSelectionCts?.Cancel();
		LoadQueuedLocalEncounterLogAsync(path, version, cancellationTokenSource);
	}

	private async Task LoadQueuedLocalEncounterLogAsync(string path, int version, CancellationTokenSource cts)
	{
		_ = 1;
		try
		{
			await Task.Delay(120, cts.Token);
			if (IsLocalEncounterLogLoadCurrent(version))
			{
				await LoadEncounterLogPathAsync(path, version, revealSelectedRow: false);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			if (IsLocalEncounterLogLoadCurrent(version))
			{
				ShowSystemBalloon("전투 기록을 열 수 없습니다: " + ex2.Message);
			}
		}
		finally
		{
			lock (_localEncounterLogLoadLock)
			{
				if (_localEncounterLogSelectionCts == cts)
				{
					_localEncounterLogSelectionCts = null;
				}
			}
			cts.Dispose();
		}
	}

	private void CancelQueuedLocalEncounterLogLoad(bool invalidate)
	{
		if (invalidate)
		{
			Interlocked.Increment(ref _localEncounterLogLoadVersion);
		}
		CancellationTokenSource localEncounterLogSelectionCts;
		lock (_localEncounterLogLoadLock)
		{
			localEncounterLogSelectionCts = _localEncounterLogSelectionCts;
			_localEncounterLogSelectionCts = null;
		}
		localEncounterLogSelectionCts?.Cancel();
	}

	private void CancelEncounterReplay()
	{
		_encounterReplayCts?.Cancel();
		_encounterReplayCts = null;
		_isEncounterReplayActive = false;
		_activeEncounterReplayPath = "";
		_encounterReplayProgressRatio = 0.0;
		_lastEncounterReplayRenderedEvents = -1;
	}

	private void StopEncounterReplayFromUi()
	{
		if (_isEncounterReplayActive)
		{
			CancelEncounterReplay();
			UpdateLoadLogButtonUI();
			RefreshLocalEncounterPanelRows();
			RenderTiles(GetSnapshotForCurrentFilter());
			if (IsCombatDetailWindowOpen())
			{
				RenderDetailForCurrentEncounter();
			}
		}
	}

	private async void LocalEncounterReplay_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if ((sender as FrameworkElement)?.DataContext is LocalEncounterPanelRow { CanReplay: not false } localEncounterPanelRow)
		{
			if (localEncounterPanelRow.IsReplayActive)
			{
				StopEncounterReplayFromUi();
			}
			else
			{
				await StartEncounterReplayAsync(localEncounterPanelRow.FullPath);
			}
		}
	}

	private async Task StartEncounterReplayAsync(string path)
	{
		string fullPath = NormalizeLogPath(path);
		if (!File.Exists(fullPath) || !EncounterLogStore.IsRecordFile(fullPath))
		{
			ShowSystemBalloon("리플레이할 수 있는 저장 기록이 아닙니다.");
			return;
		}
		CancelQueuedLocalEncounterLogLoad(invalidate: true);
		CancelEncounterReplay();
		CancellationTokenSource cts = new CancellationTokenSource();
		_encounterReplayCts = cts;
		_isEncounterReplayActive = true;
		_activeEncounterReplayPath = fullPath;
		_encounterReplayProgressRatio = 0.0;
		_lastEncounterReplayRenderedEvents = -1;
		UpdateLoadLogButtonUI();
		RefreshLocalEncounterPanelRows(GetStoredEncounterPanelKey(fullPath), revealSelectedRow: false);
		try
		{
			SetMainContentView(MainContentView.Dps, manual: true, force: true);
			await LoadEncounterLogPathAsync(fullPath);
			if (cts.IsCancellationRequested)
			{
				return;
			}
			DpsCards.Clear();
			ResetDpsRankReorderClock();
			DpsUiAnimations.ResetItems(lstDps);
			borderTopTarget.Visibility = Visibility.Collapsed;
			SetBossHpBar(0.0, visible: false);
			_lastSnapshotDamageEvents = Interlocked.Read(in _parsedDamageEvents);
			await base.Dispatcher.InvokeAsync(delegate
			{
				DpsUiAnimations.ResetItems(lstDps);
			}, DispatcherPriority.Render);
			using MeterEngine replayEngine = CreateHistoryReplayEngine();
			await replayEngine.ReplayEncounterRecordFileAsync(fullPath, 1.0, async delegate(CombatSnapshot? snapshot, EncounterReplayProgress progress)
			{
				await base.Dispatcher.InvokeAsync(delegate
				{
					if (!cts.IsCancellationRequested && _isEncounterReplayActive && string.Equals(_activeEncounterReplayPath, fullPath, StringComparison.OrdinalIgnoreCase))
					{
						UpdateEncounterReplayProgressRows(fullPath, progress);
						if (snapshot != null && (progress.PlayedEvents != _lastEncounterReplayRenderedEvents || progress.IsComplete))
						{
							_lastEncounterReplayRenderedEvents = progress.PlayedEvents;
							RenderTiles(snapshot);
						}
					}
				}, DispatcherPriority.Background);
			}, cts.Token);
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			ShowSystemBalloon("리플레이를 시작할 수 없습니다: " + ex2.Message);
		}
		finally
		{
			if (_encounterReplayCts == cts)
			{
				_encounterReplayCts = null;
				_isEncounterReplayActive = false;
				_activeEncounterReplayPath = "";
				_encounterReplayProgressRatio = 0.0;
				_lastEncounterReplayRenderedEvents = -1;
				UpdateLoadLogButtonUI();
				RefreshLocalEncounterPanelRows(GetStoredEncounterPanelKey(fullPath), revealSelectedRow: false);
			}
			cts.Dispose();
		}
	}

	private bool IsLocalEncounterLogLoadCurrent(int version)
	{
		return version == Volatile.Read(in _localEncounterLogLoadVersion);
	}

	private async Task LoadLocalEncounterHistoryRowsAsync()
	{
		int version = Interlocked.Increment(ref _localEncounterHistoryLoadVersion);
		_isLocalEncounterHistoryLoading = true;
		RefreshLocalEncounterPanelRows();
		try
		{
			List<EncounterHistoryRow> collection = await Task.Run((Func<List<EncounterHistoryRow>>)BuildEncounterHistoryRows);
			if (version == _localEncounterHistoryLoadVersion)
			{
				_cachedLocalEncounterHistoryRows.Clear();
				_cachedLocalEncounterHistoryRows.AddRange(collection);
			}
		}
		catch (Exception ex)
		{
			if (version == _localEncounterHistoryLoadVersion)
			{
				ShowSystemBalloon("전투 기록을 불러올 수 없습니다: " + ex.Message);
			}
		}
		finally
		{
			if (version == _localEncounterHistoryLoadVersion)
			{
				_isLocalEncounterHistoryLoading = false;
				RefreshLocalEncounterPanelRows();
			}
		}
	}

	private void RefreshLocalEncounterPanelRows(string? preferredKey = null, bool revealSelectedRow = true)
	{
		if (!IsLocalEncounterPanelOpen() || lstLocalEncounterHistory == null)
		{
			return;
		}
		RefreshLocalEncounterBossSuggestions();
		bool flag = !revealSelectedRow || (preferredKey == null && IsLocalEncounterManualScrollActive());
		double? num = (flag ? GetLocalEncounterHistoryVerticalOffset() : ((double?)null));
		string text = (lstLocalEncounterHistory.SelectedItem as LocalEncounterPanelRow)?.Key;
		string selectedKey = preferredKey ?? text ?? GetLocalEncounterPanelKeyFromCurrentFilter();
		List<LocalEncounterPanelRow> list = BuildLocalEncounterPanelRows();
		if (!flag && preferredKey == null && selectedKey != null && string.Equals(selectedKey, text, StringComparison.Ordinal))
		{
			flag = true;
			num = GetLocalEncounterHistoryVerticalOffset();
		}
		double? restoreVerticalOffset = (flag ? num : ((double?)null));
		_isUpdatingLocalEncounterSelection = true;
		try
		{
			SynchronizeLocalEncounterRows(list);
			LocalEncounterPanelRow localEncounterPanelRow = ((!string.IsNullOrWhiteSpace(selectedKey)) ? LocalEncounterRows.FirstOrDefault((LocalEncounterPanelRow row) => row.IsEncounterRow && string.Equals(row.Key, selectedKey, StringComparison.Ordinal)) : null);
			lstLocalEncounterHistory.SelectedItem = localEncounterPanelRow;
			if (localEncounterPanelRow != null && revealSelectedRow && !flag)
			{
				lstLocalEncounterHistory.ScrollIntoView(localEncounterPanelRow);
			}
			else if (restoreVerticalOffset.HasValue)
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					RestoreLocalEncounterHistoryVerticalOffset(restoreVerticalOffset.Value);
				}, DispatcherPriority.Background);
			}
			if (txtLocalEncounterCount != null)
			{
				int value = list.Count((LocalEncounterPanelRow row) => row.IsEncounterRow);
				txtLocalEncounterCount.Text = (_isLocalEncounterHistoryLoading ? $"{value:N0}개 · 불러오는 중" : $"{value:N0}개");
			}
			if (txtLocalEncounterEmpty != null)
			{
				int num2 = list.Count((LocalEncounterPanelRow row) => row.IsEncounterRow);
				txtLocalEncounterEmpty.Text = (_isLocalEncounterHistoryLoading ? "기록 불러오는 중" : (HasLocalEncounterBossFilter() ? "검색 결과 없음" : "기록 없음"));
				txtLocalEncounterEmpty.Visibility = ((num2 != 0) ? Visibility.Collapsed : Visibility.Visible);
			}
		}
		finally
		{
			_isUpdatingLocalEncounterSelection = false;
		}
	}

	private void SynchronizeLocalEncounterRows(IReadOnlyList<LocalEncounterPanelRow> rows)
	{
		int num = Math.Min(LocalEncounterRows.Count, rows.Count);
		for (int i = 0; i < num; i++)
		{
			if (!LocalEncounterPanelRowsEqual(LocalEncounterRows[i], rows[i]))
			{
				LocalEncounterRows[i] = rows[i];
			}
		}
		while (LocalEncounterRows.Count > rows.Count)
		{
			LocalEncounterRows.RemoveAt(LocalEncounterRows.Count - 1);
		}
		for (int j = LocalEncounterRows.Count; j < rows.Count; j++)
		{
			LocalEncounterRows.Add(rows[j]);
		}
	}

	private static bool LocalEncounterPanelRowsEqual(LocalEncounterPanelRow left, LocalEncounterPanelRow right)
	{
		if (left.Kind == right.Kind && string.Equals(left.Key, right.Key, StringComparison.Ordinal) && left.TargetId == right.TargetId && left.ArchivedRecordId == right.ArchivedRecordId && string.Equals(left.FullPath, right.FullPath, StringComparison.OrdinalIgnoreCase) && Nullable.Equals(left.StartUtc, right.StartUtc) && string.Equals(left.TimeText, right.TimeText, StringComparison.Ordinal) && string.Equals(left.BossName, right.BossName, StringComparison.Ordinal) && string.Equals(left.DungeonText, right.DungeonText, StringComparison.Ordinal) && string.Equals(left.DurationText, right.DurationText, StringComparison.Ordinal) && string.Equals(left.TotalDamageText, right.TotalDamageText, StringComparison.Ordinal) && string.Equals(left.ParticipantText, right.ParticipantText, StringComparison.Ordinal) && string.Equals(left.LocalPlayerDpsText, right.LocalPlayerDpsText, StringComparison.Ordinal) && string.Equals(left.DateText, right.DateText, StringComparison.Ordinal))
		{
			return left.IsReplayActive == right.IsReplayActive;
		}
		return false;
	}

	private double? GetLocalEncounterHistoryVerticalOffset()
	{
		return FindVisualDescendant<ScrollViewer>(lstLocalEncounterHistory)?.VerticalOffset;
	}

	private bool IsLocalEncounterManualScrollActive()
	{
		return (DateTime.UtcNow - _lastLocalEncounterManualScrollUtc).TotalSeconds < 2.0;
	}

	private bool IsActiveEncounterReplayPath(string path)
	{
		if (_isEncounterReplayActive && !string.IsNullOrWhiteSpace(path))
		{
			return string.Equals(NormalizeLogPath(path), _activeEncounterReplayPath, StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private double GetEncounterReplayProgressRatio(string path)
	{
		if (!IsActiveEncounterReplayPath(path))
		{
			return 0.0;
		}
		return _encounterReplayProgressRatio;
	}

	private void UpdateEncounterReplayProgressRows(string path, EncounterReplayProgress progress)
	{
		if (!IsActiveEncounterReplayPath(path))
		{
			return;
		}
		double num = Math.Max(1.0, progress.Duration.TotalMilliseconds);
		_encounterReplayProgressRatio = Math.Clamp(progress.Position.TotalMilliseconds * 100.0 / num, 0.0, 100.0);
		foreach (LocalEncounterPanelRow localEncounterRow in LocalEncounterRows)
		{
			bool flag = (localEncounterRow.IsReplayActive = localEncounterRow.CanReplay && IsActiveEncounterReplayPath(localEncounterRow.FullPath));
			localEncounterRow.ReplayProgressRatio = (flag ? _encounterReplayProgressRatio : 0.0);
		}
	}

	private void RestoreLocalEncounterHistoryVerticalOffset(double offset)
	{
		if (IsLocalEncounterPanelOpen())
		{
			ScrollViewer scrollViewer = FindVisualDescendant<ScrollViewer>(lstLocalEncounterHistory);
			scrollViewer?.ScrollToVerticalOffset(Math.Clamp(offset, 0.0, scrollViewer.ScrollableHeight));
		}
	}

	private List<LocalEncounterPanelRow> BuildLocalEncounterPanelRows()
	{
		List<LocalEncounterPanelRow> list = new List<LocalEncounterPanelRow>();
		HashSet<int> liveTargetIds = new HashSet<int>();
		string bossFilter = GetLocalEncounterBossFilter();
		List<EncounterHistoryRow> storedRows = _cachedLocalEncounterHistoryRows.Where((EncounterHistoryRow row) => EncounterLogStore.IsRecordFile(row.FullPath)).ToList();
		HashSet<string> storedPaths = new HashSet<string>(_cachedLocalEncounterHistoryRows.Select((EncounterHistoryRow row) => NormalizeLogPath(row.FullPath)), StringComparer.OrdinalIgnoreCase);
		if (!_isLogViewMode)
		{
			foreach (TargetInfo item in from t in _engine.GetAllTargets()
				where t.TotalDamage > 0
				orderby t.LastHit descending, t.TotalDamage descending
				select t)
			{
				if (!HasArchivedSnapshotForLiveTarget(item) && !HasStoredEncounterForLiveTarget(item, storedRows))
				{
					liveTargetIds.Add(item.TargetId);
					LocalEncounterPanelRow localEncounterPanelRow = CreateLiveEncounterPanelRow(item);
					if (MatchesLocalEncounterBossFilter(localEncounterPanelRow.BossName, bossFilter))
					{
						list.Add(localEncounterPanelRow);
					}
				}
			}
		}
		foreach (ArchivedBossRecord item2 in from record in _archivedBossRecords
			where !liveTargetIds.Contains(record.TargetId) && (string.IsNullOrWhiteSpace(record.SourceFullPath) || !storedPaths.Contains(NormalizeLogPath(record.SourceFullPath)))
			orderby record.DisplayTimeLocal descending
			select record)
		{
			LocalEncounterPanelRow localEncounterPanelRow2 = CreateArchivedEncounterPanelRow(item2);
			if (MatchesLocalEncounterBossFilter(localEncounterPanelRow2.BossName, bossFilter))
			{
				list.Add(localEncounterPanelRow2);
			}
		}
		foreach (EncounterHistoryRow stored in string.IsNullOrWhiteSpace(bossFilter) ? _cachedLocalEncounterHistoryRows.Take(120) : _cachedLocalEncounterHistoryRows.Where((EncounterHistoryRow row) => MatchesLocalEncounterBossFilter(row.BossName, bossFilter)).Take(120))
		{
			ArchivedBossRecord archivedBossRecord = FindArchivedBossRecordBySourcePath(stored.FullPath);
			if (archivedBossRecord != null)
			{
				list.Add(CreateStoredBackedArchivedEncounterPanelRow(stored, archivedBossRecord));
			}
			else if (!list.Any((LocalEncounterPanelRow row) => IsSameLocalEncounter(row, stored)))
			{
				list.Add(CreateStoredEncounterPanelRow(stored));
			}
		}
		return InsertLocalEncounterDateSeparators(list);
	}

	private static bool HasStoredEncounterForLiveTarget(TargetInfo target, IReadOnlyList<EncounterHistoryRow> storedRows)
	{
		if (storedRows.Count == 0)
		{
			return false;
		}
		DateTime dateTime = NormalizeUtc(target.FirstHit);
		DateTime dateTime2 = NormalizeUtc(target.LastHit);
		string text = NormalizeBossRecordName(target.Name);
		foreach (EncounterHistoryRow storedRow in storedRows)
		{
			EncounterLogIndexItem source = storedRow.Source;
			bool num = source.BossActorId > 0 && source.BossActorId == target.TargetId;
			bool flag = source.BossMobCode > 0 && target.MobCode > 0 && source.BossMobCode == target.MobCode;
			bool flag2 = text.Length > 0 && string.Equals(text, NormalizeBossRecordName(storedRow.BossName), StringComparison.OrdinalIgnoreCase);
			if (num || flag || flag2)
			{
				DateTime dateTime3 = NormalizeUtc(source.StartUtc);
				DateTime dateTime4 = NormalizeUtc(source.EndUtc);
				if (dateTime4 < dateTime3)
				{
					dateTime4 = dateTime3;
				}
				bool flag3 = dateTime <= dateTime4.AddSeconds(3.0) && dateTime2 >= dateTime3.AddSeconds(-3.0);
				bool flag4 = Math.Abs((dateTime - dateTime3).TotalSeconds) <= 3.0;
				bool flag5 = Math.Abs((dateTime2 - dateTime4).TotalSeconds) <= 5.0;
				if (flag3 || flag4 || flag5)
				{
					return true;
				}
			}
		}
		return false;
	}

	private string GetLocalEncounterBossFilter()
	{
		return _localEncounterBossSearchTextBox?.Text?.Trim() ?? "";
	}

	private bool HasLocalEncounterBossFilter()
	{
		return !string.IsNullOrWhiteSpace(GetLocalEncounterBossFilter());
	}

	private static bool MatchesLocalEncounterBossFilter(string bossName, string filter)
	{
		if (string.IsNullOrWhiteSpace(filter))
		{
			return true;
		}
		string text = NormalizeBossRecordName(bossName);
		string value = NormalizeBossRecordName(filter);
		return text.Contains(value, StringComparison.OrdinalIgnoreCase);
	}

	private static List<LocalEncounterPanelRow> InsertLocalEncounterDateSeparators(List<LocalEncounterPanelRow> rows)
	{
		List<LocalEncounterPanelRow> list = new List<LocalEncounterPanelRow>(rows.Count + 8);
		DateTime? dateTime = null;
		foreach (LocalEncounterPanelRow row in rows)
		{
			DateTime date = (row.StartUtc ?? DateTime.UtcNow).ToLocalTime().Date;
			if (dateTime != date)
			{
				list.Add(new LocalEncounterPanelRow
				{
					Kind = LocalEncounterPanelRowKind.DateSeparator,
					Key = $"date:{date:yyyyMMdd}",
					DateText = date.ToString("yyyy년 M월 d일 dddd", KoreanCulture)
				});
				dateTime = date;
			}
			list.Add(row);
		}
		return list;
	}

	private LocalEncounterPanelRow CreateLiveEncounterPanelRow(TargetInfo target)
	{
		CombatSnapshot combatSnapshot = _engine.BuildSnapshotForTarget(target.TargetId);
		DateTime value = NormalizeUtc(combatSnapshot?.SessionStartUtc ?? target.FirstHit);
		TimeSpan duration = combatSnapshot?.TopTargetDuration ?? (target.LastHit - target.FirstHit);
		long value2 = combatSnapshot?.TopTargetDamage ?? target.TotalDamage;
		string text = combatSnapshot?.TopTargetName ?? target.Name;
		if (string.IsNullOrWhiteSpace(text) || text == target.TargetId.ToString(CultureInfo.InvariantCulture))
		{
			text = $"#{target.TargetId}";
		}
		return new LocalEncounterPanelRow
		{
			Kind = LocalEncounterPanelRowKind.Live,
			Key = GetLiveEncounterPanelKey(target.TargetId),
			TargetId = target.TargetId,
			StartUtc = value,
			TimeText = value.ToLocalTime().ToString("HH:mm", KoreanCulture),
			BossName = text,
			DungeonText = ResolveEncounterDungeonText(target.MobCode, "던전 정보 없음"),
			DurationText = FormatCombatDuration(duration),
			TotalDamageText = FormatLocalEncounterCompact(value2),
			ParticipantText = FormatLocalEncounterParticipants(combatSnapshot),
			LocalPlayerDpsText = FormatLocalPlayerDps(combatSnapshot)
		};
	}

	private LocalEncounterPanelRow CreateArchivedEncounterPanelRow(ArchivedBossRecord record)
	{
		CombatSnapshot snapshot = record.Snapshot;
		DateTime value = NormalizeUtc(snapshot.SessionStartUtc);
		return new LocalEncounterPanelRow
		{
			Kind = LocalEncounterPanelRowKind.Archived,
			Key = (GetLocalEncounterPanelKeyForArchivedRecord(record) ?? GetArchivedEncounterPanelKey(record.ArchivedRecordId)),
			TargetId = record.TargetId,
			ArchivedRecordId = record.ArchivedRecordId,
			FullPath = record.SourceFullPath,
			StartUtc = value,
			TimeText = record.DisplayTimeLocal.ToString("HH:mm", KoreanCulture),
			BossName = (string.IsNullOrWhiteSpace(record.TargetName) ? snapshot.TopTargetName : record.TargetName),
			DungeonText = (string.IsNullOrWhiteSpace(record.DungeonText) ? "던전 정보 없음" : record.DungeonText),
			DurationText = FormatCombatDuration(snapshot.TopTargetDuration),
			TotalDamageText = FormatLocalEncounterCompact(snapshot.TopTargetDamage),
			ParticipantText = FormatLocalEncounterParticipants(snapshot),
			LocalPlayerDpsText = ((string.IsNullOrWhiteSpace(record.LocalPlayerDpsText) || string.Equals(record.LocalPlayerDpsText, "-", StringComparison.Ordinal)) ? FormatLocalPlayerDps(snapshot) : record.LocalPlayerDpsText),
			IsReplayActive = IsActiveEncounterReplayPath(record.SourceFullPath),
			ReplayProgressRatio = GetEncounterReplayProgressRatio(record.SourceFullPath)
		};
	}

	private LocalEncounterPanelRow CreateStoredBackedArchivedEncounterPanelRow(EncounterHistoryRow row, ArchivedBossRecord record)
	{
		return new LocalEncounterPanelRow
		{
			Kind = LocalEncounterPanelRowKind.Archived,
			Key = GetStoredEncounterPanelKey(row.FullPath),
			TargetId = record.TargetId,
			ArchivedRecordId = record.ArchivedRecordId,
			FullPath = row.FullPath,
			StartUtc = NormalizeUtc(row.StartUtc),
			TimeText = row.TimeText,
			BossName = row.BossName,
			DungeonText = row.DungeonText,
			DurationText = row.DurationText,
			TotalDamageText = row.TotalDamageText,
			ParticipantText = FormatLocalEncounterParticipants(record.Snapshot),
			LocalPlayerDpsText = ((string.IsNullOrWhiteSpace(record.LocalPlayerDpsText) || string.Equals(record.LocalPlayerDpsText, "-", StringComparison.Ordinal)) ? row.LocalPlayerDpsText : record.LocalPlayerDpsText),
			IsReplayActive = IsActiveEncounterReplayPath(row.FullPath),
			ReplayProgressRatio = GetEncounterReplayProgressRatio(row.FullPath)
		};
	}

	private LocalEncounterPanelRow CreateStoredEncounterPanelRow(EncounterHistoryRow row)
	{
		return new LocalEncounterPanelRow
		{
			Kind = LocalEncounterPanelRowKind.Stored,
			Key = GetStoredEncounterPanelKey(row.FullPath),
			FullPath = row.FullPath,
			StartUtc = NormalizeUtc(row.StartUtc),
			TimeText = row.TimeText,
			BossName = row.BossName,
			DungeonText = row.DungeonText,
			DurationText = row.DurationText,
			TotalDamageText = row.TotalDamageText,
			ParticipantText = row.ParticipantText,
			LocalPlayerDpsText = row.LocalPlayerDpsText,
			IsReplayActive = IsActiveEncounterReplayPath(row.FullPath),
			ReplayProgressRatio = GetEncounterReplayProgressRatio(row.FullPath)
		};
	}

	private string GetCurrentLocalEncounterDungeonText()
	{
		return ResolveEncounterDungeonText(0, "던전 정보 없음");
	}

	private string ResolveEncounterDungeonText(int bossMobCode, string fallbackText)
	{
		if (_currentDungeonContent != null)
		{
			return _currentDungeonContent.DisplayName;
		}
		return ResolveEncounterDungeonTextFromCodes(0, bossMobCode, fallbackText);
	}

	private string ResolveEncounterDungeonTextFromCodes(int contentCode, int bossMobCode, string fallbackText)
	{
		if (contentCode > 0 && _dungeonContentMap.TryGet(contentCode, out DungeonContentInfo info))
		{
			return info.DisplayName;
		}
		if (bossMobCode > 0)
		{
			DungeonBossCatalogEntry dungeonBossCatalogEntry = _dungeonBossCatalogMap.FindDungeonsByBossCode(bossMobCode).FirstOrDefault();
			if (dungeonBossCatalogEntry != null)
			{
				return dungeonBossCatalogEntry.DisplayName;
			}
		}
		if (!string.IsNullOrWhiteSpace(fallbackText))
		{
			return fallbackText;
		}
		return "던전 정보 없음";
	}

	private string ResolveStoredEncounterDungeonText(string path)
	{
		EncounterHistoryRow encounterHistoryRow = _cachedLocalEncounterHistoryRows.FirstOrDefault((EncounterHistoryRow row) => string.Equals(row.FullPath, path, StringComparison.OrdinalIgnoreCase));
		if (encounterHistoryRow != null && !string.IsNullOrWhiteSpace(encounterHistoryRow.DungeonText))
		{
			return encounterHistoryRow.DungeonText;
		}
		try
		{
			EncounterLogStore encounterLogStore = new EncounterLogStore();
			foreach (EncounterLogIndexItem item in encounterLogStore.ListRecords())
			{
				if (string.Equals(encounterLogStore.ResolveRecordPath(item.FileName), path, StringComparison.OrdinalIgnoreCase))
				{
					return ResolveEncounterDungeonTextFromCodes(item.ContentCode, item.BossMobCode, "던전 정보 없음");
				}
			}
		}
		catch
		{
		}
		if (!EncounterLogStore.IsRecordFile(path))
		{
			return "CSV 로그";
		}
		return "던전 정보 없음";
	}

	private string ResolveStoredEncounterLocalPlayerDpsText(string path)
	{
		EncounterHistoryRow encounterHistoryRow = _cachedLocalEncounterHistoryRows.FirstOrDefault((EncounterHistoryRow row) => string.Equals(row.FullPath, path, StringComparison.OrdinalIgnoreCase));
		if (encounterHistoryRow != null && !string.IsNullOrWhiteSpace(encounterHistoryRow.LocalPlayerDpsText) && !string.Equals(encounterHistoryRow.LocalPlayerDpsText, "-", StringComparison.Ordinal))
		{
			return encounterHistoryRow.LocalPlayerDpsText;
		}
		try
		{
			EncounterLogStore encounterLogStore = new EncounterLogStore();
			foreach (EncounterLogIndexItem item in encounterLogStore.ListRecords())
			{
				if (string.Equals(encounterLogStore.ResolveRecordPath(item.FileName), path, StringComparison.OrdinalIgnoreCase) && item.LocalPlayerDps > 0.0)
				{
					return ((long)item.LocalPlayerDps).ToString("N0", KoreanCulture);
				}
			}
		}
		catch
		{
		}
		return "";
	}

	private static string FormatLocalEncounterParticipants(CombatSnapshot? snapshot)
	{
		if (snapshot == null)
		{
			return "-";
		}
		int num = snapshot.Actors.Count((ActorStats actor) => !actor.IsMonster && actor.TotalDamage > 0);
		if (num <= 0)
		{
			return "-";
		}
		return $"{num:N0}명";
	}

	private string FormatLocalPlayerDps(CombatSnapshot? snapshot)
	{
		if (snapshot == null)
		{
			return "-";
		}
		ActorStats actorStats = FindLocalPlayerActor(snapshot);
		if ((object)actorStats == null || !(actorStats.Dps > 0.0))
		{
			return "-";
		}
		return ((long)actorStats.Dps).ToString("N0", KoreanCulture);
	}

	private ActorStats? FindLocalPlayerActor(CombatSnapshot snapshot)
	{
		int? localActorId = _engine.LocalPlayerActorId;
		if (localActorId.HasValue)
		{
			ActorStats actorStats = snapshot.Actors.FirstOrDefault((ActorStats actor) => actor.ActorId == localActorId.Value);
			if (actorStats != null)
			{
				return actorStats;
			}
		}
		string localPlayerName = _engine.LocalPlayerName;
		if (string.IsNullOrWhiteSpace(localPlayerName))
		{
			return null;
		}
		string normalizedLocalName = NormalizeCharacterNameForMatch(localPlayerName);
		return snapshot.Actors.FirstOrDefault((ActorStats actor) => !actor.IsMonster && string.Equals(NormalizeCharacterNameForMatch(actor.Name), normalizedLocalName, StringComparison.Ordinal));
	}

	private static string NormalizeCharacterNameForMatch(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "";
		}
		string text = name.Trim();
		int num = text.IndexOf('[');
		if (num > 0)
		{
			text = text.Substring(0, num).Trim();
		}
		return text;
	}

	private static string FormatLocalEncounterCompact(long value)
	{
		if (value <= 0)
		{
			return "-";
		}
		if (value >= 100000000)
		{
			return $"{(double)value / 100000000.0:0.##}억";
		}
		if (value >= 10000)
		{
			return $"{(double)value / 10000.0:0.#}만";
		}
		return value.ToString("N0", KoreanCulture);
	}

	private static bool IsSameLocalEncounter(LocalEncounterPanelRow existing, EncounterHistoryRow stored)
	{
		if (!existing.StartUtc.HasValue)
		{
			return false;
		}
		if (Math.Abs((existing.StartUtc.Value - NormalizeUtc(stored.StartUtc)).TotalSeconds) <= 3.0)
		{
			return string.Equals(NormalizeBossRecordName(existing.BossName), NormalizeBossRecordName(stored.BossName), StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private string? GetLocalEncounterPanelKeyFromCurrentFilter()
	{
		if (_encounterViewKind == EncounterViewKind.ArchivedBoss)
		{
			return GetLocalEncounterPanelKeyForArchivedRecord(GetSelectedArchivedBossRecord());
		}
		int currentLiveBossTargetId = GetCurrentLiveBossTargetId();
		if (currentLiveBossTargetId <= 0)
		{
			return null;
		}
		return GetLiveEncounterPanelKey(currentLiveBossTargetId);
	}

	private static string GetLiveEncounterPanelKey(int targetId)
	{
		return $"live:{targetId}";
	}

	private static string? GetLocalEncounterPanelKeyForArchivedRecord(ArchivedBossRecord? record)
	{
		if (record == null)
		{
			return null;
		}
		if (!string.IsNullOrWhiteSpace(record.SourceFullPath))
		{
			return GetStoredEncounterPanelKey(record.SourceFullPath);
		}
		return GetArchivedEncounterPanelKey(record.ArchivedRecordId);
	}

	private static string GetArchivedEncounterPanelKey(int archivedRecordId)
	{
		return $"arch:{archivedRecordId}";
	}

	private static string GetStoredEncounterPanelKey(string fullPath)
	{
		return "file:" + NormalizeLogPath(fullPath);
	}

	private static string NormalizeLogPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "";
		}
		try
		{
			return System.IO.Path.GetFullPath(path);
		}
		catch
		{
			return path.Trim();
		}
	}

	private static DateTime NormalizeUtc(DateTime value)
	{
		if (value.Kind == DateTimeKind.Utc)
		{
			return value;
		}
		if (value.Kind == DateTimeKind.Local)
		{
			return value.ToUniversalTime();
		}
		return DateTime.SpecifyKind(value, DateTimeKind.Utc);
	}

	private CombatSnapshot? GetSnapshotForCurrentFilter()
	{
		if (_encounterViewKind == EncounterViewKind.ArchivedBoss)
		{
			return GetSelectedArchivedBossRecord()?.Snapshot ?? _engine.LatestSnapshot;
		}
		int currentLiveBossTargetId = GetCurrentLiveBossTargetId();
		if (currentLiveBossTargetId != 0)
		{
			return _engine.BuildSnapshotForTarget(currentLiveBossTargetId) ?? _engine.LatestSnapshot;
		}
		return _engine.LatestSnapshot;
	}

	private int GetSelectedTargetId()
	{
		if (_encounterViewKind == EncounterViewKind.ArchivedBoss)
		{
			return GetSelectedArchivedBossRecord()?.TargetId ?? 0;
		}
		return GetCurrentLiveBossTargetId();
	}

	private (double Ratio, string Text, bool IsBossHpPercent) GetDamageShareDisplay(ActorStats actor, CombatSnapshot snap, long partyDamage)
	{
		bool flag = _damageShareMode == DamageShareMode.BossHpPercent && GetSelectedTargetId() != 0 && !IsTrainingDummyTargetName(snap.TopTargetName) && snap.TopTargetMaxHp > 0;
		double num = (flag ? ((double)actor.TotalDamage * 100.0 / (double)Math.Max(1, snap.TopTargetMaxHp)) : ((double)actor.TotalDamage * 100.0 / (double)Math.Max(1L, partyDamage)));
		string item = $"{num:0.0}%";
		return (Ratio: num, Text: item, IsBossHpPercent: flag);
	}

	private double GetDamageShareBackgroundRatio(ActorStats actor, long partyDamage, double displayRatio, double topDisplayRatio)
	{
		if (_damageShareGraphMode == DamageShareGraphMode.PartyDamageShare)
		{
			return Math.Clamp((double)actor.TotalDamage * 100.0 / (double)Math.Max(1L, partyDamage), 0.0, 100.0);
		}
		return GetRelativeTopShareRatio(displayRatio, topDisplayRatio);
	}

	private static double GetRelativeTopShareRatio(double ratio, double topRatio)
	{
		if (topRatio <= 0.0)
		{
			return 0.0;
		}
		return Math.Clamp(ratio * 100.0 / topRatio, 0.0, 100.0);
	}

	private static bool IsTrainingDummyTargetName(string? targetName)
	{
		if (!string.IsNullOrWhiteSpace(targetName))
		{
			return targetName.Contains("훈련용 허수아비", StringComparison.Ordinal);
		}
		return false;
	}

	private static string FormatCombatDuration(TimeSpan duration)
	{
		if (!(duration.TotalHours >= 1.0))
		{
			return duration.ToString("mm\\:ss");
		}
		return duration.ToString("h\\:mm\\:ss");
	}

	private static string FormatCombatDurationDetailed(TimeSpan duration)
	{
		if (duration < TimeSpan.Zero)
		{
			duration = TimeSpan.Zero;
		}
		TimeSpan timeSpan = TimeSpan.FromMilliseconds(Math.Round(duration.TotalMilliseconds / 100.0, MidpointRounding.AwayFromZero) * 100.0);
		int value = timeSpan.Milliseconds / 100;
		if (timeSpan.TotalHours >= 1.0)
		{
			return $"{(int)timeSpan.TotalHours}:{timeSpan.Minutes:00}:{timeSpan.Seconds:00}.{value}";
		}
		return $"{(int)timeSpan.TotalMinutes:00}:{timeSpan.Seconds:00}.{value}";
	}

	private static string FormatActorCombatDuration(ActorStats actor, TimeSpan fallbackDuration)
	{
		if (actor.FirstHitUtc != default(DateTime) && actor.LastHitUtc != default(DateTime) && actor.LastHitUtc >= actor.FirstHitUtc)
		{
			return FormatCombatDuration(actor.LastHitUtc - actor.FirstHitUtc);
		}
		return FormatCombatDuration(fallbackDuration);
	}

	private static string FormatTopTargetHp(CombatSnapshot snap)
	{
		if (snap.TopTargetCurrentHp >= 0)
		{
			return snap.TopTargetCurrentHp.ToString("N0");
		}
		if (snap.TopTargetMaxHp <= 0)
		{
			return "";
		}
		return snap.TopTargetMaxHp.ToString("N0");
	}

	private long GetTopTargetCurrentHpForDisplay(CombatSnapshot snap)
	{
		long value = ((snap.TopTargetCurrentHp >= 0) ? snap.TopTargetCurrentHp : snap.TopTargetMaxHp);
		if (_encounterViewKind == EncounterViewKind.ArchivedBoss && snap.TopTargetMaxHp > 0 && snap.TopTargetDamage > 0 && (snap.TopTargetCurrentHp < 0 || snap.TopTargetCurrentHp >= snap.TopTargetMaxHp || snap.TopTargetDamage >= snap.TopTargetMaxHp))
		{
			value = Math.Clamp(snap.TopTargetMaxHp - snap.TopTargetDamage, 0L, snap.TopTargetMaxHp);
		}
		return Math.Clamp(value, 0L, Math.Max(0, snap.TopTargetMaxHp));
	}

	private void SetTopTargetName(CombatSnapshot snap, string targetName, bool minimal)
	{
		txtTopTargetName.Inlines.Clear();
		txtTopTargetHpValue.Text = "";
		txtTopTargetHpPercent.Text = "";
		txtTopTargetHpValue.Visibility = Visibility.Visible;
		topTargetHpSummaryStack.Visibility = Visibility.Collapsed;
		topTargetHpSummaryStack.ToolTip = null;
		if (!minimal || snap.TopTargetMaxHp <= 0)
		{
			txtTopTargetName.Text = targetName;
			txtTopTargetName.ToolTip = targetName;
			return;
		}
		long topTargetCurrentHpForDisplay = GetTopTargetCurrentHpForDisplay(snap);
		string text = topTargetCurrentHpForDisplay.ToString("N0");
		string value = snap.TopTargetMaxHp.ToString("N0");
		string text2 = FormatTopTargetHpPercent(topTargetCurrentHpForDisplay, snap.TopTargetMaxHp);
		string toolTip = $"{targetName}\n현재 HP: {text}\n총 HP: {value}\n잔여 HP: {text2}";
		txtTopTargetName.Text = targetName;
		txtTopTargetName.ToolTip = toolTip;
		topTargetHpSummaryStack.ToolTip = toolTip;
		topTargetHpSummaryStack.Visibility = Visibility.Visible;
		txtTopTargetHpValue.Visibility = Visibility.Visible;
		txtTopTargetHpValue.Text = text;
		txtTopTargetHpPercent.Text = text2;
	}

	private void SetTopTargetStatus(CombatSnapshot snap, bool isPlayerTarget)
	{
		SetTopTargetStatusText(txtTopTargetType, snap, isPlayerTarget);
	}

	private void SetTopTargetInlineMetrics(CombatSnapshot snap)
	{
		txtTopTargetInlineDamage.Text = $"{snap.TopTargetDamage:N0}";
		txtTopTargetInlineDuration.Text = FormatCombatDuration(snap.TopTargetDuration);
	}

	private void SetTopTargetStatusText(TextBlock target, CombatSnapshot snap, bool isPlayerTarget)
	{
		target.Inlines.Clear();
		if (isPlayerTarget)
		{
			target.ToolTip = null;
			target.Inlines.Add(new Run("플레이어")
			{
				Foreground = FindBrush("BossCardPlayerBrush")
			});
			return;
		}
		if (!snap.IsBossConfirmed)
		{
			target.ToolTip = null;
			target.Inlines.Add(new Run("보스 추정")
			{
				Foreground = FindBrush("ThemeTextMutedBrush")
			});
			return;
		}
		if (snap.TopTargetMaxHp <= 0)
		{
			target.ToolTip = null;
			target.Inlines.Add(new Run("보스")
			{
				Foreground = FindBrush("BossCardStatusBrush")
			});
			return;
		}
		long topTargetCurrentHpForDisplay = GetTopTargetCurrentHpForDisplay(snap);
		string text = topTargetCurrentHpForDisplay.ToString("N0");
		string text2 = snap.TopTargetMaxHp.ToString("N0");
		string text3 = FormatTopTargetHpPercent(topTargetCurrentHpForDisplay, snap.TopTargetMaxHp);
		target.ToolTip = $"현재 HP: {text}\n총 HP: {text2}\n잔여 HP: {text3}";
		target.Inlines.Add(new Run(text)
		{
			Foreground = FindBrush("BossCardHpCurrentBrush")
		});
		target.Inlines.Add(new Run(" / ")
		{
			Foreground = FindBrush("ThemeTextMutedBrush")
		});
		target.Inlines.Add(new Run(text2)
		{
			Foreground = FindBrush("BossCardHpMaxBrush")
		});
		target.Inlines.Add(new Run("  ")
		{
			Foreground = FindBrush("ThemeTextMutedBrush")
		});
		target.Inlines.Add(new Run(text3)
		{
			Foreground = FindBrush("BossCardHpCurrentBrush")
		});
	}

	private static string FormatTopTargetHpPercent(long currentHp, int maxHp)
	{
		if (maxHp <= 0)
		{
			return "";
		}
		double value = Math.Clamp((double)currentHp * 100.0 / (double)maxHp, 0.0, 100.0);
		return $"{value:0.0}%";
	}

	private System.Windows.Media.Brush FindBrush(string resourceKey)
	{
		return (TryFindResource(resourceKey) as System.Windows.Media.Brush) ?? System.Windows.Media.Brushes.White;
	}

	private void ApplyTopTargetThemeBrushes()
	{
		borderTopTarget.SetResourceReference(Border.BackgroundProperty, "BossCardBackgroundBrush");
		borderTopTarget.SetResourceReference(Border.BorderBrushProperty, "BossCardBorderBrush");
		txtTopTargetName.SetResourceReference(TextBlock.ForegroundProperty, "BossCardNameBrush");
		txtTopTargetDuration.SetResourceReference(TextBlock.ForegroundProperty, "BossCardDurationBrush");
		txtTopTargetDamage.SetResourceReference(TextBlock.ForegroundProperty, "DpsValueBrush");
		txtTopTargetInlineDamage.SetResourceReference(TextBlock.ForegroundProperty, "DpsValueBrush");
		txtTopTargetInlineDuration.SetResourceReference(TextBlock.ForegroundProperty, "BossCardDurationBrush");
		txtTopTargetHpValue.SetResourceReference(TextBlock.ForegroundProperty, "BossCardHpCurrentBrush");
		txtTopTargetHpPercent.SetResourceReference(TextBlock.ForegroundProperty, "BossCardHpMaxBrush");
		txtTopTargetHits.SetResourceReference(TextBlock.ForegroundProperty, "BossCardHitsBrush");
		bossHpTrack.SetResourceReference(Border.BackgroundProperty, "BossHpTrackBrush");
		bossHpFill.SetResourceReference(Border.BackgroundProperty, "BossHpFillBrush");
	}

	private void ApplyTopTargetFrameLayout(bool neon)
	{
		borderTopTarget.ClipToBounds = true;
		if (!neon || bossNeonFrameHost == null)
		{
			borderTopTarget.SetResourceReference(Border.BorderThicknessProperty, "ThemeBossCardBorderThickness");
			if (bossNeonFrameHost != null)
			{
				bossNeonFrameHost.Visibility = Visibility.Collapsed;
			}
		}
		else
		{
			borderTopTarget.BorderThickness = new Thickness(0.0);
			Thickness padding = borderTopTarget.Padding;
			bossNeonFrameHost.Visibility = Visibility.Visible;
			bossNeonFrameHost.Margin = new Thickness(0.0 - padding.Left, 0.0 - padding.Top, 0.0, 0.0);
		}
	}

	private Thickness AlignContentHorizontalInset(Thickness margin)
	{
		return new Thickness(ContentHorizontalInset, margin.Top, ContentHorizontalInset, margin.Bottom);
	}

	private void UpdateTopTargetCard(CombatSnapshot snap)
	{
		bool num = borderTopTarget.Visibility != Visibility.Visible || _lastAnimatedTopTargetId != snap.TopTargetId;
		borderTopTarget.Visibility = Visibility.Visible;
		string targetName = (string.IsNullOrWhiteSpace(snap.TopTargetName) ? $"Actor {snap.TopTargetId}" : snap.TopTargetName);
		bool minimal = _displayPreset == MeterDisplayPreset.Minimal;
		SetTopTargetName(snap, targetName, minimal);
		txtTopTargetDamage.Text = $"{snap.TopTargetDamage:N0}";
		txtTopTargetHits.Text = "";
		txtTopTargetHits.Visibility = Visibility.Collapsed;
		txtTopTargetDuration.Text = FormatCombatDuration(snap.TopTargetDuration);
		string name;
		bool isPlayerTarget = _engine.TryGetActorName(snap.TopTargetId, out name) && name != null && name.Contains("[");
		SetTopTargetStatus(snap, isPlayerTarget);
		SetTopTargetInlineMetrics(snap);
		UpdateBossHpBar(snap);
		ApplyTopTargetLayout(minimal);
		if (num)
		{
			_lastAnimatedTopTargetId = snap.TopTargetId;
			DpsUiAnimations.PlayBossCardEnter(borderTopTarget);
		}
	}

	private void ApplyTopTargetLayout(bool minimal)
	{
		bool isAbyssTheme = IsAbyssTheme;
		bool isAetherVeilTheme = IsAetherVeilTheme;
		bool isDefaultSkin = IsDefaultSkin;
		double layoutScale = Math.Clamp(_meterLayoutScale * ((IsBloomTheme && !isAbyssTheme) ? 0.94 : 1.0), 0.75, 1.7);
		double textScale = Math.Clamp(_meterTextScale * ((IsBloomTheme && !isAbyssTheme) ? 0.96 : 1.0), 0.6, 1.4);
		bool isNeonTheme = IsNeonTheme;
		bool flag = IsBloomTheme || isNeonTheme;
		if (minimal)
		{
			borderTopTarget.MinHeight = Dim(_isHudMode ? 28 : 30);
			borderTopTarget.Margin = AlignContentHorizontalInset(Scale(_isHudMode ? new Thickness(0.0, 2.0, 0.0, 0.0) : new Thickness(0.0, 2.0, 0.0, 0.0)));
			borderTopTarget.Padding = Scale(_isHudMode ? new Thickness(7.0, 2.0, 7.0, 2.0) : new Thickness(8.0, 2.0, 8.0, 2.0));
			ApplyTopTargetFrameLayout(isNeonTheme);
			borderTopTarget.Opacity = 1.0;
			ApplyTopTargetThemeBrushes();
			topTargetRootGrid.VerticalAlignment = VerticalAlignment.Center;
			topTargetInfoGrid.Margin = new Thickness(0.0);
			double num = Font((!isAbyssTheme) ? ((!isNeonTheme) ? ((!(isAetherVeilTheme || isDefaultSkin)) ? ((!IsBloomTheme) ? ((double)(_isHudMode ? 11 : 12)) : (_isHudMode ? 12.2 : 13.5)) : (_isHudMode ? 11.8 : 13.0)) : (_isHudMode ? 12.8 : 14.0)) : (_isHudMode ? 11.8 : 13.4));
			double num2 = Font(_isHudMode ? 9.2 : 9.8);
			double num3 = num + 1.0;
			double lineHeight = num2 + 1.0;
			topTargetNameTextRow.Height = new GridLength(num3);
			topTargetHpTextRow.Height = new GridLength(0.0);
			topTargetNeonHpRow.Height = new GridLength(0.0);
			topTargetHpRow.Height = new GridLength(0.0);
			bossHpTrack.Height = Dim(3.0);
			bossHpTrack.Margin = new Thickness(0.0, Dim(1.0), 0.0, 0.0);
			bdTargetIcon.Visibility = Visibility.Collapsed;
			colTargetIcon.Width = new GridLength(0.0);
			txtTopTargetType.Visibility = Visibility.Collapsed;
			topTargetDamageStack.Visibility = Visibility.Collapsed;
			txtTopTargetDuration.Visibility = Visibility.Collapsed;
			topTargetInlineMetricsStack.Visibility = Visibility.Visible;
			topTargetDamageStack.Margin = new Thickness(Dim(10.0), 0.0, 0.0, 0.0);
			txtTopTargetDamageLabel.Visibility = Visibility.Collapsed;
			bossHpTrack.Visibility = Visibility.Collapsed;
			if (neonBossHpBar != null)
			{
				neonBossHpBar.Visibility = Visibility.Collapsed;
			}
			txtTopTargetName.FontSize = num;
			txtTopTargetName.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
			txtTopTargetName.VerticalAlignment = VerticalAlignment.Center;
			txtTopTargetName.MaxWidth = Dim(_isHudMode ? 170 : 210);
			txtTopTargetName.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetName.LineHeight = num3;
			topTargetHpSummaryStack.Margin = new Thickness(Dim(8.0), 0.0, 0.0, 0.0);
			topTargetHpSummaryStack.Visibility = (string.IsNullOrWhiteSpace(txtTopTargetHpPercent.Text) ? Visibility.Collapsed : Visibility.Visible);
			txtTopTargetHpValue.Visibility = Visibility.Visible;
			txtTopTargetHpValue.FontSize = num2;
			txtTopTargetHpValue.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetHpValue.LineHeight = lineHeight;
			txtTopTargetHpPercent.Margin = new Thickness(Dim(6.0), 0.0, 0.0, 0.0);
			txtTopTargetHpPercent.FontSize = num2;
			txtTopTargetHpPercent.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetHpPercent.LineHeight = lineHeight;
			txtTopTargetType.FontSize = num2;
			txtTopTargetType.Margin = new Thickness(0.0);
			txtTopTargetType.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetType.LineHeight = lineHeight;
			txtTopTargetInlineDamage.FontSize = num2;
			txtTopTargetInlineDamage.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetInlineDamage.LineHeight = lineHeight;
			txtTopTargetInlineDuration.FontSize = num2;
			txtTopTargetInlineDuration.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetInlineDuration.LineHeight = lineHeight;
			txtTopTargetDamage.FontSize = Font(12.0);
			txtTopTargetDamage.Margin = new Thickness(0.0);
			txtTopTargetDamage.SetResourceReference(TextBlock.ForegroundProperty, "DpsValueBrush");
			txtTopTargetDuration.FontSize = Font((!(isAetherVeilTheme || isDefaultSkin)) ? ((!flag) ? (_isHudMode ? 10.5 : 11.0) : (_isHudMode ? 11.0 : 11.5)) : (_isHudMode ? 10.4 : 10.8));
			txtTopTargetDuration.SetResourceReference(TextBlock.ForegroundProperty, "BossCardDurationBrush");
			return;
		}
		borderTopTarget.MinHeight = 0.0;
		ApplyTopTargetThemeBrushes();
		topTargetRootGrid.VerticalAlignment = VerticalAlignment.Stretch;
		borderTopTarget.Margin = AlignContentHorizontalInset(Scale((!isNeonTheme) ? ((!isAbyssTheme) ? ((!(isAetherVeilTheme || isDefaultSkin)) ? ((!IsBloomTheme) ? (_isHudMode ? new Thickness(0.0, HudTopTargetMargin.Top, 0.0, HudTopTargetMargin.Bottom) : new Thickness(0.0, NormalTopTargetMargin.Top, 0.0, NormalTopTargetMargin.Bottom)) : (_isHudMode ? new Thickness(0.0, 6.0, 0.0, 2.0) : new Thickness(0.0, 6.0, 0.0, 4.0))) : (_isHudMode ? new Thickness(0.0, 2.0, 0.0, 0.0) : new Thickness(0.0, 2.0, 0.0, 0.0))) : (_isHudMode ? new Thickness(0.0, 2.0, 0.0, 3.0) : new Thickness(0.0, 3.0, 0.0, 4.0))) : (_isHudMode ? new Thickness(0.0, 5.0, 0.0, 2.0) : new Thickness(0.0, 5.0, 0.0, 3.0))));
		borderTopTarget.Padding = Scale((!isNeonTheme) ? ((!isAbyssTheme) ? ((!(isAetherVeilTheme || isDefaultSkin)) ? ((!IsBloomTheme) ? (_isHudMode ? HudTopTargetPadding : new Thickness(10.0, 4.0, 10.0, 3.0)) : (_isHudMode ? new Thickness(9.0, 5.0, 9.0, 5.0) : new Thickness(11.0, 7.0, 11.0, 7.0))) : (_isHudMode ? new Thickness(6.0, 2.0, 6.0, 2.0) : new Thickness(7.5, 3.0, 7.5, 3.0))) : (_isHudMode ? new Thickness(8.0, 12.0, 8.0, 11.0) : new Thickness(10.0, 13.0, 10.0, 12.0))) : (_isHudMode ? new Thickness(8.0, 3.0, 8.0, 2.0) : new Thickness(10.0, 3.0, 10.0, 3.0)));
		ApplyTopTargetFrameLayout(isNeonTheme);
		borderTopTarget.Opacity = 1.0;
		topTargetInfoGrid.Margin = (isAbyssTheme ? new Thickness(0.0, Dim(6.8), 0.0, Dim(2.8)) : new Thickness(0.0, Dim((!(isNeonTheme || isDefaultSkin)) ? (isAetherVeilTheme ? 1 : ((!flag) ? 1 : 2)) : 0), 0.0, Dim((!(isNeonTheme || isDefaultSkin)) ? (isAetherVeilTheme ? 1 : ((!flag) ? 1 : 2)) : 0)));
		bdTargetIcon.Visibility = ((_isHudMode && !flag) ? Visibility.Collapsed : Visibility.Visible);
		topTargetHpSummaryStack.Visibility = Visibility.Collapsed;
		txtTopTargetDuration.Visibility = Visibility.Visible;
		topTargetInlineMetricsStack.Visibility = Visibility.Collapsed;
		double num4 = Dim((!isAbyssTheme) ? ((!isNeonTheme) ? ((!(isAetherVeilTheme || isDefaultSkin)) ? ((!IsBloomTheme) ? 30 : (_isHudMode ? 38 : 44)) : (_isHudMode ? 30 : 38)) : (_isHudMode ? 39 : 46)) : (_isHudMode ? 38 : 44));
		bdTargetIcon.Width = (isNeonTheme ? (num4 * 1.16) : num4);
		bdTargetIcon.Height = num4;
		bdTargetIcon.CornerRadius = (isNeonTheme ? new CornerRadius(Dim(3.0)) : new CornerRadius(num4 / 2.0));
		bdTargetIcon.BorderThickness = (isNeonTheme ? new Thickness(0.0) : new Thickness(1.0));
		bdTargetIcon.Margin = new Thickness(Dim(isNeonTheme ? (-5) : 0), 0.0, Dim(isNeonTheme ? 8 : (isAbyssTheme ? 10 : ((isAetherVeilTheme || isDefaultSkin) ? 13 : (flag ? 12 : 9)))), 0.0);
		colTargetIcon.Width = ((_isHudMode && !flag) ? new GridLength(0.0) : GridLength.Auto);
		txtTopTargetType.Visibility = Visibility.Visible;
		topTargetDamageStack.Visibility = Visibility.Visible;
		topTargetDamageStack.Margin = new Thickness(Dim(isAbyssTheme ? 8 : ((isAetherVeilTheme || isDefaultSkin) ? 12 : 14)), 0.0, 0.0, 0.0);
		Grid.SetRow(topTargetDamageStack, 1);
		Grid.SetRowSpan(topTargetDamageStack, isDefaultSkin ? 1 : 2);
		txtTopTargetDamageLabel.SetResourceReference(UIElement.VisibilityProperty, "ThemeAbyssDamageLabelVisibility");
		txtTopTargetName.FontSize = Font((!isAbyssTheme) ? (isNeonTheme ? ((double)(_isHudMode ? 15 : 17)) : ((!isDefaultSkin) ? ((!isAetherVeilTheme) ? ((!IsBloomTheme) ? 13.0 : (_isHudMode ? 14.5 : 16.0)) : (_isHudMode ? 13.2 : 14.5)) : (_isHudMode ? 12.2 : 13.4))) : (_isHudMode ? 12.6 : 14.0));
		txtTopTargetName.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
		txtTopTargetName.VerticalAlignment = VerticalAlignment.Center;
		txtTopTargetName.MaxWidth = double.PositiveInfinity;
		txtTopTargetType.FontSize = Font(isAbyssTheme ? 9.6 : ((isAetherVeilTheme || isDefaultSkin) ? 10.4 : (flag ? 11.2 : 10.5)));
		txtTopTargetType.Margin = new Thickness(0.0, Dim(isAbyssTheme ? 1.4 : (isNeonTheme ? 0.0 : ((isAetherVeilTheme || isDefaultSkin) ? 1.5 : ((double)(flag ? 3 : 2))))), 0.0, 0.0);
		double fontSize = Font(isAbyssTheme ? 10.2 : ((isAetherVeilTheme || isDefaultSkin) ? 11.0 : (flag ? 12.0 : 10.5)));
		double num5 = Font(_isHudMode ? 17.0 : 19.2);
		double num6 = Font(_isHudMode ? 13.0 : 14.0);
		if (isNeonTheme)
		{
			topTargetNameTextRow.Height = new GridLength(num5);
			topTargetHpTextRow.Height = new GridLength(num6);
			txtTopTargetName.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetName.LineHeight = num5;
			txtTopTargetType.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetType.LineHeight = num6;
		}
		else if (isAbyssTheme)
		{
			double num7 = Font(_isHudMode ? 17.8 : 19.2);
			double num8 = Font(_isHudMode ? 14.2 : 15.0);
			topTargetNameTextRow.Height = new GridLength(num7);
			topTargetHpTextRow.Height = new GridLength(num8);
			txtTopTargetName.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetName.LineHeight = num7;
			txtTopTargetType.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetType.LineHeight = num8;
		}
		else if (isAetherVeilTheme || isDefaultSkin)
		{
			double num9 = Font((!isDefaultSkin) ? (_isHudMode ? 17.0 : 18.2) : (_isHudMode ? 15.8 : 16.8));
			double num10 = Font((!isDefaultSkin) ? (_isHudMode ? 12.5 : 13.2) : (_isHudMode ? 11.6 : 12.4));
			topTargetNameTextRow.Height = new GridLength(num9);
			topTargetHpTextRow.Height = new GridLength(num10);
			txtTopTargetName.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetName.LineHeight = num9;
			txtTopTargetType.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetType.LineHeight = num10;
		}
		else
		{
			topTargetNameTextRow.Height = GridLength.Auto;
			topTargetHpTextRow.Height = GridLength.Auto;
			txtTopTargetName.ClearValue(TextBlock.LineStackingStrategyProperty);
			txtTopTargetName.ClearValue(TextBlock.LineHeightProperty);
			txtTopTargetType.ClearValue(TextBlock.LineStackingStrategyProperty);
			txtTopTargetType.ClearValue(TextBlock.LineHeightProperty);
		}
		txtTopTargetDamage.FontSize = txtTopTargetType.FontSize;
		txtTopTargetDamage.Margin = new Thickness(0.0);
		if (isDefaultSkin)
		{
			txtTopTargetDamage.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
			txtTopTargetDamage.LineHeight = txtTopTargetType.LineHeight;
		}
		else
		{
			txtTopTargetDamage.ClearValue(TextBlock.LineStackingStrategyProperty);
			txtTopTargetDamage.ClearValue(TextBlock.LineHeightProperty);
		}
		txtTopTargetHits.FontSize = Font(flag ? 12.5 : 12.0);
		txtTopTargetHits.Visibility = Visibility.Collapsed;
		txtTopTargetDuration.FontSize = fontSize;
		txtTopTargetDamage.SetResourceReference(TextBlock.ForegroundProperty, "DpsValueBrush");
		txtTopTargetHits.SetResourceReference(TextBlock.ForegroundProperty, "BossCardHitsBrush");
		txtTopTargetDuration.SetResourceReference(TextBlock.ForegroundProperty, "BossCardDurationBrush");
		double num11 = Dim(isNeonTheme ? 3.5 : (isAbyssTheme ? 3.6 : (isDefaultSkin ? 3.2 : (isAetherVeilTheme ? 3.6 : (IsBloomTheme ? 5.5 : 3.0)))));
		double num12 = Dim(isNeonTheme ? 2.0 : (isAbyssTheme ? 4.0 : (isDefaultSkin ? 1.4 : (isAetherVeilTheme ? 2.2 : ((double)((!IsBloomTheme) ? 1 : 3))))));
		topTargetNeonHpRow.Height = new GridLength(isNeonTheme ? (num11 + Dim(6.0)) : (num11 + num12 + Dim((isAetherVeilTheme || isDefaultSkin) ? 1 : ((!IsBloomTheme) ? 1 : 2))));
		topTargetHpRow.Height = new GridLength(0.0);
		bossHpTrack.Height = num11;
		bossHpTrack.CornerRadius = new CornerRadius(num11 / 2.0);
		bossHpFill.CornerRadius = bossHpTrack.CornerRadius;
		bossHpTrack.Margin = new Thickness(0.0, num12, Dim(isAbyssTheme ? 28 : ((!isDefaultSkin) ? (isAetherVeilTheme ? 40 : (IsBloomTheme ? 48 : 12)) : 0)), 0.0);
		Grid.SetRow(bossHpTrack, 2);
		Grid.SetColumn(bossHpTrack, 0);
		Grid.SetColumnSpan(bossHpTrack, (!isDefaultSkin) ? 1 : 2);
		if (neonBossHpBar != null)
		{
			Grid.SetRow(neonBossHpBar, (!isNeonTheme) ? 1 : 2);
			Grid.SetColumn(neonBossHpBar, 0);
			Grid.SetColumnSpan(neonBossHpBar, isNeonTheme ? 1 : 2);
			neonBossHpBar.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
			neonBossHpBar.VerticalAlignment = ((!isNeonTheme) ? VerticalAlignment.Stretch : VerticalAlignment.Top);
			neonBossHpBar.Height = num11;
			neonBossHpBar.Margin = (isNeonTheme ? new Thickness(0.0, Dim(2.0), Dim(12.0), 0.0) : bossHpTrack.Margin);
		}
		UpdateBossHpFillWidth();
		double Dim(double value)
		{
			return MeterVisualScale.Dimension(value, layoutScale);
		}
		double Font(double value)
		{
			return MeterVisualScale.Font(value, textScale, _meterFontSizeDelta);
		}
		Thickness Scale(Thickness value)
		{
			return MeterVisualScale.ScaleThickness(value, layoutScale);
		}
	}

	private void UpdateBossHpBar(CombatSnapshot snap)
	{
		if (snap.TopTargetMaxHp <= 0 || (snap.TopTargetCurrentHp < 0 && _encounterViewKind != EncounterViewKind.ArchivedBoss))
		{
			SetBossHpBar(0.0, visible: false);
			return;
		}
		double ratio = Math.Clamp((double)GetTopTargetCurrentHpForDisplay(snap) / (double)snap.TopTargetMaxHp, 0.0, 1.0);
		SetBossHpBar(ratio, visible: true);
	}

	private void SetBossHpBar(double ratio, bool visible)
	{
		_bossHpRatio = ratio;
		bool isNeonTheme = IsNeonTheme;
		if (bossHpTrack != null)
		{
			bossHpTrack.Visibility = ((!visible || isNeonTheme) ? Visibility.Collapsed : Visibility.Visible);
		}
		if (neonBossHpBar != null)
		{
			neonBossHpBar.Ratio = ratio;
			neonBossHpBar.Visibility = ((!(visible && isNeonTheme)) ? Visibility.Collapsed : Visibility.Visible);
		}
		UpdateBossHpFillWidth();
	}

	private void BossHpTrack_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateBossHpFillWidth();
	}

	private void UpdateBossHpFillWidth()
	{
		if (bossHpFill != null && bossHpTrack != null)
		{
			bossHpFill.Width = Math.Max(0.0, bossHpTrack.ActualWidth * _bossHpRatio);
			if (neonBossHpBar != null)
			{
				neonBossHpBar.Ratio = _bossHpRatio;
			}
		}
	}

	private static bool HasResolvedDpsCardName(ActorStats actor)
	{
		string text = actor.Name?.Trim() ?? "";
		int result;
		if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("Actor ", StringComparison.OrdinalIgnoreCase) && !text.Equals("Unknown Player", StringComparison.OrdinalIgnoreCase))
		{
			return !int.TryParse(text, out result);
		}
		return false;
	}

	private ActorStats ResolveLateDpsCardName(ActorStats actor)
	{
		if (HasResolvedDpsCardName(actor) || actor.ActorId <= 0 || !_engine.TryGetActorName(actor.ActorId, out string name) || string.IsNullOrWhiteSpace(name))
		{
			return actor;
		}
		var (text, serverName) = SplitPartyMemberName(name);
		if (string.IsNullOrWhiteSpace(text) || !HasResolvedDpsCardName(actor with
		{
			Name = text
		}))
		{
			return actor;
		}
		return actor with
		{
			Name = text,
			ServerName = serverName
		};
	}

	private void RenderTiles(CombatSnapshot? snap)
	{
		if (snap == null)
		{
			_combatTimeBadgeText = "00:00";
			_combatTimeText = "전투 시간: 00:00.0";
			_detailWindow?.SetCombatTime(_combatTimeText);
			ApplyMainContentView();
			return;
		}
		TryAutoShowDps(snap);
		_combatTimeBadgeText = FormatCombatDuration(snap.SessionDuration);
		_combatTimeText = "전투 시간: " + FormatCombatDurationDetailed(snap.SessionDuration);
		if (!IsCombatDetailWindowOpen())
		{
			_detailWindow?.SetCombatTime(_combatTimeText);
		}
		if (snap.Actors.Count == 0 && DpsCards.Count > 0 && GetSelectedTargetId() != 0)
		{
			ApplyMainContentView();
			return;
		}
		if (ShouldShowTopTargetCard(snap))
		{
			UpdateTopTargetCard(snap);
		}
		else
		{
			borderTopTarget.Visibility = Visibility.Collapsed;
			_lastAnimatedTopTargetId = 0;
			SetBossHpBar(0.0, visible: false);
		}
		int selectedTargetId = GetSelectedTargetId();
		bool isBossTargetSnapshot = snap.IsBossConfirmed && snap.TopTargetId > 0 && (selectedTargetId == 0 || selectedTargetId == snap.TopTargetId);
		bool flag = CanFetchAverageDpsForSnapshot(snap);
		IEnumerable<ActorStats> source = from a in snap.Actors.Select(ResolveLateDpsCardName)
			where !a.IsMonster && a.ActorId != snap.TopTargetId && (selectedTargetId == 0 || a.ActorId != selectedTargetId) && (isBossTargetSnapshot || (!a.Name.StartsWith("Boss ") && !a.Name.StartsWith("Mob ") && !a.Name.StartsWith("Mob_")))
			select a;
		source = source.Where((ActorStats a) => _nameValidRegex.IsMatch(a.Name));
		if (isBossTargetSnapshot)
		{
			source = source.Where(HasResolvedDpsCardName);
		}
		if (!isBossTargetSnapshot && chkShowUnknown.IsChecked == true)
		{
			source = source.Where((ActorStats a) => (!a.Name.StartsWith("Actor ") && !int.TryParse(a.Name, out var _)) || a.Hits >= 5);
		}
		if (!isBossTargetSnapshot && chkShowUnknown.IsChecked == true)
		{
			source = source.Where((ActorStats a) => !string.IsNullOrWhiteSpace(a.Name) && !a.Name.StartsWith("Actor ") && !int.TryParse(a.Name, out var _));
		}
		int result;
		if (!isBossTargetSnapshot && cmbFilterClass.SelectedIndex > 0)
		{
			result = cmbFilterClass.SelectedIndex;
			JobClass filterJob = result switch
			{
				1 => JobClass.Gladiator, 
				2 => JobClass.Templar, 
				3 => JobClass.Ranger, 
				4 => JobClass.Assassin, 
				5 => JobClass.Sorcerer, 
				6 => JobClass.Spiritmaster, 
				7 => JobClass.Cleric, 
				8 => JobClass.Chanter, 
				_ => JobClass.None, 
			};
			source = source.Where((ActorStats a) => a.Job == filterJob);
		}
		List<ActorStats> source2 = (from a in source
			orderby a.Dps descending, a.Hps descending
			select a).ToList();
		long totalPartyDamage = Math.Max(1L, source2.Sum((ActorStats x) => x.TotalDamage));
		int count = Math.Clamp(_maxDpsCards, 1, 10);
		List<ActorStats> list = source2.Take(count).ToList();
		if (list.Count == 0 && DpsCards.Count > 0 && selectedTargetId != 0)
		{
			ApplyMainContentView();
			return;
		}
		HashSet<int> hashSet = new HashSet<int>(list.Select((ActorStats a) => a.ActorId));
		for (int num = DpsCards.Count - 1; num >= 0; num--)
		{
			if (!hashSet.Contains(DpsCards[num].ActorId))
			{
				DpsCards.RemoveAt(num);
			}
		}
		Dictionary<int, DpsCardViewModel> dictionary = new Dictionary<int, DpsCardViewModel>();
		foreach (DpsCardViewModel dpsCard in DpsCards)
		{
			dictionary[dpsCard.ActorId] = dpsCard;
		}
		double topDisplayRatio = ((list.Count == 0) ? 0.0 : list.Max((ActorStats actor) => GetDamageShareDisplay(actor, snap, totalPartyDamage).Ratio));
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			ActorStats actorStats = list[num2];
			dictionary.TryGetValue(actorStats.ActorId, out var value);
			string text = (string.IsNullOrWhiteSpace(actorStats.Name) ? $"Actor {actorStats.ActorId}" : actorStats.Name);
			string text2 = text;
			if (chkHideNickname.IsChecked == true && !text.StartsWith("Actor "))
			{
				text2 = GetAnonymousName(text, actorStats.ActorId);
			}
			if (_showActorId && !text.StartsWith("Actor ") && !int.TryParse(text, out result))
			{
				text2 += $" ({actorStats.ActorId})";
			}
			string text3 = FormatDpsCardDps(actorStats.Dps);
			string text4 = $"{actorStats.CritRate * 100.0:0.0}%";
			string text5 = FormatDpsCardTotalDamage(actorStats.TotalDamage);
			(double, string, bool) damageShareDisplay = GetDamageShareDisplay(actorStats, snap, totalPartyDamage);
			double damageShareBackgroundRatio = GetDamageShareBackgroundRatio(actorStats, totalPartyDamage, damageShareDisplay.Item1, topDisplayRatio);
			string text6 = FormatActorCombatDuration(actorStats, snap.SessionDuration);
			string text8;
			string text7 = (TryGetPacketCombatPowerText(text, actorStats.ServerName, out text8) ? text8 : "");
			int aion2ServerId = PartyTracker.GetAion2ServerId(actorStats.ServerName);
			bool flag2 = ShouldShowMeterUserMarker(text, aion2ServerId);
			bool flag3 = flag && aion2ServerId > 0 && !string.IsNullOrWhiteSpace(text) && !text.StartsWith("Actor ", StringComparison.Ordinal) && !int.TryParse(text, out result);
			string text9 = (flag3 ? GetAverageDpsCacheKey(text, aion2ServerId) : "");
			bool flag4 = value != null && !string.Equals(value.AverageDpsScopeKey, text9, StringComparison.Ordinal);
			bool flag5 = flag3 && (ShouldAutoFetchCombatScore(actorStats) || flag4);
			if (value == null)
			{
				value = new DpsCardViewModel
				{
					ActorId = actorStats.ActorId,
					CharacterName = text,
					Job = actorStats.Job,
					TotalDamage = actorStats.TotalDamage,
					TotalHealing = actorStats.TotalHealing,
					Name = text2,
					ServerName = actorStats.ServerName,
					DpsText = text3,
					SubText = text5,
					DamageShareRatio = damageShareDisplay.Item1,
					DamageShareBackgroundRatio = damageShareBackgroundRatio,
					DamageSharePctText = damageShareDisplay.Item2,
					IsBossHpShareMode = damageShareDisplay.Item3,
					CombatTimeText = text6,
					ShowCombatTime = _showDpsCardCombatTime,
					CritRateText = text4,
					Hits = actorStats.Hits,
					IsHudMode = _isHudMode,
					UiScale = _meterUiScale,
					DisplayPreset = _displayPreset,
					Theme = CurrentThemeName,
					FontWeightMode = _fontWeightMode,
					TextShadowEnabled = _textShadowEnabled,
					CombatPower = text7,
					IsMeterUserOnline = flag2,
					AverageDpsScopeKey = text9
				};
				value.SetVisualScale(_meterLayoutScale, _meterTextScale, _meterFontSizeDelta);
				DpsCards.Add(value);
				DpsUiAnimations.PlayItemEnter(lstDps, value);
				if (flag5)
				{
					FetchCombatScoreAsync(value, actorStats.Name, actorStats.ServerName);
				}
				continue;
			}
			if (value.CharacterName != text)
			{
				value.CharacterName = text;
			}
			if (value.Job != actorStats.Job)
			{
				value.Job = actorStats.Job;
			}
			if (value.Name != text2)
			{
				value.Name = text2;
			}
			if (value.ServerName != actorStats.ServerName)
			{
				value.ServerName = actorStats.ServerName;
			}
			if (!string.IsNullOrEmpty(text7) && value.CombatPower != text7)
			{
				value.CombatPower = text7;
			}
			if (value.DpsText != text3)
			{
				value.DpsText = text3;
			}
			if (value.SubText != text5)
			{
				value.SubText = text5;
			}
			if (value.CombatTimeText != text6)
			{
				value.CombatTimeText = text6;
			}
			if (value.ShowCombatTime != _showDpsCardCombatTime)
			{
				value.ShowCombatTime = _showDpsCardCombatTime;
			}
			if (value.CritRateText != text4)
			{
				value.CritRateText = text4;
			}
			value.TotalDamage = actorStats.TotalDamage;
			value.TotalHealing = actorStats.TotalHealing;
			var (num3, _, _) = damageShareDisplay;
			if (Math.Abs(value.DamageShareRatio - num3) > 0.05)
			{
				value.DamageShareRatio = num3;
			}
			if (Math.Abs(value.DamageShareBackgroundRatio - damageShareBackgroundRatio) > 0.05)
			{
				value.DamageShareBackgroundRatio = damageShareBackgroundRatio;
			}
			string item = damageShareDisplay.Item2;
			if (value.DamageSharePctText != item)
			{
				value.DamageSharePctText = item;
			}
			if (value.IsBossHpShareMode != damageShareDisplay.Item3)
			{
				value.IsBossHpShareMode = damageShareDisplay.Item3;
			}
			if (value.Hits != actorStats.Hits)
			{
				value.Hits = actorStats.Hits;
			}
			if (value.IsHudMode != _isHudMode)
			{
				value.IsHudMode = _isHudMode;
			}
			if (value.DisplayPreset != _displayPreset)
			{
				value.DisplayPreset = _displayPreset;
			}
			if (!string.Equals(value.Theme, CurrentThemeName, StringComparison.OrdinalIgnoreCase))
			{
				value.Theme = CurrentThemeName;
			}
			if (value.FontWeightMode != _fontWeightMode)
			{
				value.FontWeightMode = _fontWeightMode;
			}
			if (value.TextShadowPreference != _textShadowEnabled)
			{
				value.TextShadowEnabled = _textShadowEnabled;
			}
			if (value.IsMeterUserOnline != flag2)
			{
				value.IsMeterUserOnline = flag2;
			}
			if (!flag3)
			{
				ClearAverageDps(value);
			}
			else if (flag4 && flag5)
			{
				value.AverageDpsScopeKey = text9;
				value.CombatScore = "조회 중...";
				value.IsDungeonAverageDps = false;
			}
			if ((string.IsNullOrEmpty(value.CombatScore) || flag4) && flag5)
			{
				FetchCombatScoreAsync(value, actorStats.Name, actorStats.ServerName, flag4);
			}
		}
		ApplyDpsRankOrder((from c in DpsCards
			orderby c.TotalDamage descending, c.TotalHealing descending
			select c).ToList());
		for (int num4 = 0; num4 < DpsCards.Count; num4++)
		{
			bool flag6 = num4 == 0 && DpsCards[num4].TotalDamage > 0;
			if (DpsCards[num4].IsTopDamageRank != flag6)
			{
				DpsCards[num4].IsTopDamageRank = flag6;
			}
		}
		int? selectedDetailActorId = ResolveSelectedDetailActorId();
		if (selectedDetailActorId.HasValue && lstDps.SelectedItem == null)
		{
			lstDps.SelectedItem = DpsCards.FirstOrDefault((DpsCardViewModel x) => x.ActorId == selectedDetailActorId.Value);
		}
		ApplyMeterScale();
		ApplyMainContentView();
	}

	private void ApplyDpsRankOrder(IReadOnlyList<DpsCardViewModel> sorted)
	{
		if (sorted.Count != DpsCards.Count)
		{
			return;
		}
		bool flag = false;
		for (int i = 0; i < sorted.Count; i++)
		{
			if (DpsCards[i] != sorted[i])
			{
				flag = true;
				break;
			}
		}
		if (!flag)
		{
			return;
		}
		DateTime utcNow = DateTime.UtcNow;
		if (utcNow - _lastDpsRankReorderUtc < DpsRankReorderInterval)
		{
			return;
		}
		IReadOnlyDictionary<object, double> previousTops = DpsUiAnimations.CaptureItemTops(lstDps);
		for (int j = 0; j < sorted.Count; j++)
		{
			int num = DpsCards.IndexOf(sorted[j]);
			if (num >= 0 && num != j)
			{
				DpsCards.Move(num, j);
			}
		}
		DpsUiAnimations.AnimateItemsFrom(lstDps, previousTops);
		_lastDpsRankReorderUtc = utcNow;
	}

	private void ResetDpsRankReorderClock()
	{
		_lastDpsRankReorderUtc = DateTime.MinValue;
	}

	private bool ShouldShowTopTargetCard(CombatSnapshot snap)
	{
		if (_showBossCard && snap.TopTargetDamage > 0)
		{
			return snap.IsBossActive;
		}
		return false;
	}

	private void RenderActorDetail(int actorId)
	{
		if (_detailWindow != null)
		{
			RememberAutomaticDetailRenderSignature(actorId);
			if (_detailWindow.IsSkillTabSelected)
			{
				RenderSkillOrHealingDetail(actorId, renderSkillRows: true, renderHealingRows: false);
			}
			else if (_detailWindow.IsHealingTabSelected)
			{
				RenderSkillOrHealingDetail(actorId, renderSkillRows: false, renderHealingRows: true);
			}
			else if (_detailWindow.IsDpsGraphTabSelected)
			{
				RenderDpsGraphOnly(actorId);
			}
			else if (_detailWindow.IsBuffTabSelected)
			{
				RenderBuffsOnly(actorId);
			}
			else if (_detailWindow.IsRdpsTabSelected)
			{
				RenderRdpsOnly(actorId);
			}
			else
			{
				RenderLogOnly(actorId);
			}
		}
	}

	private void QueueActorDetailRender(int actorId)
	{
		_queuedDetailActorId = actorId;
		if (_detailRenderQueued)
		{
			return;
		}
		_detailRenderQueued = true;
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			_detailRenderQueued = false;
			int? num = ResolveSelectedDetailActorId() ?? _queuedDetailActorId;
			_queuedDetailActorId = null;
			if (!_isPaused && num.HasValue && IsCombatDetailWindowOpen())
			{
				RenderActorDetail(num.Value);
			}
		}, DispatcherPriority.Background);
	}

	private bool IsCombatDetailWindowOpen()
	{
		return _detailWindow?.IsVisible ?? false;
	}

	private bool ShouldQueueAutomaticDetailRender(int actorId, long parsedDamageEvents, long parsedBuffEvents)
	{
		if (_detailWindow == null)
		{
			return false;
		}
		return !string.Equals(BuildAutomaticDetailRenderSignature(actorId, parsedDamageEvents, parsedBuffEvents), _lastAutoDetailRenderSignature, StringComparison.Ordinal);
	}

	private void RememberAutomaticDetailRenderSignature(int actorId)
	{
		_lastAutoDetailRenderSignature = BuildAutomaticDetailRenderSignature(actorId, Interlocked.Read(in _parsedDamageEvents), Interlocked.Read(in _parsedBuffEvents));
	}

	private string BuildAutomaticDetailRenderSignature(int actorId, long parsedDamageEvents, long parsedBuffEvents)
	{
		CombatDetailWindow? detailWindow = _detailWindow;
		bool num = (detailWindow != null && detailWindow.IsBuffTabSelected) || (_detailWindow?.IsRdpsTabSelected ?? false);
		string value = ((_detailWindow == null) ? "" : (_detailWindow.IsSkillTabSelected ? "skills" : (_detailWindow.IsHealingTabSelected ? "healing" : (_detailWindow.IsDpsGraphTabSelected ? "graph" : (_detailWindow.IsBuffTabSelected ? "buffs" : (_detailWindow.IsRdpsTabSelected ? "rdps" : "log"))))));
		long value2 = (num ? parsedBuffEvents : 0);
		return $"{actorId}|{GetSelectedTargetId()}|{value}|{parsedDamageEvents}|{value2}|{_isLogViewMode}";
	}

	private void RenderSkillOrHealingDetail(int actorId, bool renderSkillRows, bool renderHealingRows)
	{
		int selectedTargetId = GetSelectedTargetId();
		UiActorState uiActorState = null;
		ArchivedBossRecord archivedRecord = null;
		ActorStats actor = null;
		CombatSnapshot snapshot = null;
		bool flag = selectedTargetId != 0 && _encounterViewKind != EncounterViewKind.LiveBoss && TryGetCoreTargetActorStats(actorId, selectedTargetId, out actor, out snapshot);
		if (!flag)
		{
			uiActorState = GetDetailActorState(actorId, out archivedRecord);
		}
		if (!flag && uiActorState == null)
		{
			SetDetailCombatTime(TimeSpan.Zero);
			_detailWindow?.SetSummary(CreateEmptyCombatDetailSummary(actorId));
			_detailWindow?.SetRdpsRows(Array.Empty<RdpsBuffRow>(), 0.0);
			_detailWindow?.SetSkillRows(Array.Empty<SkillRow>());
			_detailWindow?.SetHealingRows(Array.Empty<SkillRow>());
			return;
		}
		double num;
		long num2;
		long totalHealing;
		List<UiSkillState> skills;
		if (flag)
		{
			num = Math.Max(1.0, snapshot.TopTargetDuration.TotalSeconds);
			num2 = actor.TotalDamage;
			totalHealing = actor.TotalHealing;
			skills = (from s in (actor.Skills ?? Array.Empty<SkillStats>()).Select(UiSkillState.FromSkillStats)
				orderby s.TotalDamage descending, s.TotalHealing descending
				select s).ToList();
			if (num2 <= 0 && totalHealing <= 0)
			{
				SetDetailCombatTime(TimeSpan.Zero);
				_detailWindow?.SetSummary(CreateEmptyCombatDetailSummary(actorId));
				_detailWindow?.SetRdpsRows(Array.Empty<RdpsBuffRow>(), 0.0);
				_detailWindow?.SetSkillRows(Array.Empty<SkillRow>());
				_detailWindow?.SetHealingRows(Array.Empty<SkillRow>());
				return;
			}
		}
		else if (selectedTargetId == 0)
		{
			num = GetDetailDurationSeconds(Math.Max(1.0, (uiActorState.LastUtc - uiActorState.FirstUtc).TotalSeconds));
			num2 = uiActorState.TotalDamage;
			totalHealing = uiActorState.TotalHealing;
			skills = (from s in uiActorState.Skills.Values
				orderby s.TotalDamage descending, s.TotalHealing descending
				select s).ToList();
		}
		else
		{
			UiActorTargetState value;
			bool flag2 = uiActorState.Targets.TryGetValue(selectedTargetId, out value) && value.TotalDamage > 0;
			if (!flag2 && uiActorState.TotalHealing <= 0)
			{
				SetDetailCombatTime(TimeSpan.Zero);
				_detailWindow?.SetSummary(CreateEmptyCombatDetailSummary(actorId));
				_detailWindow?.SetRdpsRows(Array.Empty<RdpsBuffRow>(), 0.0);
				_detailWindow?.SetSkillRows(Array.Empty<SkillRow>());
				_detailWindow?.SetHealingRows(Array.Empty<SkillRow>());
				return;
			}
			num = ((flag2 && value != null) ? Math.Max(1.0, (value.LastUtc - value.FirstUtc).TotalSeconds) : GetDetailDurationSeconds(Math.Max(1.0, (uiActorState.LastUtc - uiActorState.FirstUtc).TotalSeconds)));
			num2 = value?.TotalDamage ?? 0;
			totalHealing = uiActorState.TotalHealing;
			skills = (from s in ((flag2 && value != null) ? BuildTargetDetailSkills(value, uiActorState) : BuildHealingOnlyDetailSkills(uiActorState)).Values
				orderby s.TotalDamage descending, s.TotalHealing descending
				select s).ToList();
		}
		SetDetailCombatTime(TimeSpan.FromSeconds(num));
		_detailWindow?.SetSummary(BuildCombatDetailSummary(skills, num2, totalHealing, num, actorId));
		if (renderSkillRows)
		{
			_detailWindow?.SetSkillRows(BuildSkillRows(skills, num2, totalHealing, num));
		}
		if (renderHealingRows)
		{
			_detailWindow?.SetHealingRows(BuildHealingRows(skills, totalHealing, num));
		}
	}

	private static DetailSkillTotals AggregateDetailSkills(IEnumerable<UiSkillState> skills)
	{
		long num = 0L;
		long num2 = 0L;
		long num3 = 0L;
		long num4 = 0L;
		int num5 = 0;
		int num6 = 0;
		int num7 = 0;
		int num8 = 0;
		int num9 = 0;
		int num10 = 0;
		int num11 = 0;
		int num12 = 0;
		int num13 = 0;
		int num14 = 0;
		int num15 = 0;
		int num16 = int.MaxValue;
		int num17 = 0;
		int num18 = int.MaxValue;
		int num19 = 0;
		int num20 = int.MaxValue;
		SortedSet<int> sortedSet = new SortedSet<int>();
		foreach (UiSkillState skill in skills)
		{
			num += skill.TotalDamage;
			num2 += skill.TotalHealing;
			num3 += skill.SelfHealing;
			num4 += skill.OtherHealing;
			num5 += skill.HitCount;
			num6 += skill.HealCount;
			num7 += skill.CritCount;
			num8 += skill.NormalHitCount;
			num9 += skill.BackCount;
			num10 += skill.DoubleCount;
			num11 += skill.PerfectCount;
			num12 += skill.ParryCount;
			num13 += skill.EvadeCount;
			num14 += skill.SmiteCount;
			num15 += skill.MultiEventCount;
			num16 = Math.Min(num16, skill.MinDamage);
			num17 = Math.Max(num17, skill.MaxDamage);
			if (skill.MinHeal > 0 && skill.MinHeal != int.MaxValue)
			{
				num18 = Math.Min(num18, skill.MinHeal);
			}
			num19 = Math.Max(num19, skill.MaxHeal);
			num20 = Math.Min(num20, skill.SkillCode);
			sortedSet.Add(skill.SkillCode);
		}
		return new DetailSkillTotals
		{
			TotalDamage = num,
			TotalHealing = num2,
			SelfHealing = num3,
			OtherHealing = num4,
			HitCount = num5,
			HealCount = num6,
			CritCount = num7,
			NormalHitCount = num8,
			BackCount = num9,
			DoubleCount = num10,
			PerfectCount = num11,
			ParryCount = num12,
			EvadeCount = num13,
			SmiteCount = num14,
			MultiEventCount = num15,
			MinDamage = num16,
			MaxDamage = num17,
			MinHeal = ((num18 != int.MaxValue) ? num18 : 0),
			MaxHeal = num19,
			BestCode = ((num20 != int.MaxValue) ? num20 : 0),
			SkillCodes = sortedSet.ToArray()
		};
	}

	private List<SkillRow> BuildSkillRows(IReadOnlyList<UiSkillState> skills, long actorTotal, long actorHealing, double durationSeconds)
	{
		string sortProp = _detailWindow?.SkillSortMemberPath;
		ListSortDirection? sortDir = _detailWindow?.SkillSortDirection;
		double duration = Math.Max(1.0, durationSeconds);
		List<SkillRow> list = (from s in skills
			group s by _skillNames.GetDisplayGroupCode(s.SkillCode)).Select(delegate(IGrouping<int, UiSkillState> g)
		{
			DetailSkillTotals detailSkillTotals = AggregateDetailSkills(g);
			int num = Math.Max(1, detailSkillTotals.HitCount);
			double value = (double)detailSkillTotals.TotalDamage * 100.0 / (double)Math.Max(1L, actorTotal);
			double value2 = ((actorHealing > 0) ? ((double)detailSkillTotals.TotalHealing * 100.0 / (double)actorHealing) : 0.0);
			double value3 = ((actorHealing > 0) ? ((double)detailSkillTotals.SelfHealing * 100.0 / (double)actorHealing) : 0.0);
			double value4 = ((actorHealing > 0) ? ((double)detailSkillTotals.OtherHealing * 100.0 / (double)actorHealing) : 0.0);
			return new SkillRow
			{
				IconPath = GetSkillIconPath(detailSkillTotals.BestCode),
				Name = _skillNames.GetNameOrCode(detailSkillTotals.BestCode),
				TraitSlots = BuildTraitSlots(detailSkillTotals.SkillCodes),
				SkillCodesText = ((detailSkillTotals.SkillCodes.Length == 0) ? "" : string.Join(", ", detailSkillTotals.SkillCodes)),
				SpecialtyTooltip = _rdpsSkillCatalog.BuildSpecialtyTooltip(detailSkillTotals.SkillCodes),
				Hit = detailSkillTotals.HitCount,
				Crit = ((double)detailSkillTotals.CritCount * 100.0 / (double)num).ToString("0.0"),
				Normal = ((double)detailSkillTotals.NormalHitCount * 100.0 / (double)num).ToString("0.0"),
				Back = ((double)detailSkillTotals.BackCount * 100.0 / (double)num).ToString("0.0"),
				Double = ((double)detailSkillTotals.DoubleCount * 100.0 / (double)num).ToString("0.0"),
				Perfect = ((double)detailSkillTotals.PerfectCount * 100.0 / (double)num).ToString("0.0"),
				Parry = ((double)detailSkillTotals.ParryCount * 100.0 / (double)num).ToString("0.0"),
				Smite = ((double)detailSkillTotals.SmiteCount * 100.0 / (double)num).ToString("0.0"),
				Multi = ((double)detailSkillTotals.MultiEventCount * 100.0 / (double)num).ToString("0.0"),
				Evade = ((double)detailSkillTotals.EvadeCount * 100.0 / (double)num).ToString("0.0"),
				DPS = ((long)((double)detailSkillTotals.TotalDamage / duration)).ToString("N0"),
				Min = ((detailSkillTotals.MinDamage == int.MaxValue) ? "0" : detailSkillTotals.MinDamage.ToString("N0")),
				Max = detailSkillTotals.MaxDamage.ToString("N0"),
				Avg = ((detailSkillTotals.HitCount > 0) ? ((double)detailSkillTotals.TotalDamage / (double)detailSkillTotals.HitCount) : 0.0).ToString("N0"),
				Share = value.ToString("0.0"),
				Damage = detailSkillTotals.TotalDamage.ToString("N0"),
				DamageWithShare = $"{detailSkillTotals.TotalDamage:N0} ({value:0.0}%)",
				Heal = ((detailSkillTotals.TotalHealing > 0) ? detailSkillTotals.TotalHealing.ToString("N0") : "-"),
				HealWithShare = ((detailSkillTotals.TotalHealing > 0) ? $"{detailSkillTotals.TotalHealing:N0} ({value2:0.0}%)" : "-"),
				SelfHeal = ((detailSkillTotals.SelfHealing > 0) ? detailSkillTotals.SelfHealing.ToString("N0") : "-"),
				SelfHealWithShare = ((detailSkillTotals.SelfHealing > 0) ? $"{detailSkillTotals.SelfHealing:N0} ({value3:0.0}%)" : "-"),
				OtherHeal = ((detailSkillTotals.OtherHealing > 0) ? detailSkillTotals.OtherHealing.ToString("N0") : "-"),
				OtherHealWithShare = ((detailSkillTotals.OtherHealing > 0) ? $"{detailSkillTotals.OtherHealing:N0} ({value4:0.0}%)" : "-"),
				HealShare = value2.ToString("0.0"),
				RawDamage = detailSkillTotals.TotalDamage,
				RawHealing = detailSkillTotals.TotalHealing,
				RawSelfHealing = detailSkillTotals.SelfHealing,
				RawOtherHealing = detailSkillTotals.OtherHealing
			};
		}).Where(delegate(SkillRow row)
		{
			long result;
			bool num = long.TryParse(row.Name, out result);
			double result2;
			bool flag = double.TryParse(row.Share, NumberStyles.Any, CultureInfo.InvariantCulture, out result2) && result2 < 0.5;
			return !(num && flag) || row.RawHealing > 0;
		}).ToList();
		SortSkillRows(list, sortProp, sortDir);
		ApplySkillShareBars(list);
		List<SkillRow> list2 = new List<SkillRow>(list);
		if (skills.Count > 0)
		{
			list2.Add(BuildTotalSkillRow(skills, actorTotal, actorHealing, duration));
		}
		return list2;
	}

	private static void SortSkillRows(List<SkillRow> rows, string? sortProp, ListSortDirection? sortDir)
	{
		IEnumerable<SkillRow> source;
		if (!string.IsNullOrEmpty(sortProp) && sortDir.HasValue)
		{
			PropertyInfo propInfo = typeof(SkillRow).GetProperty(sortProp);
			source = ((propInfo == null) ? (from row in rows
				orderby row.RawDamage descending, row.RawHealing descending
				select row) : ((sortDir == ListSortDirection.Ascending) ? rows.OrderBy((SkillRow row) => ParseForSort(propInfo.GetValue(row))) : rows.OrderByDescending((SkillRow row) => ParseForSort(propInfo.GetValue(row)))));
		}
		else
		{
			source = from row in rows
				orderby row.RawDamage descending, row.RawHealing descending
				select row;
		}
		List<SkillRow> collection = source.ToList();
		rows.Clear();
		rows.AddRange(collection);
	}

	private static void ApplySkillShareBars(IReadOnlyList<SkillRow> rows)
	{
		long num = ((rows.Count == 0) ? 0 : rows.Max((SkillRow row) => Math.Max(row.RawDamage, row.RawHealing)));
		foreach (SkillRow row in rows)
		{
			row.ShareBarValue = ((num > 0) ? Math.Clamp((double)Math.Max(row.RawDamage, row.RawHealing) * 100.0 / (double)num, 0.0, 100.0) : 0.0);
		}
	}

	private static SkillRow BuildTotalSkillRow(IReadOnlyList<UiSkillState> skills, long actorTotal, long actorHealing, double duration)
	{
		DetailSkillTotals detailSkillTotals = AggregateDetailSkills(skills);
		int num = Math.Max(1, detailSkillTotals.HitCount);
		int num2 = detailSkillTotals.MinDamage;
		if (num2 == int.MaxValue)
		{
			num2 = 0;
		}
		return new SkillRow
		{
			Name = "총합",
			TraitSlots = Array.Empty<TraitSlot>(),
			SkillCodesText = "",
			Hit = detailSkillTotals.HitCount,
			Crit = ((double)detailSkillTotals.CritCount * 100.0 / (double)num).ToString("0.0"),
			Normal = ((double)detailSkillTotals.NormalHitCount * 100.0 / (double)num).ToString("0.0"),
			Back = ((double)detailSkillTotals.BackCount * 100.0 / (double)num).ToString("0.0"),
			Double = ((double)detailSkillTotals.DoubleCount * 100.0 / (double)num).ToString("0.0"),
			Perfect = ((double)detailSkillTotals.PerfectCount * 100.0 / (double)num).ToString("0.0"),
			Parry = ((double)detailSkillTotals.ParryCount * 100.0 / (double)num).ToString("0.0"),
			Smite = ((double)detailSkillTotals.SmiteCount * 100.0 / (double)num).ToString("0.0"),
			Multi = ((double)detailSkillTotals.MultiEventCount * 100.0 / (double)num).ToString("0.0"),
			Evade = ((double)detailSkillTotals.EvadeCount * 100.0 / (double)num).ToString("0.0"),
			DPS = ((double)actorTotal / duration).ToString("N0"),
			Min = num2.ToString("N0"),
			Max = detailSkillTotals.MaxDamage.ToString("N0"),
			Avg = ((detailSkillTotals.HitCount > 0) ? ((double)actorTotal / (double)detailSkillTotals.HitCount) : 0.0).ToString("N0"),
			Share = "100.0",
			Damage = actorTotal.ToString("N0"),
			DamageWithShare = $"{actorTotal:N0} (100.0%)",
			Heal = ((actorHealing > 0) ? actorHealing.ToString("N0") : "-"),
			HealWithShare = ((actorHealing > 0) ? $"{actorHealing:N0} (100.0%)" : "-"),
			SelfHeal = ((detailSkillTotals.SelfHealing > 0) ? detailSkillTotals.SelfHealing.ToString("N0") : "-"),
			SelfHealWithShare = ((actorHealing > 0 && detailSkillTotals.SelfHealing > 0) ? $"{detailSkillTotals.SelfHealing:N0} ({(double)detailSkillTotals.SelfHealing * 100.0 / (double)actorHealing:0.0}%)" : "-"),
			OtherHeal = ((detailSkillTotals.OtherHealing > 0) ? detailSkillTotals.OtherHealing.ToString("N0") : "-"),
			OtherHealWithShare = ((actorHealing > 0 && detailSkillTotals.OtherHealing > 0) ? $"{detailSkillTotals.OtherHealing:N0} ({(double)detailSkillTotals.OtherHealing * 100.0 / (double)actorHealing:0.0}%)" : "-"),
			HealShare = ((actorHealing > 0) ? "100.0" : "0.0"),
			RawDamage = actorTotal,
			RawHealing = actorHealing,
			RawSelfHealing = detailSkillTotals.SelfHealing,
			RawOtherHealing = detailSkillTotals.OtherHealing,
			IsTotal = true
		};
	}

	private static Dictionary<int, UiSkillState> BuildTargetDetailSkills(UiActorTargetState targetState, UiActorState actor)
	{
		Dictionary<int, UiSkillState> dictionary = new Dictionary<int, UiSkillState>();
		foreach (UiSkillState value2 in targetState.Skills.Values)
		{
			UiSkillState uiSkillState = value2.CloneDamageStatsOnly();
			if (uiSkillState.TotalDamage > 0 || uiSkillState.HitCount > 0)
			{
				dictionary[uiSkillState.SkillCode] = uiSkillState;
			}
		}
		foreach (UiSkillState item in actor.Skills.Values.Where((UiSkillState s) => s.TotalHealing > 0))
		{
			if (!dictionary.TryGetValue(item.SkillCode, out var value))
			{
				value = new UiSkillState(item.SkillCode);
				dictionary[item.SkillCode] = value;
			}
			value.MergeHealingFrom(item);
		}
		return dictionary;
	}

	private static Dictionary<int, UiSkillState> BuildHealingOnlyDetailSkills(UiActorState actor)
	{
		Dictionary<int, UiSkillState> dictionary = new Dictionary<int, UiSkillState>();
		foreach (UiSkillState item in actor.Skills.Values.Where((UiSkillState s) => s.TotalHealing > 0))
		{
			UiSkillState uiSkillState = new UiSkillState(item.SkillCode);
			uiSkillState.MergeHealingFrom(item);
			dictionary[item.SkillCode] = uiSkillState;
		}
		return dictionary;
	}

	private List<SkillRow> BuildHealingRows(IReadOnlyList<UiSkillState> skills, long actorHealing, double durationSeconds)
	{
		if (actorHealing <= 0)
		{
			return new List<SkillRow>();
		}
		double duration = Math.Max(1.0, durationSeconds);
		List<SkillRow> list = (from row in (from s in skills
				where s.TotalHealing > 0
				group s by _skillNames.GetDisplayGroupCode(s.SkillCode)).Select(delegate(IGrouping<int, UiSkillState> g)
			{
				DetailSkillTotals detailSkillTotals2 = AggregateDetailSkills(g);
				string nameOrCode = _skillNames.GetNameOrCode(detailSkillTotals2.BestCode);
				double value = ((actorHealing > 0) ? ((double)detailSkillTotals2.TotalHealing * 100.0 / (double)actorHealing) : 0.0);
				double value2 = ((actorHealing > 0) ? ((double)detailSkillTotals2.SelfHealing * 100.0 / (double)actorHealing) : 0.0);
				double value3 = ((actorHealing > 0) ? ((double)detailSkillTotals2.OtherHealing * 100.0 / (double)actorHealing) : 0.0);
				return new SkillRow
				{
					IconPath = GetSkillIconPath(detailSkillTotals2.BestCode),
					Name = nameOrCode,
					TraitSlots = BuildTraitSlots(detailSkillTotals2.SkillCodes),
					SkillCodesText = ((detailSkillTotals2.SkillCodes.Length == 0) ? "" : string.Join(", ", detailSkillTotals2.SkillCodes)),
					SpecialtyTooltip = _rdpsSkillCatalog.BuildSpecialtyTooltip(detailSkillTotals2.SkillCodes),
					Hit = detailSkillTotals2.HealCount,
					DPS = ((long)((double)detailSkillTotals2.TotalHealing / duration)).ToString("N0"),
					Min = detailSkillTotals2.MinHeal.ToString("N0"),
					Max = detailSkillTotals2.MaxHeal.ToString("N0"),
					Heal = detailSkillTotals2.TotalHealing.ToString("N0"),
					HealWithShare = $"{detailSkillTotals2.TotalHealing:N0} ({value:0.0}%)",
					SelfHeal = ((detailSkillTotals2.SelfHealing > 0) ? detailSkillTotals2.SelfHealing.ToString("N0") : "-"),
					SelfHealWithShare = ((detailSkillTotals2.SelfHealing > 0) ? $"{detailSkillTotals2.SelfHealing:N0} ({value2:0.0}%)" : "-"),
					OtherHeal = ((detailSkillTotals2.OtherHealing > 0) ? detailSkillTotals2.OtherHealing.ToString("N0") : "-"),
					OtherHealWithShare = ((detailSkillTotals2.OtherHealing > 0) ? $"{detailSkillTotals2.OtherHealing:N0} ({value3:0.0}%)" : "-"),
					HealShare = value.ToString("0.0"),
					Share = value.ToString("0.0"),
					RawHealing = detailSkillTotals2.TotalHealing,
					RawSelfHealing = detailSkillTotals2.SelfHealing,
					RawOtherHealing = detailSkillTotals2.OtherHealing
				};
			})
			orderby row.RawHealing descending, row.Name
			select row).ToList();
		long num = ((list.Count == 0) ? 0 : list.Max((SkillRow row) => row.RawHealing));
		foreach (SkillRow item in list)
		{
			item.ShareBarValue = ((num > 0) ? Math.Clamp((double)item.RawHealing * 100.0 / (double)num, 0.0, 100.0) : 0.0);
		}
		if (list.Count > 0)
		{
			DetailSkillTotals detailSkillTotals = AggregateDetailSkills(skills);
			list.Add(new SkillRow
			{
				Name = "총합",
				TraitSlots = Array.Empty<TraitSlot>(),
				SkillCodesText = "",
				Hit = detailSkillTotals.HealCount,
				DPS = ((long)((double)actorHealing / duration)).ToString("N0"),
				Min = detailSkillTotals.MinHeal.ToString("N0"),
				Max = detailSkillTotals.MaxHeal.ToString("N0"),
				Heal = actorHealing.ToString("N0"),
				HealWithShare = $"{actorHealing:N0} (100.0%)",
				SelfHeal = ((detailSkillTotals.SelfHealing > 0) ? detailSkillTotals.SelfHealing.ToString("N0") : "-"),
				SelfHealWithShare = ((actorHealing > 0 && detailSkillTotals.SelfHealing > 0) ? $"{detailSkillTotals.SelfHealing:N0} ({(double)detailSkillTotals.SelfHealing * 100.0 / (double)actorHealing:0.0}%)" : "-"),
				OtherHeal = ((detailSkillTotals.OtherHealing > 0) ? detailSkillTotals.OtherHealing.ToString("N0") : "-"),
				OtherHealWithShare = ((actorHealing > 0 && detailSkillTotals.OtherHealing > 0) ? $"{detailSkillTotals.OtherHealing:N0} ({(double)detailSkillTotals.OtherHealing * 100.0 / (double)actorHealing:0.0}%)" : "-"),
				HealShare = "100.0",
				Share = "100.0",
				RawHealing = actorHealing,
				RawSelfHealing = detailSkillTotals.SelfHealing,
				RawOtherHealing = detailSkillTotals.OtherHealing,
				IsTotal = true
			});
		}
		return list;
	}

	private void SetDetailCombatTime(TimeSpan duration)
	{
		_detailWindow?.SetCombatTime("전투 시간: " + FormatCombatDurationDetailed(duration));
	}

	private double GetDetailDurationSeconds(double fallbackSeconds)
	{
		fallbackSeconds = Math.Max(1.0, fallbackSeconds);
		int targetId = GetSelectedTargetId();
		if (targetId <= 0)
		{
			return fallbackSeconds;
		}
		TargetInfo targetInfo = _engine.GetAllTargets().FirstOrDefault((TargetInfo t) => t.TargetId == targetId);
		double num = ((targetInfo == null) ? 0.0 : (targetInfo.LastHit - targetInfo.FirstHit).TotalSeconds);
		if (!(num > 0.0))
		{
			return fallbackSeconds;
		}
		return Math.Max(1.0, num);
	}

	private void SetDetailCombatTimeFromEvents(IReadOnlyList<DamageEvent> events)
	{
		if (events.Count == 0)
		{
			SetDetailCombatTime(TimeSpan.Zero);
			return;
		}
		DateTime dateTime = events.Min((DamageEvent e) => e.TimestampUtc);
		DateTime dateTime2 = events.Max((DamageEvent e) => e.TimestampUtc);
		double detailDurationSeconds = GetDetailDurationSeconds(Math.Max(1.0, (dateTime2 - dateTime).TotalSeconds));
		SetDetailCombatTime(TimeSpan.FromSeconds(detailDurationSeconds));
	}

	private CombatDetailSummary CreateEmptyCombatDetailSummary(int actorId = 0)
	{
		var (combatPower, avgDps) = GetDetailSummaryCardMetrics(actorId);
		return new CombatDetailSummary("0", "0", "0", "0", "0", "0개", "0.0%", "0.0%", "0.0%", "0.0%", "0.0%", "0.0%", combatPower, avgDps);
	}

	private CombatDetailSummary BuildCombatDetailSummary(IReadOnlyList<UiSkillState> skills, long totalDamage, long totalHealing, double durationSeconds, int actorId)
	{
		DetailSkillTotals detailSkillTotals = AggregateDetailSkills(skills);
		int total = Math.Max(1, detailSkillTotals.HitCount);
		int num = detailSkillTotals.EvadeCount + detailSkillTotals.ParryCount;
		string accuracyRate = FormatRate(Math.Max(0, detailSkillTotals.HitCount - num), detailSkillTotals.HitCount);
		var (combatPower, avgDps) = GetDetailSummaryCardMetrics(actorId);
		return new CombatDetailSummary(totalDamage.ToString("N0"), ((long)((double)totalDamage / Math.Max(1.0, durationSeconds))).ToString("N0"), totalHealing.ToString("N0"), ((long)((double)totalHealing / Math.Max(1.0, durationSeconds))).ToString("N0"), detailSkillTotals.HitCount.ToString("N0"), $"{skills.Count:N0}개", FormatRate(detailSkillTotals.CritCount, total), FormatRate(detailSkillTotals.BackCount, total), FormatRate(detailSkillTotals.DoubleCount, total), FormatRate(detailSkillTotals.MultiEventCount, total), FormatRate(detailSkillTotals.PerfectCount, total), accuracyRate, combatPower, avgDps);
	}

	private (string CombatPower, string AvgDps) GetDetailSummaryCardMetrics(int actorId)
	{
		DpsCardViewModel dpsCardViewModel = ((actorId > 0) ? DpsCards.FirstOrDefault((DpsCardViewModel x) => x.ActorId == actorId) : null);
		if (dpsCardViewModel == null && !string.IsNullOrWhiteSpace(_selectedDetailCharacterKey))
		{
			dpsCardViewModel = FindDpsCardByCharacterKey(_selectedDetailCharacterKey);
		}
		return (CombatPower: NormalizeDetailSummaryMetric(dpsCardViewModel?.CombatPower), AvgDps: NormalizeDetailSummaryMetric(dpsCardViewModel?.CombatScoreBadgeText));
	}

	private static string NormalizeDetailSummaryMetric(string? value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		return "대기";
	}

	private bool TryGetCoreTargetActorStats(int actorId, int targetId, out ActorStats actor, out CombatSnapshot snapshot)
	{
		actor = null;
		snapshot = null;
		if (targetId <= 0)
		{
			return false;
		}
		CombatSnapshot combatSnapshot = _engine.BuildSnapshotForTarget(targetId);
		if (combatSnapshot == null || combatSnapshot.Actors.Count == 0)
		{
			return false;
		}
		string detailCharacterKey = GetDetailCharacterKey(actorId);
		foreach (ActorStats actor2 in combatSnapshot.Actors)
		{
			if (actor2.ActorId == actorId)
			{
				actor = actor2;
				snapshot = combatSnapshot;
				return true;
			}
			if (!string.IsNullOrWhiteSpace(detailCharacterKey) && !string.IsNullOrWhiteSpace(actor2.Name) && !string.IsNullOrWhiteSpace(actor2.ServerName) && string.Equals(GetCharacterKey(actor2.Name, actor2.ServerName), detailCharacterKey, StringComparison.Ordinal))
			{
				actor = actor2;
				snapshot = combatSnapshot;
				return true;
			}
		}
		return false;
	}

	private UiActorState? GetDetailActorState(int actorId, out ArchivedBossRecord? archivedRecord)
	{
		archivedRecord = GetSelectedArchivedBossRecord();
		string characterKey = GetDetailCharacterKey(actorId);
		if (archivedRecord != null)
		{
			List<UiActorState> actors = archivedRecord.UiActors.Values.Where((UiActorState actor) => IsDetailActorMatch(actor, actorId, characterKey)).ToList();
			return BuildMergedDetailActorState(actorId, actors);
		}
		lock (_sync)
		{
			List<UiActorState> actors2 = _uiActors.Values.Where((UiActorState actor) => IsDetailActorMatch(actor, actorId, characterKey)).ToList();
			return BuildMergedDetailActorState(actorId, actors2);
		}
	}

	private string? GetDetailCharacterKey(int actorId)
	{
		if (!string.IsNullOrWhiteSpace(_selectedDetailCharacterKey))
		{
			return _selectedDetailCharacterKey;
		}
		string dpsCardCharacterKey = GetDpsCardCharacterKey((actorId > 0) ? DpsCards.FirstOrDefault((DpsCardViewModel x) => x.ActorId == actorId) : null);
		if (!string.IsNullOrWhiteSpace(dpsCardCharacterKey))
		{
			return dpsCardCharacterKey;
		}
		if (!TryGetActorCharacterKey(actorId, out string key))
		{
			return null;
		}
		return key;
	}

	private bool IsDetailActorMatch(UiActorState actor, int selectedActorId, string? selectedCharacterKey)
	{
		if (ResolveDetailActorId(actor.ActorId) == selectedActorId)
		{
			return true;
		}
		if (!string.IsNullOrWhiteSpace(selectedCharacterKey) && TryGetActorCharacterKey(actor.ActorId, out string key))
		{
			return string.Equals(key, selectedCharacterKey, StringComparison.Ordinal);
		}
		return false;
	}

	private bool TryGetActorCharacterKey(int actorId, out string? key)
	{
		key = null;
		if (actorId <= 0)
		{
			return false;
		}
		return TryGetCharacterKeyFromFullName(_engine.Names.GetOrFallback(actorId), out key);
	}

	private static bool TryGetCharacterKeyFromFullName(string? fullName, out string? key)
	{
		key = null;
		if (string.IsNullOrWhiteSpace(fullName))
		{
			return false;
		}
		int num = fullName.IndexOf('[');
		int num2 = fullName.IndexOf(']');
		if (num <= 0 || num2 <= num)
		{
			return false;
		}
		string text = fullName.Substring(0, num).Trim();
		string text2 = fullName.Substring(num + 1, num2 - num - 1).Trim();
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text2))
		{
			return false;
		}
		key = GetCharacterKey(text, text2);
		return true;
	}

	private static UiActorState? BuildMergedDetailActorState(int selectedActorId, IReadOnlyList<UiActorState> actors)
	{
		if (actors.Count == 0)
		{
			return null;
		}
		if (actors.Count == 1 && actors[0].ActorId == selectedActorId)
		{
			return actors[0];
		}
		HashSet<int> aliasIds = actors.Select((UiActorState actor) => actor.ActorId).ToHashSet();
		List<DamageEvent> list = (from e in actors.SelectMany((UiActorState actor) => actor.Recent.ToList())
			orderby e.TimestampUtc
			select e).ToList();
		DateTime firstUtc = actors.Min((UiActorState actor) => actor.FirstUtc);
		UiActorState uiActorState = new UiActorState(selectedActorId, firstUtc);
		foreach (UiActorState actor in actors)
		{
			uiActorState.MergeAggregateFrom(actor, aliasIds, selectedActorId);
		}
		foreach (DamageEvent item in list)
		{
			uiActorState.AddRecentOnly(NormalizeDetailDamageEvent(item, aliasIds, selectedActorId));
		}
		foreach (UiBuffEvent item2 in from e in DeduplicateActorBuffEvents(from e in actors.SelectMany((UiActorState actor) => actor.BuffEvents.ToList())
				select NormalizeDetailBuffEvent(e, aliasIds, selectedActorId))
			orderby e.TimestampUtc
			select e)
		{
			uiActorState.ApplyBuff(item2);
		}
		return uiActorState;
	}

	private static DamageEvent NormalizeDetailDamageEvent(DamageEvent e, IReadOnlySet<int> aliasIds, int selectedActorId)
	{
		int num = (aliasIds.Contains(e.ActorId) ? selectedActorId : e.ActorId);
		int num2 = (aliasIds.Contains(e.TargetId) ? selectedActorId : e.TargetId);
		if (num != e.ActorId || num2 != e.TargetId)
		{
			return e with
			{
				ActorId = num,
				TargetId = num2
			};
		}
		return e;
	}

	private static UiBuffEvent NormalizeDetailBuffEvent(UiBuffEvent e, IReadOnlySet<int> aliasIds, int selectedActorId)
	{
		int num = (aliasIds.Contains(e.ActorId) ? selectedActorId : e.ActorId);
		int num2 = (aliasIds.Contains(e.TargetId) ? selectedActorId : e.TargetId);
		int num3 = (aliasIds.Contains(e.OwnerId) ? selectedActorId : e.OwnerId);
		if (num != e.ActorId || num2 != e.TargetId || num3 != e.OwnerId)
		{
			return e with
			{
				ActorId = num,
				TargetId = num2,
				OwnerId = num3
			};
		}
		return e;
	}

	private List<DamageEvent> GetFilteredActorEvents(UiActorState actor)
	{
		List<DamageEvent> list;
		lock (_sync)
		{
			list = actor.Recent.ToList();
		}
		int tid = GetSelectedTargetId();
		if (tid == 0)
		{
			return list;
		}
		List<DamageEvent> list2 = list.Where((DamageEvent e) => e.TargetId == tid).ToList();
		if (list2.Count <= 0)
		{
			return list;
		}
		return list2;
	}

	private static List<UiSkillState> BuildSkillStatesFromEvents(IEnumerable<DamageEvent> events)
	{
		Dictionary<int, UiSkillState> dictionary = new Dictionary<int, UiSkillState>();
		foreach (DamageEvent @event in events)
		{
			if (!dictionary.TryGetValue(@event.SkillCodeRaw, out var value))
			{
				value = new UiSkillState(@event.SkillCodeRaw);
				dictionary[@event.SkillCodeRaw] = value;
			}
			value.Apply(@event);
		}
		return dictionary.Values.ToList();
	}

	private void SetDetailSummaryFromEvents(IReadOnlyList<DamageEvent> events, int actorId)
	{
		if (events.Count == 0)
		{
			SetDetailCombatTime(TimeSpan.Zero);
			_detailWindow?.SetSummary(CreateEmptyCombatDetailSummary(actorId));
			return;
		}
		List<UiSkillState> skills = BuildSkillStatesFromEvents(events);
		long totalDamage = ((IEnumerable<DamageEvent>)events).Sum((Func<DamageEvent, long>)((DamageEvent e) => e.Damage));
		long totalHealing = ((IEnumerable<DamageEvent>)events).Sum((Func<DamageEvent, long>)((DamageEvent e) => Math.Max(0, e.HealAmount)));
		DateTime dateTime = events.Min((DamageEvent e) => e.TimestampUtc);
		DateTime dateTime2 = events.Max((DamageEvent e) => e.TimestampUtc);
		double detailDurationSeconds = GetDetailDurationSeconds(Math.Max(1.0, (dateTime2 - dateTime).TotalSeconds));
		SetDetailCombatTime(TimeSpan.FromSeconds(detailDurationSeconds));
		_detailWindow?.SetSummary(BuildCombatDetailSummary(skills, totalDamage, totalHealing, detailDurationSeconds, actorId));
	}

	private void RenderDpsGraphOnly(int actorId)
	{
		ArchivedBossRecord archivedRecord;
		UiActorState detailActorState = GetDetailActorState(actorId, out archivedRecord);
		if (detailActorState == null)
		{
			_detailWindow?.SetSummary(CreateEmptyCombatDetailSummary(actorId));
			_detailWindow?.SetRdpsRows(Array.Empty<RdpsBuffRow>(), 0.0);
			_detailWindow?.SetDpsGraphRows(Array.Empty<DpsGraphRow>());
			return;
		}
		List<DamageEvent> list = (from e in GetFilteredActorEvents(detailActorState)
			orderby e.TimestampUtc
			select e).ToList();
		SetDetailSummaryFromEvents(list, actorId);
		if (list.Count == 0 || ShouldRefreshDetailRdpsRows(actorId))
		{
			UpdateDetailRdpsRows(actorId, list, archivedRecord, CalculateDetailDpsFromEvents(list));
		}
		_detailWindow?.SetDpsGraphRows(SampleDpsGraphRows(BuildDpsGraphRows(list)));
	}

	private void RenderBuffsOnly(int actorId)
	{
		ArchivedBossRecord archivedRecord;
		UiActorState detailActorState = GetDetailActorState(actorId, out archivedRecord);
		if (detailActorState == null)
		{
			_detailWindow?.SetSummary(CreateEmptyCombatDetailSummary(actorId));
			_detailWindow?.SetRdpsRows(Array.Empty<RdpsBuffRow>(), 0.0);
			_detailWindow?.SetBuffRows(Array.Empty<BuffUptimeRow>());
			return;
		}
		List<DamageEvent> list = (from e in GetFilteredActorEvents(detailActorState)
			orderby e.TimestampUtc
			select e).ToList();
		SetDetailSummaryFromEvents(list, actorId);
		if (list.Count == 0 || ShouldRefreshDetailRdpsRows(actorId))
		{
			UpdateDetailRdpsRows(actorId, list, archivedRecord, CalculateDetailDpsFromEvents(list));
		}
		_detailWindow?.SetBuffRows(BuildBuffUptimeRows(detailActorState, list));
	}

	private void RenderRdpsOnly(int actorId)
	{
		ArchivedBossRecord archivedRecord;
		UiActorState detailActorState = GetDetailActorState(actorId, out archivedRecord);
		if (detailActorState == null)
		{
			_detailWindow?.SetSummary(CreateEmptyCombatDetailSummary(actorId));
			_detailWindow?.SetRdpsRows(Array.Empty<RdpsBuffRow>(), 0.0);
			return;
		}
		List<DamageEvent> list = (from e in GetFilteredActorEvents(detailActorState)
			orderby e.TimestampUtc
			select e).ToList();
		SetDetailSummaryFromEvents(list, actorId);
		MarkDetailRdpsRowsRefreshed(actorId);
		UpdateDetailRdpsRows(actorId, list, archivedRecord, CalculateDetailDpsFromEvents(list));
	}

	private bool ShouldRefreshDetailRdpsRows(int actorId)
	{
		int selectedTargetId = GetSelectedTargetId();
		long timestamp = Stopwatch.GetTimestamp();
		bool num = actorId != _lastDetailRdpsActorId || selectedTargetId != _lastDetailRdpsTargetId || _lastDetailRdpsRefreshTick == 0;
		double num2 = ((_lastDetailRdpsRefreshTick == 0L) ? double.PositiveInfinity : ((double)(timestamp - _lastDetailRdpsRefreshTick) / (double)Stopwatch.Frequency));
		if (!num && num2 < 5.0)
		{
			return false;
		}
		_lastDetailRdpsActorId = actorId;
		_lastDetailRdpsTargetId = selectedTargetId;
		_lastDetailRdpsRefreshTick = timestamp;
		return true;
	}

	private void MarkDetailRdpsRowsRefreshed(int actorId)
	{
		_lastDetailRdpsActorId = actorId;
		_lastDetailRdpsTargetId = GetSelectedTargetId();
		_lastDetailRdpsRefreshTick = Stopwatch.GetTimestamp();
	}

	private void UpdateDetailRdpsRows(int actorId, IReadOnlyList<DamageEvent> selectedEvents, ArchivedBossRecord? archivedRecord, double baseDps, CombatSnapshot? currentSnapshot = null)
	{
		if (selectedEvents.Count == 0)
		{
			_detailWindow?.SetRdpsRows(Array.Empty<RdpsBuffRow>(), baseDps);
			return;
		}
		int selectedTargetId = GetSelectedTargetId();
		List<UiActorState> detailActorStatesSnapshot = GetDetailActorStatesSnapshot(archivedRecord);
		List<DamageEvent> detailDamageEventsSnapshot = GetDetailDamageEventsSnapshot(detailActorStatesSnapshot, archivedRecord, selectedTargetId);
		List<UiBuffEvent> detailBuffEventsSnapshot = GetDetailBuffEventsSnapshot(detailActorStatesSnapshot, archivedRecord);
		Dictionary<int, JobClass> actorJobs = BuildRdpsActorJobMap(archivedRecord?.Snapshot ?? currentSnapshot ?? GetSnapshotForCurrentFilter());
		_detailWindow?.SetRdpsRows(BuildRdpsBuffRows(actorId, selectedEvents, detailDamageEventsSnapshot, detailActorStatesSnapshot, detailBuffEventsSnapshot, actorJobs), baseDps);
	}

	private static double CalculateDpsFromEvents(IReadOnlyList<DamageEvent> events)
	{
		if (events.Count == 0)
		{
			return 0.0;
		}
		DateTime dateTime = events.Min((DamageEvent e) => e.TimestampUtc);
		DateTime dateTime2 = events.Max((DamageEvent e) => e.TimestampUtc);
		double num = Math.Max(1.0, (dateTime2 - dateTime).TotalSeconds);
		return ((IEnumerable<DamageEvent>)events).Sum((Func<DamageEvent, double>)((DamageEvent e) => e.Damage)) / num;
	}

	private double CalculateDetailDpsFromEvents(IReadOnlyList<DamageEvent> events)
	{
		if (events.Count == 0)
		{
			return 0.0;
		}
		DateTime dateTime = events.Min((DamageEvent e) => e.TimestampUtc);
		DateTime dateTime2 = events.Max((DamageEvent e) => e.TimestampUtc);
		double detailDurationSeconds = GetDetailDurationSeconds(Math.Max(1.0, (dateTime2 - dateTime).TotalSeconds));
		return ((IEnumerable<DamageEvent>)events).Sum((Func<DamageEvent, double>)((DamageEvent e) => e.Damage)) / detailDurationSeconds;
	}

	private List<UiActorState> GetDetailActorStatesSnapshot(ArchivedBossRecord? archivedRecord)
	{
		if (archivedRecord != null)
		{
			return archivedRecord.UiActors.Values.ToList();
		}
		lock (_sync)
		{
			return _uiActors.Values.ToList();
		}
	}

	private List<UiBuffEvent> GetDetailBuffEventsSnapshot(IReadOnlyList<UiActorState> actors, ArchivedBossRecord? archivedRecord)
	{
		if (archivedRecord != null)
		{
			return DeduplicateBuffEvents(actors.SelectMany((UiActorState actor) => actor.BuffEvents.ToList()));
		}
		lock (_sync)
		{
			PruneActiveBuffEvents(DateTime.UtcNow);
			return DeduplicateBuffEvents(actors.SelectMany((UiActorState actor) => actor.BuffEvents.ToList()).Concat(_allBuffEvents).Concat(_activeBuffEvents.Values));
		}
	}

	private List<DamageEvent> GetDetailDamageEventsSnapshot(IReadOnlyList<UiActorState> actors, ArchivedBossRecord? archivedRecord, int targetId)
	{
		if (actors.Count == 0)
		{
			return new List<DamageEvent>();
		}
		if (archivedRecord != null)
		{
			return FilterDetailDamageEvents(actors, targetId);
		}
		lock (_sync)
		{
			return FilterDetailDamageEvents(actors, targetId);
		}
	}

	private static List<DamageEvent> FilterDetailDamageEvents(IReadOnlyList<UiActorState> actors, int targetId)
	{
		List<DamageEvent> list = new List<DamageEvent>();
		foreach (UiActorState actor in actors)
		{
			foreach (DamageEvent item in actor.Recent)
			{
				if (targetId == 0 || item.TargetId == targetId)
				{
					list.Add(item);
				}
			}
		}
		return list;
	}

	private static List<UiBuffEvent> DeduplicateBuffEvents(IEnumerable<UiBuffEvent> buffEvents)
	{
		List<UiBuffEvent> list = new List<UiBuffEvent>();
		HashSet<(DateTime, string, int, int, int, int)> hashSet = new HashSet<(DateTime, string, int, int, int, int)>();
		foreach (UiBuffEvent buffEvent in buffEvents)
		{
			(DateTime, string, int, int, int, int) item = (buffEvent.TimestampUtc, buffEvent.Kind, buffEvent.BuffId, buffEvent.SkillId, buffEvent.OwnerId, buffEvent.TargetId);
			if (hashSet.Add(item))
			{
				list.Add(buffEvent);
			}
		}
		return list;
	}

	private static List<UiBuffEvent> DeduplicateActorBuffEvents(IEnumerable<UiBuffEvent> buffEvents)
	{
		List<UiBuffEvent> list = new List<UiBuffEvent>();
		HashSet<(DateTime, string, int, int, int, int, int)> hashSet = new HashSet<(DateTime, string, int, int, int, int, int)>();
		foreach (UiBuffEvent buffEvent in buffEvents)
		{
			(DateTime, string, int, int, int, int, int) item = (buffEvent.TimestampUtc, buffEvent.Kind, buffEvent.ActorId, buffEvent.TargetId, buffEvent.OwnerId, buffEvent.BuffId, buffEvent.SkillId);
			if (hashSet.Add(item))
			{
				list.Add(buffEvent);
			}
		}
		return list;
	}

	private Dictionary<int, JobClass> BuildRdpsActorJobMap(CombatSnapshot? snapshot)
	{
		Dictionary<int, JobClass> dictionary = new Dictionary<int, JobClass>();
		if (snapshot != null)
		{
			foreach (ActorStats actor in snapshot.Actors)
			{
				int num = ResolveDetailActorId(actor.ActorId);
				if (num > 0 && actor.Job != JobClass.None)
				{
					dictionary.TryAdd(num, actor.Job);
				}
			}
		}
		foreach (DpsCardViewModel dpsCard in DpsCards)
		{
			int num2 = ResolveDetailActorId(dpsCard.ActorId);
			if (num2 > 0 && dpsCard.Job != JobClass.None)
			{
				dictionary.TryAdd(num2, dpsCard.Job);
			}
		}
		return dictionary;
	}

	private IReadOnlyList<RdpsBuffRow> BuildRdpsBuffRows(int selectedActorId, IReadOnlyList<DamageEvent> selectedEvents, IReadOnlyList<DamageEvent> damageEvents, IReadOnlyList<UiActorState> actors, IReadOnlyList<UiBuffEvent> buffEvents, IReadOnlyDictionary<int, JobClass> actorJobs)
	{
		if (selectedEvents.Count == 0 || actors.Count == 0 || _rdpsPartyBuffCatalog.Effects.Count == 0)
		{
			return Array.Empty<RdpsBuffRow>();
		}
		selectedActorId = ResolveDetailActorId(selectedActorId);
		DateTime windowStart = selectedEvents.Min((DamageEvent e) => e.TimestampUtc);
		DateTime windowEnd = selectedEvents.Max((DamageEvent e) => e.TimestampUtc);
		if (windowEnd <= windowStart)
		{
			windowEnd = windowStart.AddSeconds(1.0);
		}
		double durationSeconds = Math.Max(1.0, (windowEnd - windowStart).TotalSeconds);
		HashSet<int> hashSet = (from actor in actors
			select ResolveDetailActorId(actor.ActorId) into id
			where id > 0
			select id).ToHashSet();
		if (!hashSet.Contains(selectedActorId))
		{
			hashSet.Add(selectedActorId);
		}
		HashSet<int> damageTargetIds = (from e in selectedEvents
			select ResolveDetailActorId(e.TargetId) into id
			where id > 0
			select id).ToHashSet();
		List<RdpsBuffWindow> list = BuildRdpsBuffWindows(buffEvents, windowStart, windowEnd, hashSet, damageTargetIds, actorJobs);
		if (list.Count == 0)
		{
			return Array.Empty<RdpsBuffRow>();
		}
		Dictionary<RdpsBuffRowKey, List<(DateTime Start, DateTime End)>> intervalsByKey = (from rdpsBuffWindow in list
			where rdpsBuffWindow.OwnerId == selectedActorId || rdpsBuffWindow.TargetId == selectedActorId
			group rdpsBuffWindow by rdpsBuffWindow.RowKey).ToDictionary((IGrouping<RdpsBuffRowKey, RdpsBuffWindow> group) => group.Key, (IGrouping<RdpsBuffRowKey, RdpsBuffWindow> group) => (from rdpsBuffWindow in @group
			select (Start: rdpsBuffWindow.Start, End: rdpsBuffWindow.End) into interval
			orderby interval.Start
			select interval).ToList());
		Dictionary<RdpsBuffRowKey, RdpsBuffAccumulator> dictionary = new Dictionary<RdpsBuffRowKey, RdpsBuffAccumulator>();
		List<DamageEvent> list2 = (from e in damageEvents
			where e.Damage > 0 && e.TimestampUtc >= windowStart && e.TimestampUtc <= windowEnd
			orderby e.TimestampUtc
			select e).ToList();
		Dictionary<int, List<RdpsBuffWindow>> dictionary2 = (from rdpsBuffWindow in list
			where rdpsBuffWindow.EffectScope != RdpsEffectScope.TargetDebuff
			group rdpsBuffWindow by rdpsBuffWindow.TargetId).ToDictionary((IGrouping<int, RdpsBuffWindow> group) => group.Key, (IGrouping<int, RdpsBuffWindow> group) => group.ToList());
		Dictionary<int, List<RdpsBuffWindow>> dictionary3 = (from rdpsBuffWindow in list
			where rdpsBuffWindow.EffectScope == RdpsEffectScope.TargetDebuff
			group rdpsBuffWindow by rdpsBuffWindow.TargetId).ToDictionary((IGrouping<int, RdpsBuffWindow> group) => group.Key, (IGrouping<int, RdpsBuffWindow> group) => group.ToList());
		foreach (DamageEvent item in list2)
		{
			int num = ResolveDetailActorId(item.ActorId);
			int key = ResolveDetailActorId(item.TargetId);
			if (!hashSet.Contains(num))
			{
				continue;
			}
			List<RdpsBuffWindow> list3 = null;
			if (dictionary2.TryGetValue(num, out var value))
			{
				foreach (RdpsBuffWindow item2 in value)
				{
					if (item2.Start <= item.TimestampUtc && item2.End >= item.TimestampUtc)
					{
						(list3 ?? (list3 = new List<RdpsBuffWindow>())).Add(item2);
					}
				}
			}
			if (dictionary3.TryGetValue(key, out var value2))
			{
				foreach (RdpsBuffWindow item3 in value2)
				{
					if (item3.Start <= item.TimestampUtc && item3.End >= item.TimestampUtc && (item3.SourceRestriction != RdpsSourceRestriction.OwnerOnly || item3.OwnerId == num))
					{
						(list3 ?? (list3 = new List<RdpsBuffWindow>())).Add(item3);
					}
				}
			}
			if (list3 == null || list3.Count == 0)
			{
				continue;
			}
			IReadOnlyList<RdpsBuffWindow> readOnlyList = RdpsSupportRules.FilterWindowsForDamageEvent(list3, item.IsCrit);
			if (readOnlyList.Count == 0)
			{
				continue;
			}
			IReadOnlyList<RdpsSupportGroup<RdpsBuffWindow>> readOnlyList2 = RdpsSupportRules.SelectEffectiveGroups(readOnlyList);
			if (readOnlyList2.Count == 0)
			{
				continue;
			}
			double num2 = readOnlyList2.Aggregate(1.0, (double num7, RdpsSupportGroup<RdpsBuffWindow> group) => num7 * group.Multiplier);
			if (num2 <= 1.0)
			{
				continue;
			}
			double num3 = (double)item.Damage - (double)item.Damage / num2;
			if (num3 <= 0.0)
			{
				continue;
			}
			double num4 = readOnlyList2.Sum((RdpsSupportGroup<RdpsBuffWindow> group) => Math.Log(group.Multiplier));
			foreach (RdpsSupportGroup<RdpsBuffWindow> item4 in readOnlyList2)
			{
				double num5 = ((num4 > 0.0) ? (Math.Log(item4.Multiplier) / num4) : (1.0 / (double)readOnlyList2.Count));
				foreach (RdpsSupportSourceShare<RdpsBuffWindow> source in item4.Sources)
				{
					RdpsBuffWindow window = source.Window;
					bool flag = num == selectedActorId && window.OwnerId != selectedActorId;
					bool flag2 = window.OwnerId == selectedActorId && num != selectedActorId;
					if (flag || flag2)
					{
						double num6 = num3 * num5 * source.Share;
						RdpsBuffRowKey rowKey = window.RowKey;
						if (!dictionary.TryGetValue(rowKey, out var value3))
						{
							value3 = (dictionary[rowKey] = new RdpsBuffAccumulator(window));
						}
						if (flag)
						{
							value3.ReducedDamage += num6;
						}
						if (flag2)
						{
							value3.AdditionalDamage += num6;
						}
					}
				}
			}
		}
		return (from row in dictionary.Values.Where((RdpsBuffAccumulator accumulator) => accumulator.AdditionalDamage > 0.5 || accumulator.ReducedDamage > 0.5).Select(delegate(RdpsBuffAccumulator accumulator)
			{
				RdpsBuffWindow window2 = accumulator.Window;
				double num7 = accumulator.AdditionalDamage / durationSeconds;
				double num8 = accumulator.ReducedDamage / durationSeconds;
				double num9 = num7 - num8;
				List<(DateTime, DateTime)> value4;
				double num10 = (intervalsByKey.TryGetValue(window2.RowKey, out value4) ? BuffIntervalUtilities.SumMergedSeconds(value4) : Math.Max(0.0, (window2.End - window2.Start).TotalSeconds));
				double num11 = Math.Clamp(num10 * 100.0 / durationSeconds, 0.0, 100.0);
				return new RdpsBuffRow
				{
					IconPath = window2.IconPath,
					Name = FormatRdpsBuffName(window2),
					ProviderName = window2.ProviderName,
					TargetName = window2.TargetName,
					EffectText = $"{window2.Percent:0.#}% / {FormatDurationShort(num10)} ({num11:0.0}%)",
					AdditionalDps = num7,
					ReducedDps = num8,
					NetDps = num9,
					AdditionalDpsText = FormatPositiveDps(num7),
					ReducedDpsText = FormatPositiveDps(num8),
					NetDpsText = FormatSignedDps(num9),
					EvidenceText = BuildRdpsEvidenceText(window2, num7, num8, num9, num10, num11)
				};
			})
			orderby Math.Abs(row.NetDps) descending, row.AdditionalDps + row.ReducedDps descending, row.Name
			select row).ToList();
	}

	private List<RdpsBuffWindow> BuildRdpsBuffWindows(IReadOnlyList<UiBuffEvent> buffEvents, DateTime windowStart, DateTime windowEnd, HashSet<int> participantIds, HashSet<int> damageTargetIds, IReadOnlyDictionary<int, JobClass> actorJobs)
	{
		List<RdpsBuffWindow> list = new List<RdpsBuffWindow>();
		foreach (UiBuffEvent buffEvent in buffEvents)
		{
			if (!IsRdpsBuffWindowEvent(buffEvent) || !BuffIntervalUtilities.HasInterval(buffEvent.DurationMs, buffEvent.ExpiresAtMs) || !TryResolveRdpsPartyBuffEffect(buffEvent, out RdpsPartyBuffEffect effect) || effect == null)
			{
				continue;
			}
			int num = ResolveDetailActorId(buffEvent.OwnerId);
			int num2 = ResolveDetailActorId(buffEvent.TargetId);
			(DateTime, DateTime) interval = BuffIntervalUtilities.GetInterval(buffEvent.TimestampUtc, buffEvent.DurationMs, buffEvent.StartedAtMs, buffEvent.ExpiresAtMs);
			if (interval.Item2 <= windowStart || interval.Item1 >= windowEnd)
			{
				continue;
			}
			DateTime dateTime = ((interval.Item1 < windowStart) ? windowStart : interval.Item1);
			DateTime dateTime2 = ((interval.Item2 > windowEnd) ? windowEnd : interval.Item2);
			if (dateTime2 <= dateTime)
			{
				continue;
			}
			if (effect.EffectScope == RdpsEffectScope.TargetDebuff)
			{
				if (num > 0 && num2 > 0 && num != num2 && participantIds.Contains(num) && damageTargetIds.Contains(num2) && RdpsSupportRules.IsEffectOwnerJob(num, effect, actorJobs))
				{
					list.Add(CreateRdpsBuffWindow(effect, buffEvent, num, num2, dateTime, dateTime2));
				}
			}
			else
			{
				if ((num <= 0 && num2 <= 0) || (num > 0 && !participantIds.Contains(num)) || (num2 > 0 && !participantIds.Contains(num2)))
				{
					continue;
				}
				if (num > 0 && num2 > 0 && num != num2 && participantIds.Contains(num) && participantIds.Contains(num2))
				{
					list.Add(CreateRdpsBuffWindow(effect, buffEvent, num, num2, dateTime, dateTime2));
				}
				int providerId = RdpsSupportRules.ResolvePartyBuffProviderId(effect, num, num2, participantIds, actorJobs);
				if (providerId <= 0)
				{
					continue;
				}
				foreach (int item in participantIds.Where((int id) => id > 0 && id != providerId))
				{
					list.Add(CreateRdpsBuffWindow(effect, buffEvent, providerId, item, dateTime, dateTime2));
				}
			}
		}
		return list;
	}

	private RdpsBuffWindow CreateRdpsBuffWindow(RdpsPartyBuffEffect effect, UiBuffEvent buff, int ownerId, int targetId, DateTime start, DateTime end)
	{
		effect = ResolveRdpsEffectForProvider(effect, ownerId);
		int displaySkillLevel = ResolveRdpsDisplaySkillLevel(buff, effect, ownerId);
		return new RdpsBuffWindow(effect.SkillId, effect.LevelCode, effect.SkillName, effect.Level, displaySkillLevel, effect.PveDamageAmpPercent, effect.Multiplier, effect.ExclusiveGroup, effect.EffectScope, effect.SourceRestriction, effect.EffectKind, ownerId, targetId, GetDetailActorDisplayName(ownerId), GetDetailActorDisplayName(targetId), GetSkillIconPath(effect.SkillId), effect.Description, start, end);
	}

	private RdpsPartyBuffEffect ResolveRdpsEffectForProvider(RdpsPartyBuffEffect effect, int providerId)
	{
		if (!CanResolveRdpsEffectBySkillLevel(effect.SkillId))
		{
			return effect;
		}
		if (TryGetStigmaSkillLevelForProvider(providerId, effect.SkillId, out var level) && _rdpsPartyBuffCatalog.TryGetEffectForSkillLevel(effect.SkillId, level, out RdpsPartyBuffEffect effect2) && effect2 != null)
		{
			return effect2;
		}
		return effect;
	}

	private int ResolveRdpsDisplaySkillLevel(UiBuffEvent buff, RdpsPartyBuffEffect effect, int providerId)
	{
		if (buff.SkillLevel > 0)
		{
			return buff.SkillLevel;
		}
		if (TryGetStigmaSkillLevelForProvider(providerId, effect.SkillId, out var level))
		{
			return level;
		}
		return 0;
	}

	private bool TryGetStigmaSkillLevelForProvider(int providerId, int skillCode, out int level)
	{
		if (_engine.TryGetStigmaSkillLevelForProvider(providerId, skillCode, out level))
		{
			return true;
		}
		int num = ResolveDetailActorId(providerId);
		if (num > 0 && num != providerId)
		{
			return _engine.TryGetStigmaSkillLevelForProvider(num, skillCode, out level);
		}
		return false;
	}

	private bool IsLocalDetailActor(int actorId)
	{
		int num = ResolveDetailActorId(actorId);
		if (num <= 0)
		{
			return false;
		}
		int? localPlayerActorId = _engine.LocalPlayerActorId;
		if (localPlayerActorId.HasValue && ResolveDetailActorId(localPlayerActorId.Value) == num)
		{
			return true;
		}
		if (!_engine.TryGetActorName(num, out string name) || string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		return string.Equals(NormalizeCharacterNameForMatch(name), NormalizeCharacterNameForMatch(_engine.LocalPlayerName), StringComparison.Ordinal);
	}

	private static string BuildRdpsEvidenceText(RdpsBuffWindow window, double addedDps, double reducedDps, double netDps, double uptimeSeconds, double uptimePercent)
	{
		string value = ((window.EffectScope == RdpsEffectScope.TargetDebuff) ? "대상 디버프" : "파티 버프");
		string text = ((window.EffectScope != RdpsEffectScope.TargetDebuff) ? ((window.OwnerId == window.TargetId) ? "자신에게 버프가 유지되는 동안 자신의 피해에 적용" : "대상 파티원에게 버프가 유지되는 동안 해당 파티원의 피해에 적용") : ((window.SourceRestriction == RdpsSourceRestriction.OwnerOnly) ? "대상에게 디버프가 유지되는 동안 부여자의 피해에만 적용" : "대상에게 디버프가 유지되는 동안 파티원의 같은 대상 피해에 적용"));
		string value2 = ((window.EffectKind == RdpsEffectKind.CriticalDamageTaken) ? $"치명타 피해 기준 {window.Percent:0.#}% 증가로 계산" : $"PVE 피해 기준 {window.Percent:0.#}% 증가로 계산");
		string value3 = ((window.EffectKind == RdpsEffectKind.CriticalDamageTaken) ? "치명타 추가 피해 = 관측 치명타 피해 - 관측 치명타 피해 / (1 + 효과%)" : "추가 피해 = 관측 피해 - 관측 피해 / (1 + 효과%)");
		if (window.EffectKind == RdpsEffectKind.CriticalDamageTaken)
		{
			text += "\n조건 보정: 실제 치명타 타격에만 적용, 도발: 위축과 동시에 걸린 시간은 제외";
		}
		return $"{FormatRdpsBuffName(window)}\n스킬 레벨: {((window.DisplaySkillLevel > 0) ? $"Lv.{window.DisplaySkillLevel}" : "패킷 미확인")}\n분류: {value}\n제공자: {window.ProviderName}\n대상: {window.TargetName}\n효과: {value2}\n적용 시간: {FormatDurationShort(uptimeSeconds)} ({uptimePercent:0.0}%)\n조건: {text}\n근거: {window.Description}\n계산식: {value3}\n결과: 가산 {addedDps:N0} / 차감 {reducedDps:N0} / 순 {FormatSignedDps(netDps)} DPS";
	}

	private static bool IsRdpsBuffWindowEvent(UiBuffEvent buff)
	{
		if (!buff.Kind.Equals("BuffApplied", StringComparison.OrdinalIgnoreCase) && !buff.Kind.Equals("BuffState", StringComparison.OrdinalIgnoreCase))
		{
			return buff.Kind.Equals("Buff", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private static string FormatRdpsBuffName(RdpsBuffWindow window)
	{
		if (window.DisplaySkillLevel <= 0)
		{
			return window.SkillName;
		}
		return $"{window.SkillName} Lv.{window.DisplaySkillLevel}";
	}

	private bool TryResolveRdpsPartyBuffEffect(UiBuffEvent buff, out RdpsPartyBuffEffect? effect)
	{
		if (_rdpsPartyBuffCatalog.TryGetEffectForBuffCode(buff.SkillId, out effect))
		{
			if (buff.SkillLevel > 0 && effect != null && CanResolveRdpsEffectBySkillLevel(effect.SkillId) && _rdpsPartyBuffCatalog.TryGetEffectForSkillLevel(effect.SkillId, buff.SkillLevel, out RdpsPartyBuffEffect effect2) && effect2 != null)
			{
				effect = effect2;
			}
			return true;
		}
		if (_rdpsPartyBuffCatalog.TryGetEffectForBuffCode(buff.BuffId, out effect))
		{
			if (buff.SkillLevel > 0 && effect != null && CanResolveRdpsEffectBySkillLevel(effect.SkillId) && _rdpsPartyBuffCatalog.TryGetEffectForSkillLevel(effect.SkillId, buff.SkillLevel, out RdpsPartyBuffEffect effect3) && effect3 != null)
			{
				effect = effect3;
			}
			return true;
		}
		effect = null;
		return false;
	}

	private int ResolveDetailActorId(int actorId)
	{
		if (actorId <= 0)
		{
			return 0;
		}
		return _engine.Names.ResolveActorId(actorId);
	}

	private string GetDetailActorDisplayName(int actorId)
	{
		if (actorId > 0 && _engine.TryGetActorName(actorId, out string name) && !string.IsNullOrWhiteSpace(name))
		{
			return name;
		}
		DpsCardViewModel dpsCardViewModel = DpsCards.FirstOrDefault((DpsCardViewModel x) => x.ActorId == actorId);
		if (dpsCardViewModel != null && !string.IsNullOrWhiteSpace(dpsCardViewModel.Name))
		{
			return dpsCardViewModel.Name;
		}
		if (actorId <= 0)
		{
			return "-";
		}
		return $"Actor {actorId}";
	}

	private static string FormatPositiveDps(double value)
	{
		if (!(value > 0.5))
		{
			return "-";
		}
		return value.ToString("N0");
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

	private static IReadOnlyList<DpsGraphRow> BuildDpsGraphRows(IReadOnlyList<DamageEvent> events)
	{
		if (events.Count == 0)
		{
			return Array.Empty<DpsGraphRow>();
		}
		DateTime first = events.Min((DamageEvent e) => e.TimestampUtc);
		DateTime dateTime = events.Max((DamageEvent e) => e.TimestampUtc);
		int num = Math.Max(0, (int)Math.Floor((dateTime - first).TotalSeconds));
		var dictionary = (from e in events
			group e by Math.Max(0, (int)Math.Floor((e.TimestampUtc - first).TotalSeconds))).ToDictionary((IGrouping<int, DamageEvent> g) => g.Key, (IGrouping<int, DamageEvent> g) => new
		{
			Damage = ((IEnumerable<DamageEvent>)g).Sum((Func<DamageEvent, long>)((DamageEvent e) => e.Damage)),
			HitCount = g.Count()
		});
		List<DpsGraphRow> list = new List<DpsGraphRow>(num + 1);
		for (int num2 = 0; num2 <= num; num2++)
		{
			dictionary.TryGetValue(num2, out var value);
			long damage = value?.Damage ?? 0;
			int num3 = Math.Max(0, num2 - 5 + 1);
			long num4 = 0L;
			for (int num5 = num3; num5 <= num2; num5++)
			{
				if (dictionary.TryGetValue(num5, out var value2))
				{
					num4 += value2.Damage;
				}
			}
			int num6 = Math.Max(1, num2 - num3 + 1);
			long dps = num4 / num6;
			list.Add(new DpsGraphRow
			{
				Second = num2,
				TimeRange = FormatRelativeTime(TimeSpan.FromSeconds(num2)),
				Dps = dps,
				Damage = damage,
				DpsText = dps.ToString("N0"),
				DamageText = damage.ToString("N0"),
				HitCount = (value?.HitCount ?? 0)
			});
		}
		return list;
	}

	private static IReadOnlyList<DpsGraphRow> SampleDpsGraphRows(IReadOnlyList<DpsGraphRow> rows)
	{
		if (rows.Count <= 240)
		{
			return rows;
		}
		List<DpsGraphRow> list = new List<DpsGraphRow>(240);
		double num = (double)(rows.Count - 1) / 239.0;
		int num2 = -1;
		for (int i = 0; i < 240; i++)
		{
			int value = (int)Math.Round((double)i * num);
			value = Math.Clamp(value, 0, rows.Count - 1);
			if (value != num2)
			{
				list.Add(rows[value]);
				num2 = value;
			}
		}
		if (list.Count != 0)
		{
			if (list[list.Count - 1].Second == rows[rows.Count - 1].Second)
			{
				goto IL_00b8;
			}
		}
		list.Add(rows[rows.Count - 1]);
		goto IL_00b8;
		IL_00b8:
		return list;
	}

	private IReadOnlyList<BuffUptimeRow> BuildBuffUptimeRows(UiActorState actor, IReadOnlyList<DamageEvent> damageEvents)
	{
		if (damageEvents.Count == 0)
		{
			return Array.Empty<BuffUptimeRow>();
		}
		DateTime windowStart = damageEvents.Min((DamageEvent e) => e.TimestampUtc);
		DateTime windowEnd = damageEvents.Max((DamageEvent e) => e.TimestampUtc);
		if (windowEnd <= windowStart)
		{
			windowEnd = windowStart.AddSeconds(1.0);
		}
		List<UiBuffEvent> first;
		List<UiBuffEvent> source;
		lock (_sync)
		{
			PruneActiveBuffEvents(DateTime.UtcNow);
			first = actor.BuffEvents.ToList();
			source = _allBuffEvents.Concat(_activeBuffEvents.Values).ToList();
		}
		int actorId = actor.ActorId;
		List<UiBuffEvent> buffEvents = DeduplicateBuffEvents(first.Concat(source.Where((UiBuffEvent b) => IsBuffEventRelatedToActor(b, actorId))));
		double windowSeconds = Math.Max(1.0, (windowEnd - windowStart).TotalSeconds);
		IReadOnlyList<BuffUptimeRow> readOnlyList = BuildBuffUptimeRowsFromEvents(buffEvents, windowStart, windowEnd, windowSeconds, actorId);
		if (readOnlyList.Count > 0)
		{
			return readOnlyList;
		}
		List<UiBuffEvent> buffEvents2 = DeduplicateBuffEvents(from b in source
			where BuffEventOverlapsWindow(b, windowStart, windowEnd)
			where (b.OwnerId > 0 && b.OwnerId == b.TargetId) || b.ActorId == actorId
			select b);
		return BuildBuffUptimeRowsFromEvents(buffEvents2, windowStart, windowEnd, windowSeconds, actorId);
	}

	private IReadOnlyList<BuffUptimeRow> BuildBuffUptimeRowsFromEvents(IReadOnlyList<UiBuffEvent> buffEvents, DateTime windowStart, DateTime windowEnd, double windowSeconds, int actorId)
	{
		if (buffEvents.Count == 0)
		{
			return Array.Empty<BuffUptimeRow>();
		}
		List<BuffUptimeRow> list = new List<BuffUptimeRow>();
		foreach (IGrouping<int, UiBuffEvent> item in from b in buffEvents
			group b by (b.BuffId <= 0) ? b.SkillId : b.BuffId)
		{
			int key = item.Key;
			if (key <= 0)
			{
				continue;
			}
			BuffInfo info = null;
			_buffNames.TryGet(key, out info);
			if (!IsVisiblePlayerBuff(info))
			{
				continue;
			}
			bool flag = IsConsumableBuff(info);
			bool isOwnSkill = !flag && item.Any((UiBuffEvent b) => IsBuffOwnedByActor(b, actorId));
			item.Where((UiBuffEvent b) => b.Kind.Equals("BuffApplied", StringComparison.OrdinalIgnoreCase)).ToList();
			List<(DateTime, DateTime)> list2 = (from b in item.ToList()
				where BuffIntervalUtilities.HasInterval(b.DurationMs, b.ExpiresAtMs)
				select BuffIntervalUtilities.GetInterval(b.TimestampUtc, b.DurationMs, b.StartedAtMs, b.ExpiresAtMs) into x
				where x.End > windowStart && x.Start < windowEnd
				select (Start: (x.Start < windowStart) ? windowStart : x.Start, End: (x.End > windowEnd) ? windowEnd : x.End) into x
				where x.End > x.Start
				orderby x.Start
				select x).ToList();
			if (list2.Count == 0)
			{
				continue;
			}
			double num = BuffIntervalUtilities.SumMergedSeconds(list2);
			if (!(num <= 0.0))
			{
				double num2 = Math.Clamp(num * 100.0 / windowSeconds, 0.0, 100.0);
				int applyCount = BuffIntervalUtilities.CountMerged(list2);
				string name = ((!string.IsNullOrWhiteSpace(info?.Name)) ? info.Name : $"Buff {key}");
				int num3 = item.Select((UiBuffEvent b) => b.SkillId).FirstOrDefault((int x) => x > 0);
				if (num3 <= 0)
				{
					num3 = key;
				}
				int num4 = ResolveBuffDisplayLevel(item, key, num3, actorId);
				list.Add(new BuffUptimeRow
				{
					IconPath = GetSkillIconPath(num3),
					Name = name,
					LevelText = ((num4 > 0) ? $"Lv.{num4}" : ""),
					ApplyCount = applyCount,
					UptimeSeconds = num,
					UptimeText = FormatDurationShort(num),
					UptimePercentText = $"{num2:0.0}%",
					UptimePercentValue = num2,
					IsConsumable = flag,
					IsOwnSkill = isOwnSkill
				});
			}
		}
		return (from r in list
			orderby r.UptimeSeconds descending, r.Name
			select r).ToList();
	}

	private int ResolveBuffDisplayLevel(IEnumerable<UiBuffEvent> group, int buffKey, int iconCode, int actorId)
	{
		foreach (UiBuffEvent item in group)
		{
			int num = ResolveBuffLevelProviderId(item, actorId);
			if (num > 0)
			{
				if (item.SkillLevel > 0)
				{
					return item.SkillLevel;
				}
				if (TryGetBuffDisplayLevel(num, item.SkillId, out var level) || TryGetBuffDisplayLevel(num, item.BuffId, out level) || TryGetBuffDisplayLevel(num, buffKey, out level) || TryGetBuffDisplayLevel(num, iconCode, out level))
				{
					return level;
				}
			}
		}
		return 0;
	}

	private bool TryGetBuffDisplayLevel(int providerId, int skillCode, out int level)
	{
		if (skillCode <= 0)
		{
			level = 0;
			return false;
		}
		return TryGetStigmaSkillLevelForProvider(providerId, skillCode, out level);
	}

	private int ResolveBuffLevelProviderId(UiBuffEvent buff, int actorId)
	{
		int num = ((buff.OwnerId > 0) ? ResolveDetailActorId(buff.OwnerId) : 0);
		if (num > 0)
		{
			return num;
		}
		int num2 = ((buff.ActorId > 0) ? ResolveDetailActorId(buff.ActorId) : 0);
		if (num2 > 0)
		{
			return num2;
		}
		if (!IsBuffOwnedByActor(buff, actorId))
		{
			return 0;
		}
		return actorId;
	}

	private static bool IsVisiblePlayerBuff(BuffInfo? buffInfo)
	{
		if (buffInfo != null && buffInfo.IconView)
		{
			return string.Equals(buffInfo.Type, "Buff", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}

	private static bool IsConsumableBuff(BuffInfo? buffInfo)
	{
		if (buffInfo == null)
		{
			return false;
		}
		if ((buffInfo.Icon ?? "").StartsWith("Item/", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string text = buffInfo.Name ?? "";
		if (!text.Contains("주문서", StringComparison.Ordinal) && !text.Contains("물약", StringComparison.Ordinal))
		{
			return text.Contains("선약", StringComparison.Ordinal);
		}
		return true;
	}

	private bool IsBuffOwnedByActor(UiBuffEvent buff, int actorId)
	{
		if (actorId <= 0)
		{
			return false;
		}
		if (buff.ActorId == actorId)
		{
			return true;
		}
		if (((buff.OwnerId > 0) ? _engine.Names.ResolveActorId(buff.OwnerId) : 0) == actorId)
		{
			return true;
		}
		if (buff.OwnerId <= 0)
		{
			return ((buff.TargetId > 0) ? _engine.Names.ResolveActorId(buff.TargetId) : 0) == actorId;
		}
		return false;
	}

	private static bool BuffEventOverlapsWindow(UiBuffEvent buff, DateTime windowStart, DateTime windowEnd)
	{
		(DateTime, DateTime) interval = BuffIntervalUtilities.GetInterval(buff.TimestampUtc, buff.DurationMs, buff.StartedAtMs, buff.ExpiresAtMs);
		if (interval.Item2 > windowStart)
		{
			return interval.Item1 < windowEnd;
		}
		return false;
	}

	private bool IsBuffEventRelatedToActor(UiBuffEvent buff, int actorId)
	{
		if (actorId <= 0)
		{
			return false;
		}
		if (buff.ActorId == actorId || buff.OwnerId == actorId || buff.TargetId == actorId)
		{
			return true;
		}
		if (buff.OwnerId > 0 && _engine.Names.ResolveActorId(buff.OwnerId) == actorId)
		{
			return true;
		}
		if (buff.TargetId > 0 && _engine.Names.ResolveActorId(buff.TargetId) == actorId)
		{
			return true;
		}
		return false;
	}

	private static string FormatRelativeTime(TimeSpan time)
	{
		return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}";
	}

	private static string FormatDurationShort(double seconds)
	{
		if (!(seconds < 60.0))
		{
			int num = (int)Math.Round(seconds);
			return $"{num / 60}:{num % 60:00}";
		}
		return $"{seconds:0}s";
	}

	private static string FormatRate(int value, int total)
	{
		if (total > 0)
		{
			return $"{(double)value * 100.0 / (double)total:0.0}%";
		}
		return "0.0%";
	}

	private IReadOnlyList<TraitSlot> BuildTraitSlots(IReadOnlyList<int> skillCodes)
	{
		if (skillCodes.Count == 0)
		{
			return Array.Empty<TraitSlot>();
		}
		string key = string.Join(",", skillCodes);
		if (_traitSlotCache.TryGetValue(key, out IReadOnlyList<TraitSlot> value))
		{
			return value;
		}
		bool[] active = new bool[5];
		string[] tooltips = new string[5];
		foreach (int skillCode in skillCodes)
		{
			RdpsSkillCodeParts rdpsSkillCodeParts = RdpsSkillCatalog.ParseSkillCode(skillCode);
			if (rdpsSkillCodeParts.BaseSkillId <= 0)
			{
				continue;
			}
			_rdpsSkillCatalog.TryGetSkill(skillCode, out RdpsSkillInfo skill);
			foreach (int trait in rdpsSkillCodeParts.TraitIndexes)
			{
				active[trait - 1] = true;
				RdpsSpecializationInfo rdpsSpecializationInfo = skill?.Specializations.FirstOrDefault((RdpsSpecializationInfo x) => x.DisplayIndex == trait);
				tooltips[trait - 1] = ((rdpsSpecializationInfo == null) ? $"특화 {trait}" : $"{skill.Name} 특화 {trait}: {rdpsSpecializationInfo.Description}");
			}
		}
		TraitSlot[] array = (from i in Enumerable.Range(0, 5)
			select new TraitSlot(i + 1, active[i], tooltips[i] ?? $"특화 {i + 1}")).ToArray();
		_traitSlotCache[key] = array;
		return array;
	}

	private bool CanResolveRdpsEffectBySkillLevel(int skillCode)
	{
		return _rdpsSkillCatalog.CanResolveEffectBySkillLevel(skillCode, _rdpsPartyBuffCatalog);
	}

	private void OnStigmaSkillLevelReceived(StigmaSkillLevelEvent info)
	{
		if (info.EffectiveLevel <= 0)
		{
			return;
		}
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			_lastDetailRdpsRefreshTick = 0L;
			int? num = ResolveSelectedDetailActorId();
			if (num.HasValue && IsCombatDetailWindowOpen())
			{
				QueueActorDetailRender(num.Value);
			}
		}, DispatcherPriority.Background);
	}

	private string GetSkillIconPath(int skillCode)
	{
		if (_skillIconPathCache.TryGetValue(skillCode, out string value))
		{
			return value;
		}
		string text = $"pack://application:,,,/Assets/Skills/{skillCode}.png";
		Uri uriResource = new Uri(text);
		string text2;
		try
		{
			StreamResourceInfo resourceStream = System.Windows.Application.GetResourceStream(uriResource);
			if (resourceStream != null)
			{
				resourceStream.Stream?.Dispose();
				text2 = text;
				_skillIconPathCache[skillCode] = text2;
				return text2;
			}
		}
		catch
		{
		}
		string nameOrCode = _skillNames.GetNameOrCode(skillCode);
		if (TryGetSpiritSkillIconFallback(skillCode, nameOrCode, out var iconSkillCode))
		{
			text2 = $"pack://application:,,,/Assets/Skills/{iconSkillCode}.png";
			_skillIconPathCache[skillCode] = text2;
			return text2;
		}
		int num = skillCode / 10000 * 10000;
		int num2 = -1;
		foreach (int registeredId in _skillNames.GetRegisteredIds())
		{
			if (registeredId < num || registeredId >= skillCode || (num2 != -1 && registeredId <= num2))
			{
				continue;
			}
			try
			{
				StreamResourceInfo resourceStream2 = System.Windows.Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/Skills/{registeredId}.png"));
				if (resourceStream2 != null)
				{
					resourceStream2.Stream?.Dispose();
					num2 = registeredId;
				}
			}
			catch
			{
			}
		}
		int value2 = ((num2 != -1) ? num2 : skillCode);
		text2 = $"pack://application:,,,/Assets/Skills/{value2}.png";
		_skillIconPathCache[skillCode] = text2;
		return text2;
	}

	private static bool TryGetSpiritSkillIconFallback(int skillCode, string? skillName, out int iconSkillCode)
	{
		iconSkillCode = 0;
		if (!string.IsNullOrWhiteSpace(skillName))
		{
			if (skillName.Contains("불의 정령", StringComparison.Ordinal))
			{
				iconSkillCode = 16100000;
			}
			else if (skillName.Contains("물의 정령", StringComparison.Ordinal))
			{
				iconSkillCode = 16110000;
			}
			else if (skillName.Contains("바람의 정령", StringComparison.Ordinal))
			{
				iconSkillCode = 16120000;
			}
			else if (skillName.Contains("땅의 정령", StringComparison.Ordinal))
			{
				iconSkillCode = 16130000;
			}
			else if (skillName.Contains("고대의 정령", StringComparison.Ordinal))
			{
				iconSkillCode = 16250000;
			}
			if (iconSkillCode > 0)
			{
				return true;
			}
		}
		int num;
		switch (skillCode)
		{
		case 100011:
		case 100012:
		case 100013:
		case 100014:
		case 100015:
		case 100016:
		case 100017:
		case 100018:
			num = 16100000;
			break;
		case 100021:
		case 100022:
		case 100023:
		case 100024:
		case 100025:
		case 100026:
		case 100027:
		case 100028:
			num = 16110000;
			break;
		case 100031:
		case 100032:
		case 100033:
		case 100034:
		case 100035:
		case 100036:
		case 100037:
		case 100038:
			num = 16120000;
			break;
		case 100041:
		case 100042:
		case 100043:
		case 100044:
		case 100045:
		case 100046:
		case 100047:
		case 100048:
			num = 16130000;
			break;
		case 100051:
		case 100052:
		case 100053:
		case 100054:
		case 100055:
		case 100056:
		case 100057:
		case 100058:
			num = 16250000;
			break;
		case 1000100:
		case 1000101:
		case 1000102:
		case 1000103:
		case 1000104:
		case 1000105:
		case 1000106:
		case 1000107:
		case 1000108:
		case 1000109:
		case 1000110:
		case 1000111:
		case 1000112:
		case 1000113:
		case 1000114:
		case 1000115:
		case 1000116:
		case 1000117:
		case 1000118:
		case 1000119:
		case 1000120:
		case 1000121:
		case 1000122:
		case 1000123:
		case 1000124:
		case 1000125:
		case 1000126:
		case 1000127:
		case 1000128:
		case 1000129:
		case 1000130:
		case 1000131:
		case 1000132:
		case 1000133:
		case 1000134:
		case 1000135:
		case 1000136:
		case 1000137:
		case 1000138:
		case 1000139:
		case 1000140:
		case 1000141:
		case 1000142:
		case 1000143:
		case 1000144:
		case 1000145:
		case 1000146:
		case 1000147:
		case 1000148:
		case 1000149:
		case 1000150:
		case 1000151:
		case 1000152:
		case 1000153:
		case 1000154:
		case 1000155:
		case 1000156:
		case 1000157:
		case 1000158:
		case 1000159:
		case 1000160:
		case 1000161:
		case 1000162:
		case 1000163:
		case 1000164:
		case 1000165:
		case 1000166:
		case 1000167:
		case 1000168:
		case 1000169:
		case 1000170:
		case 1000171:
		case 1000172:
		case 1000173:
		case 1000174:
		case 1000175:
		case 1000176:
		case 1000177:
		case 1000178:
		case 1000179:
		case 1000180:
		case 1000181:
		case 1000182:
		case 1000183:
		case 1000184:
		case 1000185:
		case 1000186:
		case 1000187:
		case 1000188:
		case 1000189:
		case 1000190:
		case 1000191:
		case 1000192:
		case 1000193:
		case 1000194:
		case 1000195:
		case 1000196:
		case 1000197:
		case 1000198:
		case 1000199:
			num = 16100000;
			break;
		case 1000200:
		case 1000201:
		case 1000202:
		case 1000203:
		case 1000204:
		case 1000205:
		case 1000206:
		case 1000207:
		case 1000208:
		case 1000209:
		case 1000210:
		case 1000211:
		case 1000212:
		case 1000213:
		case 1000214:
		case 1000215:
		case 1000216:
		case 1000217:
		case 1000218:
		case 1000219:
		case 1000220:
		case 1000221:
		case 1000222:
		case 1000223:
		case 1000224:
		case 1000225:
		case 1000226:
		case 1000227:
		case 1000228:
		case 1000229:
		case 1000230:
		case 1000231:
		case 1000232:
		case 1000233:
		case 1000234:
		case 1000235:
		case 1000236:
		case 1000237:
		case 1000238:
		case 1000239:
		case 1000240:
		case 1000241:
		case 1000242:
		case 1000243:
		case 1000244:
		case 1000245:
		case 1000246:
		case 1000247:
		case 1000248:
		case 1000249:
		case 1000250:
		case 1000251:
		case 1000252:
		case 1000253:
		case 1000254:
		case 1000255:
		case 1000256:
		case 1000257:
		case 1000258:
		case 1000259:
		case 1000260:
		case 1000261:
		case 1000262:
		case 1000263:
		case 1000264:
		case 1000265:
		case 1000266:
		case 1000267:
		case 1000268:
		case 1000269:
		case 1000270:
		case 1000271:
		case 1000272:
		case 1000273:
		case 1000274:
		case 1000275:
		case 1000276:
		case 1000277:
		case 1000278:
		case 1000279:
		case 1000280:
		case 1000281:
		case 1000282:
		case 1000283:
		case 1000284:
		case 1000285:
		case 1000286:
		case 1000287:
		case 1000288:
		case 1000289:
		case 1000290:
		case 1000291:
		case 1000292:
		case 1000293:
		case 1000294:
		case 1000295:
		case 1000296:
		case 1000297:
		case 1000298:
		case 1000299:
			num = 16110000;
			break;
		case 1000300:
		case 1000301:
		case 1000302:
		case 1000303:
		case 1000304:
		case 1000305:
		case 1000306:
		case 1000307:
		case 1000308:
		case 1000309:
		case 1000310:
		case 1000311:
		case 1000312:
		case 1000313:
		case 1000314:
		case 1000315:
		case 1000316:
		case 1000317:
		case 1000318:
		case 1000319:
		case 1000320:
		case 1000321:
		case 1000322:
		case 1000323:
		case 1000324:
		case 1000325:
		case 1000326:
		case 1000327:
		case 1000328:
		case 1000329:
		case 1000330:
		case 1000331:
		case 1000332:
		case 1000333:
		case 1000334:
		case 1000335:
		case 1000336:
		case 1000337:
		case 1000338:
		case 1000339:
		case 1000340:
		case 1000341:
		case 1000342:
		case 1000343:
		case 1000344:
		case 1000345:
		case 1000346:
		case 1000347:
		case 1000348:
		case 1000349:
		case 1000350:
		case 1000351:
		case 1000352:
		case 1000353:
		case 1000354:
		case 1000355:
		case 1000356:
		case 1000357:
		case 1000358:
		case 1000359:
		case 1000360:
		case 1000361:
		case 1000362:
		case 1000363:
		case 1000364:
		case 1000365:
		case 1000366:
		case 1000367:
		case 1000368:
		case 1000369:
		case 1000370:
		case 1000371:
		case 1000372:
		case 1000373:
		case 1000374:
		case 1000375:
		case 1000376:
		case 1000377:
		case 1000378:
		case 1000379:
		case 1000380:
		case 1000381:
		case 1000382:
		case 1000383:
		case 1000384:
		case 1000385:
		case 1000386:
		case 1000387:
		case 1000388:
		case 1000389:
		case 1000390:
		case 1000391:
		case 1000392:
		case 1000393:
		case 1000394:
		case 1000395:
		case 1000396:
		case 1000397:
		case 1000398:
		case 1000399:
			num = 16120000;
			break;
		case 1000400:
		case 1000401:
		case 1000402:
		case 1000403:
		case 1000404:
		case 1000405:
		case 1000406:
		case 1000407:
		case 1000408:
		case 1000409:
		case 1000410:
		case 1000411:
		case 1000412:
		case 1000413:
		case 1000414:
		case 1000415:
		case 1000416:
		case 1000417:
		case 1000418:
		case 1000419:
		case 1000420:
		case 1000421:
		case 1000422:
		case 1000423:
		case 1000424:
		case 1000425:
		case 1000426:
		case 1000427:
		case 1000428:
		case 1000429:
		case 1000430:
		case 1000431:
		case 1000432:
		case 1000433:
		case 1000434:
		case 1000435:
		case 1000436:
		case 1000437:
		case 1000438:
		case 1000439:
		case 1000440:
		case 1000441:
		case 1000442:
		case 1000443:
		case 1000444:
		case 1000445:
		case 1000446:
		case 1000447:
		case 1000448:
		case 1000449:
		case 1000450:
		case 1000451:
		case 1000452:
		case 1000453:
		case 1000454:
		case 1000455:
		case 1000456:
		case 1000457:
		case 1000458:
		case 1000459:
		case 1000460:
		case 1000461:
		case 1000462:
		case 1000463:
		case 1000464:
		case 1000465:
		case 1000466:
		case 1000467:
		case 1000468:
		case 1000469:
		case 1000470:
		case 1000471:
		case 1000472:
		case 1000473:
		case 1000474:
		case 1000475:
		case 1000476:
		case 1000477:
		case 1000478:
		case 1000479:
		case 1000480:
		case 1000481:
		case 1000482:
		case 1000483:
		case 1000484:
		case 1000485:
		case 1000486:
		case 1000487:
		case 1000488:
		case 1000489:
		case 1000490:
		case 1000491:
		case 1000492:
		case 1000493:
		case 1000494:
		case 1000495:
		case 1000496:
		case 1000497:
		case 1000498:
		case 1000499:
			num = 16130000;
			break;
		case 1000500:
		case 1000501:
		case 1000502:
		case 1000503:
		case 1000504:
		case 1000505:
		case 1000506:
		case 1000507:
		case 1000508:
		case 1000509:
		case 1000510:
		case 1000511:
		case 1000512:
		case 1000513:
		case 1000514:
		case 1000515:
		case 1000516:
		case 1000517:
		case 1000518:
		case 1000519:
		case 1000520:
		case 1000521:
		case 1000522:
		case 1000523:
		case 1000524:
		case 1000525:
		case 1000526:
		case 1000527:
		case 1000528:
		case 1000529:
		case 1000530:
		case 1000531:
		case 1000532:
		case 1000533:
		case 1000534:
		case 1000535:
		case 1000536:
		case 1000537:
		case 1000538:
		case 1000539:
		case 1000540:
		case 1000541:
		case 1000542:
		case 1000543:
		case 1000544:
		case 1000545:
		case 1000546:
		case 1000547:
		case 1000548:
		case 1000549:
		case 1000550:
		case 1000551:
		case 1000552:
		case 1000553:
		case 1000554:
		case 1000555:
		case 1000556:
		case 1000557:
		case 1000558:
		case 1000559:
		case 1000560:
		case 1000561:
		case 1000562:
		case 1000563:
		case 1000564:
		case 1000565:
		case 1000566:
		case 1000567:
		case 1000568:
		case 1000569:
		case 1000570:
		case 1000571:
		case 1000572:
		case 1000573:
		case 1000574:
		case 1000575:
		case 1000576:
		case 1000577:
		case 1000578:
		case 1000579:
		case 1000580:
		case 1000581:
		case 1000582:
		case 1000583:
		case 1000584:
		case 1000585:
		case 1000586:
		case 1000587:
		case 1000588:
		case 1000589:
		case 1000590:
		case 1000591:
		case 1000592:
		case 1000593:
		case 1000594:
		case 1000595:
		case 1000596:
		case 1000597:
		case 1000598:
		case 1000599:
			num = 16250000;
			break;
		default:
			num = 0;
			break;
		}
		iconSkillCode = num;
		return iconSkillCode > 0;
	}

	private static double ParseForSort(object? val)
	{
		if (val == null)
		{
			return 0.0;
		}
		if (double.TryParse(val.ToString()?.Replace(",", "") ?? "", out var result))
		{
			return result;
		}
		return 0.0;
	}

	private void RenderLogOnly(int actorId)
	{
		ArchivedBossRecord archivedRecord;
		UiActorState detailActorState = GetDetailActorState(actorId, out archivedRecord);
		if (detailActorState == null)
		{
			SetDetailCombatTime(TimeSpan.Zero);
			_detailWindow?.SetLogRows(Array.Empty<LogRow>());
			return;
		}
		List<DamageEvent> filteredActorEvents = GetFilteredActorEvents(detailActorState);
		SetDetailCombatTimeFromEvents(filteredActorEvents);
		List<LogRow> logRows = (from ev in filteredActorEvents.OrderByDescending((DamageEvent x) => x.TimestampUtc).Take(800)
			select new LogRow
			{
				TimeStr = ev.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff"),
				TargetName = ((archivedRecord != null && archivedRecord.TargetId == ev.TargetId) ? archivedRecord.TargetName : (_engine.TryGetActorName(ev.TargetId, out string name) ? (name ?? ev.TargetId.ToString()) : ev.TargetId.ToString())),
				SkillName = _skillNames.GetNameOrCode(ev.SkillCodeRaw),
				Damage = ev.Damage,
				Specials = ((ev.Specials != null && ev.Specials.Count > 0) ? string.Join(", ", ev.Specials) : (ev.IsCrit ? "CRIT" : ""))
			}).ToList();
		_detailWindow?.SetLogRows(logRows);
	}

	private void LstDps_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (lstDps.SelectedItem is DpsCardViewModel dpsCardViewModel)
		{
			string dpsCardCharacterKey = GetDpsCardCharacterKey(dpsCardViewModel);
			bool flag = ((dpsCardCharacterKey != null) ? string.Equals(dpsCardCharacterKey, _lastDoubleClickedCharacterKey, StringComparison.Ordinal) : (_lastDoubleClickedActorId == dpsCardViewModel.ActorId));
			if (IsCombatDetailWindowOpen() && flag)
			{
				CloseCombatDetailWindow();
				_lastDoubleClickedActorId = null;
				_lastDoubleClickedCharacterKey = null;
			}
			else
			{
				_lastDoubleClickedActorId = dpsCardViewModel.ActorId;
				_lastDoubleClickedCharacterKey = dpsCardCharacterKey;
				OpenDetailForActor(dpsCardViewModel.ActorId);
			}
		}
	}

	private void LstDps_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
	{
		if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
		{
			BtnCopy_Click(null, null);
			e.Handled = true;
		}
	}

	private void LstDps_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
		{
			double num = MeterScaleOptions.NormalizeTextScale(_textScale + ((e.Delta > 0) ? 0.01 : (-0.01)));
			if (Math.Abs(num - _textScale) < 0.001)
			{
				e.Handled = true;
				return;
			}
			_textScale = num;
			ApplyMeterScale(force: true);
			lstDps.Items.Refresh();
			lstLookup.Items.Refresh();
			SaveConfig();
			e.Handled = true;
		}
	}

	private void LstDps_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		lstDps.Focus();
		if (lstDps.SelectedItem is DpsCardViewModel selectedDetailCard)
		{
			SetSelectedDetailCard(selectedDetailCard);
			if (IsCombatDetailWindowOpen())
			{
				RenderSelectedActorDetail();
			}
		}
	}

	private void LstDps_PreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		HitTestResult hitTestResult = VisualTreeHelper.HitTest(lstDps, e.GetPosition(lstDps));
		if (hitTestResult?.VisualHit != null && FindParent<ListBoxItem>(hitTestResult.VisualHit) == null)
		{
			lstDps.SelectedItem = null;
			ClearSelectedDetailTarget();
			ClearCombatDetailRows();
		}
	}

	private void LstDps_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (FindParent<ListBoxItem>(VisualTreeHelper.HitTest(lstDps, e.GetPosition(lstDps))?.VisualHit)?.DataContext is DpsCardViewModel dpsCardViewModel)
		{
			lstDps.SelectedItem = dpsCardViewModel;
			SetSelectedDetailCard(dpsCardViewModel);
			_contextMenuActorId = dpsCardViewModel.ActorId;
		}
		else
		{
			_contextMenuActorId = null;
		}
		lstDps.ContextMenu = BuildDpsContextMenu();
	}

	private void LstDps_ContextMenuOpening(object sender, ContextMenuEventArgs e)
	{
		lstDps.ContextMenu = BuildDpsContextMenu();
	}

	private System.Windows.Controls.ContextMenu BuildDpsContextMenu()
	{
		System.Windows.Controls.ContextMenu contextMenu = new System.Windows.Controls.ContextMenu();
		contextMenu.SetResourceReference(FrameworkElement.StyleProperty, "ThemedContextMenu");
		if (_contextMenuActorId.HasValue)
		{
			int actorId = _contextMenuActorId.Value;
			System.Windows.Controls.MenuItem menuItem = new System.Windows.Controls.MenuItem
			{
				Header = "상세정보 열기"
			};
			menuItem.SetResourceReference(FrameworkElement.StyleProperty, "ThemedMenuItem");
			menuItem.Click += delegate
			{
				OpenDetailForActor(actorId);
			};
			contextMenu.Items.Add(menuItem);
			Separator separator = new Separator();
			separator.SetResourceReference(FrameworkElement.StyleProperty, "ThemedMenuSeparator");
			contextMenu.Items.Add(separator);
		}
		System.Windows.Controls.MenuItem confirmedOnlyItem = new System.Windows.Controls.MenuItem
		{
			Header = "확인된 유저만 표시",
			IsCheckable = true,
			IsChecked = (chkShowUnknown.IsChecked == true)
		};
		confirmedOnlyItem.SetResourceReference(FrameworkElement.StyleProperty, "ThemedMenuItem");
		confirmedOnlyItem.Click += delegate
		{
			chkShowUnknown.IsChecked = confirmedOnlyItem.IsChecked;
			BtnShowUnknown_Click(chkShowUnknown, new RoutedEventArgs());
		};
		contextMenu.Items.Add(confirmedOnlyItem);
		System.Windows.Controls.MenuItem hideNicknameItem = new System.Windows.Controls.MenuItem
		{
			Header = "닉네임 비공개 모드",
			IsCheckable = true,
			IsChecked = (chkHideNickname.IsChecked == true)
		};
		hideNicknameItem.SetResourceReference(FrameworkElement.StyleProperty, "ThemedMenuItem");
		hideNicknameItem.Click += delegate
		{
			chkHideNickname.IsChecked = hideNicknameItem.IsChecked;
			BtnHideNickname_Click(chkHideNickname, new RoutedEventArgs());
		};
		contextMenu.Items.Add(hideNicknameItem);
		System.Windows.Controls.MenuItem minimalPresetItem = new System.Windows.Controls.MenuItem
		{
			Header = "미니멀 표시",
			IsCheckable = true,
			IsChecked = (_displayPreset == MeterDisplayPreset.Minimal)
		};
		minimalPresetItem.SetResourceReference(FrameworkElement.StyleProperty, "ThemedMenuItem");
		minimalPresetItem.Click += delegate
		{
			SetDisplayPreset((!minimalPresetItem.IsChecked) ? MeterDisplayPreset.Standard : MeterDisplayPreset.Minimal);
		};
		contextMenu.Items.Add(minimalPresetItem);
		if (_isLogViewMode && _isPaused)
		{
			Separator separator2 = new Separator();
			separator2.SetResourceReference(FrameworkElement.StyleProperty, "ThemedMenuSeparator");
			contextMenu.Items.Add(separator2);
			System.Windows.Controls.MenuItem menuItem2 = new System.Windows.Controls.MenuItem
			{
				Header = "실시간 분석 재개"
			};
			menuItem2.SetResourceReference(FrameworkElement.StyleProperty, "ThemedMenuItem");
			menuItem2.Click += delegate
			{
				BtnPause_Click(btnPrimaryAction, new RoutedEventArgs());
			};
			contextMenu.Items.Add(menuItem2);
		}
		return contextMenu;
	}

	private void SetDisplayPreset(MeterDisplayPreset preset)
	{
		if (_displayPreset != preset)
		{
			_displayPreset = preset;
			ApplyDisplayPresetVisualState(forceScale: true);
			SaveConfig();
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				RenderTiles(GetSnapshotForCurrentFilter());
				RefreshCurrentLayoutState();
				CaptureCompactDpsWidth();
			}, DispatcherPriority.Loaded);
		}
	}

	private void ApplyDisplayPresetVisualState(bool forceScale = false)
	{
		foreach (DpsCardViewModel dpsCard in DpsCards)
		{
			if (dpsCard.DisplayPreset != _displayPreset)
			{
				dpsCard.DisplayPreset = _displayPreset;
			}
			if (!string.Equals(dpsCard.Theme, CurrentThemeName, StringComparison.OrdinalIgnoreCase))
			{
				dpsCard.Theme = CurrentThemeName;
			}
		}
		foreach (PartyMemberItem partyMember in PartyMembers)
		{
			if (partyMember.DisplayPreset != _displayPreset)
			{
				partyMember.DisplayPreset = _displayPreset;
			}
		}
		ApplyHeaderScale();
		ApplyDpsListSpacing();
		ApplyMeterScale(forceScale);
		lstDps?.Items.Refresh();
		lstLookup?.Items.Refresh();
	}

	private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
	{
		while (child != null)
		{
			if (child is T result)
			{
				return result;
			}
			child = VisualTreeHelper.GetParent(child);
		}
		return null;
	}

	private void BtnShowUnknown_Click(object sender, RoutedEventArgs e)
	{
		RenderTiles(GetSnapshotForCurrentFilter());
	}

	private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_engine == null || cmbFilterTarget == null || _isUpdatingTargetCombo)
		{
			return;
		}
		try
		{
			if (!(cmbFilterTarget.SelectedItem is ComboBoxItem comboBoxItem) || !TryGetTargetFilterOption(comboBoxItem.Tag, out TargetFilterOption option))
			{
				return;
			}
			if (option.Kind == TargetFilterItemKind.ClearHistory)
			{
				ClearArchivedBossHistory();
				return;
			}
			SetSelectedTargetFilterOption(option, syncCombo: false);
			CombatSnapshot snapshotForCurrentFilter = GetSnapshotForCurrentFilter();
			if (snapshotForCurrentFilter != null)
			{
				RenderTiles(snapshotForCurrentFilter);
			}
			if (IsCombatDetailWindowOpen())
			{
				RenderDetailForCurrentEncounter();
			}
			RefreshLocalEncounterPanelRows();
		}
		catch
		{
		}
	}

	private void BtnHideNickname_Click(object sender, RoutedEventArgs e)
	{
		RenderTiles(GetSnapshotForCurrentFilter());
		UpdatePartyMemberDisplayNames();
	}

	private void UpdatePartyMemberDisplayNames()
	{
		base.Dispatcher.Invoke(delegate
		{
			foreach (PartyMemberItem partyMember in PartyMembers)
			{
				partyMember.DisplayName = GetDisplayCharacterName(partyMember.Name);
			}
		});
	}

	private void BtnMinimize_Click(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void Image_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left)
		{
			DragMove();
			SaveConfig();
		}
	}

	private void LoadConfig()
	{
		try
		{
			bool flag = false;
			bool flag2 = false;
			bool saveConfigAfterLoad = false;
			if (File.Exists(_configPath))
			{
				string[] array = File.ReadAllLines(_configPath);
				for (int i = 0; i < array.Length; i++)
				{
					string text = array[i].Trim();
					if (text.StartsWith("#") || string.IsNullOrWhiteSpace(text))
					{
						continue;
					}
					string[] array2 = text.Split('=', 2);
					if (array2.Length != 2)
					{
						continue;
					}
					string text2 = array2[0].Trim();
					string text3 = array2[1].Trim();
					if (text2 == "Topmost" && bool.TryParse(text3, out var result))
					{
						base.Topmost = result;
						_preHudTopmost = result;
						UpdateTopmostButtonUI();
						continue;
					}
					switch (text2)
					{
					case "PauseHotkey":
						_pauseHotkey = "None";
						continue;
					case "ClearHotkey":
						_clearHotkey = (string.IsNullOrWhiteSpace(text3) ? "Ctrl+R" : text3);
						continue;
					case "HudHotkey":
						_hudHotkey = "None";
						continue;
					case "HideHotkey":
						_hideHotkey = (string.IsNullOrWhiteSpace(text3) ? "None" : text3);
						continue;
					case "ClickThroughHotkey":
						_clickThroughHotkey = (string.IsNullOrWhiteSpace(text3) ? "None" : text3);
						continue;
					case "MainViewHotkey":
						_mainViewHotkey = (string.IsNullOrWhiteSpace(text3) ? "None" : text3);
						continue;
					case "MaxDpsCards":
					{
						if (int.TryParse(text3, out var result2))
						{
							_maxDpsCards = Math.Clamp(result2, 1, 10);
							continue;
						}
						break;
					}
					}
					if (text2 == "MaxDpsCardsForce10Applied" && bool.TryParse(text3, out var result3))
					{
						_maxDpsCardsForce10Applied = result3;
						continue;
					}
					if (text2 == "ShowActorId" && bool.TryParse(text3, out var result4))
					{
						_showActorId = result4;
						continue;
					}
					if (text2 == "HudHeight" && TryParseConfigDouble(text3, out var number))
					{
						_hudHeight = number;
						continue;
					}
					if (text2 == "HudWidth" && TryParseConfigDouble(text3, out var number2))
					{
						_hudWidth = Math.Max(315.0, number2);
						continue;
					}
					if (text2 == "FullWidth" && TryParseConfigDouble(text3, out var number3))
					{
						_fullWidth = Math.Max(700.0, number3);
						continue;
					}
					if (text2 == "PartyWidth" && TryParseConfigDouble(text3, out var number4))
					{
						_partyWidth = Math.Max(500.0, number4);
						continue;
					}
					if (text2 == "CompactWidth" && TryParseConfigDouble(text3, out var number5))
					{
						_compactWidth = Math.Clamp(number5, 315.0, 1400.0);
						continue;
					}
					if (text2 == "NormalHeight" && TryParseConfigDouble(text3, out var number6))
					{
						_normalHeight = Math.Max(240.0, number6);
						continue;
					}
					if (text2 == "WindowLeft" && TryParseConfigDouble(text3, out var number7))
					{
						_savedWindowLeft = number7;
						continue;
					}
					if (text2 == "WindowTop" && TryParseConfigDouble(text3, out var number8))
					{
						_savedWindowTop = number8;
						continue;
					}
					if (text2 == "AutoBossFilter" && bool.TryParse(text3, out var result5))
					{
						_autoBossFilter = true;
						continue;
					}
					if (text2 == "BossOnlyMeasurement" && bool.TryParse(text3, out result5))
					{
						_bossOnlyMeasurement = true;
						_engine.BossOnlyMeasurement = true;
						continue;
					}
					if (text2 == "AutoResetOnMapChange" && bool.TryParse(text3, out result5))
					{
						_autoResetOnMapChange = false;
						_engine.MapChangeAutoReset = false;
						continue;
					}
					if (text2 == "AutoResetOnNewBoss" && bool.TryParse(text3, out result5))
					{
						_autoResetOnNewBoss = true;
						continue;
					}
					if (text2 == "SaveEncounterLogs" && bool.TryParse(text3, out var result6))
					{
						_saveEncounterLogs = result6;
						_engine.SaveEncounterLogs = result6;
						continue;
					}
					if (text2 == "HudClickThrough" && bool.TryParse(text3, out var result7))
					{
						_hudClickThrough = result7;
						continue;
					}
					if (text2 == "BuffTimerEnabled" && bool.TryParse(text3, out var result8))
					{
						_buffTimerEnabled = result8;
						continue;
					}
					if (text2 == "BuffTimerLeft" && TryParseConfigDouble(text3, out var number9))
					{
						_buffTimerLeft = number9;
						continue;
					}
					if (text2 == "BuffTimerTop" && TryParseConfigDouble(text3, out var number10))
					{
						_buffTimerTop = number10;
						continue;
					}
					if (text2 == "BuffTimerWidth" && TryParseConfigDouble(text3, out var number11))
					{
						_buffTimerWidth = Math.Max(96.0, number11);
						continue;
					}
					if (text2 == "BuffTimerHeight" && TryParseConfigDouble(text3, out var number12))
					{
						_buffTimerHeight = Math.Max(84.0, number12);
						continue;
					}
					if (text2 == "BuffTimerHiddenKeys")
					{
						_hiddenBuffTimerKeys.UnionWith(ParseBuffTimerHiddenKeys(text3));
						continue;
					}
					if (text2 == "ShowBossCard" && bool.TryParse(text3, out var result9))
					{
						_showBossCard = result9;
						continue;
					}
					if (text2 == "ShowDpsCardCombatTime" && bool.TryParse(text3, out var result10))
					{
						_showDpsCardCombatTime = result10;
						continue;
					}
					if (text2 == "AutoHideBackground" && bool.TryParse(text3, out var result11))
					{
						_autoHideBackground = result11;
						continue;
					}
					if (text2 == "ShowOnlyWhenAionActive" && bool.TryParse(text3, out var result12))
					{
						_showOnlyWhenAionActive = result12;
						continue;
					}
					if (text2 == "ShowInTaskbar" && bool.TryParse(text3, out var result13))
					{
						_showInTaskbar = result13;
						continue;
					}
					if (text2 == "CloseButtonBehavior" && TryParseCloseButtonBehavior(text3, out var behavior))
					{
						_closeButtonBehavior = behavior;
						continue;
					}
					if (text2 == "WindowOpacity" && TryParseOpacity(text3, out var opacity))
					{
						_windowOpacity = opacity;
						continue;
					}
					if (text2 == "HudOpacity" && TryParseOpacity(text3, out var opacity2))
					{
						_hudOpacity = opacity2;
						continue;
					}
					if (text2 == "DisplayPreset" && TryParseDisplayPreset(text3, out var preset))
					{
						_displayPreset = preset;
						continue;
					}
					if (text2 == "DpsCardNumberFormatMode" && Enum.TryParse<DpsCardNumberFormatMode>(text3, out var result14))
					{
						_dpsCardNumberFormatMode = result14;
						continue;
					}
					if (text2 == "UiScale" && TryParseConfigDouble(text3, out var number13))
					{
						_uiScale = MeterScaleOptions.NormalizeUiScale(number13);
						flag = true;
						continue;
					}
					if (text2 == "TextScale" && TryParseConfigDouble(text3, out var number14))
					{
						_textScale = MeterScaleOptions.NormalizeTextScale(number14);
						flag2 = true;
						continue;
					}
					if (text2 == "FontSizeMode" && Enum.TryParse<MeterFontSizeMode>(text3, out var result15))
					{
						if (!flag)
						{
							_uiScale = MeterScaleOptions.UiScaleForLegacyMode(result15);
						}
						if (!flag2)
						{
							_textScale = MeterScaleOptions.TextScaleForLegacyMode(result15);
						}
						continue;
					}
					if (text2 == "FontWeightMode" && Enum.TryParse<MeterFontWeightMode>(text3, out var result16))
					{
						_fontWeightMode = result16;
						continue;
					}
					if (text2 == "FontFamily")
					{
						_fontFamilyName = MeterFontFamilies.NormalizeForStorage(text3);
						continue;
					}
					if (text2 == "TextShadowEnabled" && bool.TryParse(text3, out var result17))
					{
						_textShadowEnabled = result17;
						continue;
					}
					if (text2 == "DamageShareMode" && Enum.TryParse<DamageShareMode>(text3, out var result18))
					{
						_damageShareMode = result18;
						continue;
					}
					if (text2 == "DamageShareGraphMode" && Enum.TryParse<DamageShareGraphMode>(text3, out var result19))
					{
						_damageShareGraphMode = result19;
						continue;
					}
					if (text2 == "CaptureBackend" && Enum.TryParse<CaptureBackend>(text3, out var result20))
					{
						_captureBackend = result20;
						continue;
					}
					if (text2 == "DevKey")
					{
						_devKey = text3;
						continue;
					}
					if (text2 == "LookupSkillDisplayEnabled" && bool.TryParse(text3, out var result21))
					{
						_lookupSkillDisplayEnabled = result21;
						continue;
					}
					switch (text2)
					{
					case "LookupSkillSelections":
						_lookupSkillSelections = LookupSkillSelectionSerializer.Parse(text3);
						break;
					case "LookupSkillDisabledClasses":
						_lookupSkillDisabledClasses = LookupSkillClassSetSerializer.Parse(text3);
						break;
					case "UiMode":
					case "StyleMode":
					case "IsHudMode":
						_isHudMode = AppearanceCatalog.ParseUiMode(text3) == MeterUiMode.Hud;
						break;
					case "Theme":
						SetAppearance(AppearanceCatalog.FromLegacyThemeName(text3), applyResources: true);
						break;
					}
				}
			}
			_hudHotkey = "None";
			_isHudMode = true;
			if (!_maxDpsCardsForce10Applied)
			{
				_maxDpsCards = 10;
				_maxDpsCardsForce10Applied = true;
				saveConfigAfterLoad = File.Exists(_configPath);
			}
			_saveConfigAfterLoad = saveConfigAfterLoad;
		}
		catch
		{
		}
	}

	private static bool TryParseDisplayPreset(string value, out MeterDisplayPreset preset)
	{
		string text = value.Trim();
		if (Enum.TryParse<MeterDisplayPreset>(text, ignoreCase: true, out preset))
		{
			return true;
		}
		if (string.Equals(text, "미니멀", StringComparison.OrdinalIgnoreCase))
		{
			preset = MeterDisplayPreset.Minimal;
			return true;
		}
		if (string.Equals(text, "기본", StringComparison.OrdinalIgnoreCase))
		{
			preset = MeterDisplayPreset.Standard;
			return true;
		}
		preset = MeterDisplayPreset.Standard;
		return false;
	}

	private static bool TryParseCloseButtonBehavior(string value, out CloseButtonBehavior behavior)
	{
		string text = value.Trim();
		if (Enum.TryParse<CloseButtonBehavior>(text, ignoreCase: true, out behavior))
		{
			return true;
		}
		if (string.Equals(text, "Tray", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "Minimize", StringComparison.OrdinalIgnoreCase))
		{
			behavior = CloseButtonBehavior.MinimizeToTray;
			return true;
		}
		if (string.Equals(text, "Close", StringComparison.OrdinalIgnoreCase))
		{
			behavior = CloseButtonBehavior.Exit;
			return true;
		}
		behavior = CloseButtonBehavior.Ask;
		return false;
	}

	private static bool TryParseConfigDouble(string value, out double number)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
		{
			return double.TryParse(value, out number);
		}
		return true;
	}

	private static HashSet<int> ParseBuffTimerHiddenKeys(string value)
	{
		HashSet<int> hashSet = new HashSet<int>();
		string[] array = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		for (int i = 0; i < array.Length; i++)
		{
			if (int.TryParse(array[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) && result > 0)
			{
				hashSet.Add(result);
			}
		}
		return hashSet;
	}

	private static string SerializeBuffTimerHiddenKeys(IEnumerable<int> keys)
	{
		return string.Join(",", from key in keys
			where key > 0
			orderby key
			select key.ToString(CultureInfo.InvariantCulture));
	}

	private void ApplySavedWindowPlacement()
	{
		if (_savedWindowLeft.HasValue && _savedWindowTop.HasValue)
		{
			double value = _savedWindowLeft.Value;
			double value2 = _savedWindowTop.Value;
			if (IsUsableWindowPosition(value, value2))
			{
				base.WindowStartupLocation = WindowStartupLocation.Manual;
				base.Left = value;
				base.Top = value2;
			}
		}
	}

	private static bool IsUsableWindowPosition(double left, double top)
	{
		if (double.IsNaN(left) || double.IsNaN(top) || double.IsInfinity(left) || double.IsInfinity(top))
		{
			return false;
		}
		double num = 80.0;
		double num2 = 40.0;
		double virtualScreenLeft = SystemParameters.VirtualScreenLeft;
		double virtualScreenTop = SystemParameters.VirtualScreenTop;
		double num3 = virtualScreenLeft + SystemParameters.VirtualScreenWidth;
		double num4 = virtualScreenTop + SystemParameters.VirtualScreenHeight;
		if (left < num3 - num && left > virtualScreenLeft - num && top < num4 - num2)
		{
			return top > virtualScreenTop - num2;
		}
		return false;
	}

	private static bool TryParseOpacity(string value, out double opacity)
	{
		if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity) && !double.TryParse(value, out opacity))
		{
			opacity = 1.0;
			return false;
		}
		if (opacity > 1.0)
		{
			opacity /= 100.0;
		}
		opacity = Math.Clamp(opacity, 0.2, 1.0);
		return true;
	}

	private int GetWindowOpacityPercent()
	{
		return (int)Math.Round(Math.Clamp(_windowOpacity, 0.2, 1.0) * 100.0);
	}

	private int GetHudOpacityPercent()
	{
		return (int)Math.Round(Math.Clamp(_hudOpacity, 0.2, 1.0) * 100.0);
	}

	private void ApplyShowInTaskbarPreference()
	{
		base.ShowInTaskbar = _showInTaskbar;
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle != IntPtr.Zero)
		{
			int windowLong = GetWindowLong(handle, -20);
			int num = (_showInTaskbar ? ((windowLong | 0x40000) & -129) : ((windowLong | 0x80) & -262145));
			if (num != windowLong)
			{
				SetWindowLong(handle, -20, num);
				SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 55u);
			}
			UpdateTrayIconVisibility();
			ApplyNativeTopmostState();
		}
	}

	private void ApplyNativeTopmostState()
	{
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle != IntPtr.Zero)
		{
			SetWindowPos(handle, base.Topmost ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, 51u);
		}
	}

	private void ApplyWindowOpacity()
	{
		if (_hiddenForAionInactive)
		{
			base.Opacity = 0.0;
			ApplyDetailWindowOpacity();
			return;
		}
		if (_isHudMode)
		{
			base.Opacity = GetHudContentOpacity();
			if (_hudClickThrough)
			{
				ApplyHudLockedSurfaceState();
			}
			else
			{
				ApplyMainSurfaceOpacity();
			}
		}
		else
		{
			base.Opacity = Math.Clamp(_windowOpacity, 0.2, 1.0);
			ApplyMainSurfaceOpacity();
		}
		ApplyDetailWindowOpacity();
		ApplyBuffTimerWindowOpacity();
	}

	private void RestoreDefaultFrameBorderBrushes()
	{
		rootBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
		mainContentBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
	}

	private double GetHudContentOpacity()
	{
		double num = Math.Clamp(_hudOpacity, 0.2, 1.0);
		return 1.0 - (1.0 - num) / 3.0;
	}

	private double GetHudSurfaceBrushOpacity()
	{
		return Math.Clamp(Math.Clamp(_hudOpacity, 0.2, 1.0) / GetHudContentOpacity(), 0.2, 1.0);
	}

	private double GetMainSurfaceBrushOpacity()
	{
		if (_autoHideBackground && !_isMainBackgroundHovered && !_isMainResizeBorderHovered && !_isDragging)
		{
			return 0.0;
		}
		if (!_isHudMode)
		{
			return 1.0;
		}
		return GetHudSurfaceBrushOpacity();
	}

	private System.Windows.Media.Brush CreateOpacityAdjustedBrush(string resourceKey, double opacity)
	{
		if (TryFindResource(resourceKey) is System.Windows.Media.Brush brush)
		{
			System.Windows.Media.Brush brush2 = brush.CloneCurrentValue();
			brush2.Opacity = opacity;
			return brush2;
		}
		return System.Windows.Media.Brushes.Transparent;
	}

	private void ApplyHudSurfaceOpacity()
	{
		ApplyMainSurfaceOpacity();
	}

	private void ApplyMainSurfaceOpacity(bool animate = false)
	{
		if (!_isHudMode || !_hudClickThrough)
		{
			double mainSurfaceBrushOpacity = GetMainSurfaceBrushOpacity();
			ApplySurfaceBrush(rootBorder, Border.BackgroundProperty, "ThemeBackgroundBrush", mainSurfaceBrushOpacity, animate);
			ApplySurfaceBrush(rootBorder, Border.BorderBrushProperty, "ThemeBorderBrush", mainSurfaceBrushOpacity, animate);
			ApplySurfaceBrush(mainContentBorder, Border.BackgroundProperty, "ThemeBackgroundBrush", mainSurfaceBrushOpacity, animate);
			ApplySurfaceBrush(mainContentBorder, Border.BorderBrushProperty, "ThemeBorderBrush", mainSurfaceBrushOpacity, animate);
		}
	}

	private void ApplySurfaceBrush(Border border, DependencyProperty property, string resourceKey, double targetOpacity, bool animate)
	{
		double num = ((animate && border.GetValue(property) is System.Windows.Media.Brush brush) ? brush.Opacity : targetOpacity);
		System.Windows.Media.Brush brush2 = CreateOpacityAdjustedBrush(resourceKey, num);
		border.SetValue(property, brush2);
		if (animate && !(Math.Abs(num - targetOpacity) < 0.001))
		{
			DoubleAnimation animation = new DoubleAnimation(targetOpacity, TimeSpan.FromMilliseconds(160L))
			{
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			brush2.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, animation);
		}
	}

	private void SetMainBackgroundHovered(bool hovered)
	{
		if (_isMainBackgroundHovered != hovered)
		{
			_isMainBackgroundHovered = hovered;
			ApplyMainSurfaceOpacity(_autoHideBackground);
		}
	}

	private void RefreshMainResizeBorderHover()
	{
		if (base.IsLoaded)
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				RefreshMainResizeBorderHover(handle);
			}
		}
	}

	private void RefreshMainResizeBorderHover(nint hwnd)
	{
		bool flag = _autoHideBackground && IsCursorInResizeBorder(hwnd);
		if (_isMainResizeBorderHovered != flag)
		{
			_isMainResizeBorderHovered = flag;
			ApplyMainSurfaceOpacity(_autoHideBackground);
		}
	}

	private bool IsCursorInResizeBorder(nint hwnd)
	{
		ResizeMode resizeMode = base.ResizeMode;
		bool flag = (uint)(resizeMode - 2) <= 1u;
		if (!flag || !GetCursorPos(out var lpPoint) || !GetWindowRect(hwnd, out var lpRect))
		{
			return false;
		}
		if (lpPoint.X < lpRect.left || lpPoint.X > lpRect.right || lpPoint.Y < lpRect.top || lpPoint.Y > lpRect.bottom)
		{
			return false;
		}
		Thickness thickness = WindowChrome.GetWindowChrome(this)?.ResizeBorderThickness ?? new Thickness(0.0);
		System.Windows.Media.Matrix? matrix = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice;
		double num = matrix?.M11 ?? 1.0;
		double num2 = matrix?.M22 ?? 1.0;
		double num3 = Math.Max(1.0, thickness.Left * num);
		double num4 = Math.Max(1.0, thickness.Right * num);
		double num5 = Math.Max(1.0, thickness.Top * num2);
		double num6 = Math.Max(1.0, thickness.Bottom * num2);
		if (!((double)(lpPoint.X - lpRect.left) <= num3) && !((double)(lpRect.right - lpPoint.X) <= num4) && !((double)(lpPoint.Y - lpRect.top) <= num5))
		{
			return (double)(lpRect.bottom - lpPoint.Y) <= num6;
		}
		return true;
	}

	private void ApplyDetailWindowOpacity()
	{
		if (_detailWindow != null)
		{
			_detailWindow.Opacity = (_hiddenForAionInactive ? 0.0 : 1.0);
		}
	}

	private void ApplyBuffTimerWindowOpacity()
	{
		if (_buffTimerWindow != null)
		{
			_buffTimerWindow.Opacity = (_hiddenForAionInactive ? 0.0 : Math.Clamp(_hudOpacity, 0.2, 1.0));
		}
	}

	private void ApplyBuffTimerLockedBackgroundState()
	{
		_buffTimerWindow?.SetLockedBackgroundHidden(_isHudMode && _hudClickThrough);
	}

	private void SetAppearance(AppearanceSelection appearance, bool applyResources)
	{
		_appearance = appearance;
		_skinProfile = AppearanceCatalog.GetSkinProfile(appearance.Skin);
		if (applyResources)
		{
			UiAppearanceManager.Apply(appearance);
		}
	}

	private void SaveConfig()
	{
		try
		{
			CaptureBuffTimerWindowPlacement();
			Rect rect = ((base.WindowState == WindowState.Normal) ? new Rect(base.Left, base.Top, base.Width, base.Height) : base.RestoreBounds);
			_isHudMode = true;
			_hudHotkey = "None";
			List<string> list = new List<string>
			{
				"# INGMeter 설정 파일",
				$"Topmost={(_isHudMode ? _preHudTopmost : base.Topmost)}",
				"PauseHotkey=" + _pauseHotkey,
				"ClearHotkey=" + _clearHotkey,
				"HudHotkey=" + _hudHotkey,
				"HideHotkey=" + _hideHotkey,
				"ClickThroughHotkey=" + _clickThroughHotkey,
				"MainViewHotkey=" + _mainViewHotkey,
				$"MaxDpsCards={_maxDpsCards}",
				$"MaxDpsCardsForce10Applied={_maxDpsCardsForce10Applied}",
				$"ShowActorId={_showActorId}",
				$"HudHeight={_hudHeight}",
				$"HudWidth={_hudWidth}",
				$"FullWidth={_fullWidth}",
				$"PartyWidth={_partyWidth}",
				"CompactWidth=" + _compactWidth.ToString("0", CultureInfo.InvariantCulture),
				$"NormalHeight={_normalHeight}",
				"WindowLeft=" + rect.Left.ToString("0", CultureInfo.InvariantCulture),
				"WindowTop=" + rect.Top.ToString("0", CultureInfo.InvariantCulture),
				$"UiMode={MeterUiMode.Hud}",
				$"AutoBossFilter={_autoBossFilter}",
				$"BossOnlyMeasurement={_bossOnlyMeasurement}",
				$"AutoResetOnMapChange={_autoResetOnMapChange}",
				$"AutoResetOnNewBoss={_autoResetOnNewBoss}",
				$"SaveEncounterLogs={_saveEncounterLogs}",
				$"HudClickThrough={_hudClickThrough}",
				$"BuffTimerEnabled={_buffTimerEnabled}",
				"BuffTimerLeft=" + (_buffTimerLeft.HasValue ? _buffTimerLeft.Value.ToString("0.##", CultureInfo.InvariantCulture) : ""),
				"BuffTimerTop=" + (_buffTimerTop.HasValue ? _buffTimerTop.Value.ToString("0.##", CultureInfo.InvariantCulture) : ""),
				"BuffTimerWidth=" + (_buffTimerWidth ?? 204.0).ToString("0.##", CultureInfo.InvariantCulture),
				"BuffTimerHeight=" + (_buffTimerHeight ?? 110.0).ToString("0.##", CultureInfo.InvariantCulture),
				"BuffTimerHiddenKeys=" + SerializeBuffTimerHiddenKeys(_hiddenBuffTimerKeys),
				$"ShowBossCard={_showBossCard}",
				$"ShowDpsCardCombatTime={_showDpsCardCombatTime}",
				$"AutoHideBackground={_autoHideBackground}",
				$"ShowOnlyWhenAionActive={_showOnlyWhenAionActive}",
				$"ShowInTaskbar={_showInTaskbar}",
				$"CloseButtonBehavior={_closeButtonBehavior}",
				"WindowOpacity=" + _windowOpacity.ToString("0.##", CultureInfo.InvariantCulture),
				"HudOpacity=" + _hudOpacity.ToString("0.##", CultureInfo.InvariantCulture),
				$"DisplayPreset={_displayPreset}",
				$"DpsCardNumberFormatMode={_dpsCardNumberFormatMode}",
				"UiScale=" + _uiScale.ToString("0.##", CultureInfo.InvariantCulture),
				"TextScale=" + _textScale.ToString("0.##", CultureInfo.InvariantCulture),
				$"FontWeightMode={_fontWeightMode}",
				"FontFamily=" + _fontFamilyName,
				$"TextShadowEnabled={_textShadowEnabled}",
				$"DamageShareMode={_damageShareMode}",
				$"DamageShareGraphMode={_damageShareGraphMode}",
				$"CaptureBackend={_captureBackend}",
				"DevKey=" + _devKey,
				$"LookupSkillDisplayEnabled={_lookupSkillDisplayEnabled}",
				"LookupSkillSelections=" + LookupSkillSelectionSerializer.Serialize(_lookupSkillSelections),
				"LookupSkillDisabledClasses=" + LookupSkillClassSetSerializer.Serialize(_lookupSkillDisabledClasses),
				"Theme=" + CurrentThemeName
			};
			list.AddRange(PrivacyConsentManager.ReadPersistedConsentLines());
			File.WriteAllLines(_configPath, list);
		}
		catch
		{
		}
	}

	private void BtnMaximize_Click(object sender, RoutedEventArgs e)
	{
		if (base.WindowState == WindowState.Maximized)
		{
			base.WindowState = WindowState.Normal;
		}
		else
		{
			base.WindowState = WindowState.Maximized;
		}
	}

	protected override void OnStateChanged(EventArgs e)
	{
		base.OnStateChanged(e);
		if (base.WindowState == WindowState.Maximized)
		{
			pathMaximizeIcon.Data = RestoreIconGeometry;
		}
		else
		{
			pathMaximizeIcon.Data = MaximizeIconGeometry;
		}
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void ApplyHudModeSelection()
	{
		_isHudMode = true;
		chkHudMode.IsChecked = _isHudMode;
		ApplyHudMode();
	}

	private void ApplyHudClickThroughMode()
	{
		UpdateHudClickThroughButtonUI();
		if (_isHudMode && _hudClickThrough)
		{
			if (_hudClickThroughTimer == null)
			{
				_hudClickThroughTimer = CreateHudClickThroughTimer();
			}
			if (!_hudClickThroughTimer.IsEnabled)
			{
				_hudClickThroughTimer.Start();
			}
			UpdateHudClickThroughState();
		}
		else
		{
			_hudClickThroughTimer?.Stop();
			SetWindowMouseTransparent(enabled: false);
		}
	}

	private DispatcherTimer CreateHudClickThroughTimer()
	{
		DispatcherTimer dispatcherTimer = new DispatcherTimer();
		dispatcherTimer.Interval = TimeSpan.FromMilliseconds(60L);
		dispatcherTimer.Tick += delegate
		{
			UpdateHudClickThroughState();
		};
		return dispatcherTimer;
	}

	private void UpdateHudClickThroughState()
	{
		bool windowMouseTransparent = _isHudMode && _hudClickThrough && !IsCursorOverHudControls();
		SetWindowMouseTransparent(windowMouseTransparent);
	}

	private bool IsCursorOverHudControls()
	{
		if (hudControls == null || hudControls.Visibility != Visibility.Visible || !GetCursorPos(out var lpPoint))
		{
			return false;
		}
		if (_hudClickThrough)
		{
			return IsScreenPointInside(btnClickThroughHud, lpPoint, 4.0);
		}
		if (!IsScreenPointInside(hudControls, lpPoint, 6.0))
		{
			return IsScreenPointInside(hudLeftControls, lpPoint, 6.0);
		}
		return true;
	}

	private static bool IsScreenPointInside(FrameworkElement element, POINT point, double padding)
	{
		if (!element.IsVisible || element.ActualWidth <= 0.0 || element.ActualHeight <= 0.0)
		{
			return false;
		}
		System.Windows.Point point2 = element.PointToScreen(new System.Windows.Point(0.0, 0.0));
		System.Windows.Point point3 = element.PointToScreen(new System.Windows.Point(element.ActualWidth, element.ActualHeight));
		if ((double)point.X >= point2.X - padding && (double)point.X <= point3.X + padding && (double)point.Y >= point2.Y - padding)
		{
			return (double)point.Y <= point3.Y + padding;
		}
		return false;
	}

	private void SetWindowMouseTransparent(bool enabled)
	{
		if (_isWindowMouseTransparent != enabled && SetWindowMouseTransparent(this, enabled))
		{
			_isWindowMouseTransparent = enabled;
		}
		SetBuffTimerWindowMouseTransparent(enabled);
	}

	private void SetBuffTimerWindowMouseTransparent(bool enabled)
	{
		if (_buffTimerWindow != null && _isBuffTimerWindowMouseTransparent != enabled && SetWindowMouseTransparent(_buffTimerWindow, enabled))
		{
			_isBuffTimerWindowMouseTransparent = enabled;
		}
	}

	private static bool SetWindowMouseTransparent(Window window, bool enabled)
	{
		nint handle = new WindowInteropHelper(window).Handle;
		if (handle == IntPtr.Zero)
		{
			return false;
		}
		int windowLong = GetWindowLong(handle, -20);
		int num = (enabled ? (windowLong | 0x20) : (windowLong & -33));
		if (num == windowLong)
		{
			return true;
		}
		SetWindowLong(handle, -20, num);
		SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 55u);
		return true;
	}

	private void BtnHudMode_Click(object sender, RoutedEventArgs e)
	{
		SetHudMode(enabled: true, save: true);
	}

	private void ToggleHudMode()
	{
		SetHudMode(enabled: true, save: true);
	}

	private void BtnExitHud_Click(object sender, RoutedEventArgs e)
	{
		SetHudMode(enabled: true, save: true);
	}

	private void SetHudMode(bool enabled, bool save)
	{
		_isHudMode = true;
		chkHudMode.IsChecked = true;
		ApplyHudMode();
		if (save)
		{
			SaveConfig();
		}
	}

	private void BtnExitAppHud_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void ApplyHudMode()
	{
		_isApplyingHudLayout = true;
		try
		{
			if (_isHudMode)
			{
				if (WindowState.Maximized == base.WindowState)
				{
					base.WindowState = WindowState.Normal;
				}
				_preHudWidth = base.Width;
				_preHudHeight = base.Height;
				_preHudTopmost = base.Topmost;
				base.Topmost = true;
				UpdateTopmostButtonUI();
				titleBar.Visibility = Visibility.Collapsed;
				titleBarSeparator.Visibility = Visibility.Collapsed;
				toolBar.Visibility = Visibility.Collapsed;
				sideMenu.Visibility = Visibility.Collapsed;
				borderParty.Visibility = Visibility.Collapsed;
				colDps.Width = new GridLength(1.0, GridUnitType.Star);
				colSplitter.Width = new GridLength(0.0);
				colDetail.Width = new GridLength(0.0);
				colSplitterToolbar.Width = new GridLength(0.0);
				colDetailToolbar.Width = new GridLength(0.0);
				rootBorder.SetResourceReference(Border.BackgroundProperty, "ThemeBackgroundBrush");
				rootBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
				rootBorder.BorderThickness = (IsBloomTheme ? new Thickness(1.0) : new Thickness(0.0));
				mainGrid.Margin = (IsBloomTheme ? new Thickness(2.0) : new Thickness(0.0));
				mainContentBorder.SetResourceReference(Border.BackgroundProperty, "ThemeBackgroundBrush");
				mainContentBorder.BorderThickness = (IsBloomTheme ? new Thickness(0.0) : new Thickness(1.0));
				mainContentBorder.CornerRadius = new CornerRadius(IsBloomTheme ? 10 : 6);
				mainContentBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
				filterPanel.Visibility = Visibility.Collapsed;
				hudLeftControls.Visibility = Visibility.Visible;
				hudControls.Visibility = Visibility.Visible;
				borderTopTarget.Margin = AlignContentHorizontalInset(HudTopTargetMargin);
				borderTopTarget.Opacity = 1.0;
				borderTopTarget.Padding = HudTopTargetPadding;
				txtTopTargetType.Visibility = Visibility.Collapsed;
				bdTargetIcon.Visibility = Visibility.Collapsed;
				colTargetIcon.Width = new GridLength(0.0);
				ApplyHudChromeLayout();
				base.MinWidth = 315.0;
				base.Width = Math.Max(_hudWidth, 315.0);
				base.MinHeight = 160.0;
				base.Height = _hudHeight;
				base.ResizeMode = ResizeMode.CanResize;
				foreach (DpsCardViewModel dpsCard in DpsCards)
				{
					dpsCard.IsHudMode = true;
				}
			}
			else
			{
				titleBar.Visibility = Visibility.Collapsed;
				titleBarSeparator.Visibility = Visibility.Collapsed;
				toolBar.Visibility = Visibility.Visible;
				sideMenu.Visibility = Visibility.Collapsed;
				ApplyExpandedLayoutColumns();
				rootBorder.SetResourceReference(Border.BackgroundProperty, "ThemeBackgroundBrush");
				rootBorder.BorderThickness = new Thickness(1.0);
				RestoreDefaultFrameBorderBrushes();
				mainGrid.Margin = NormalMainGridMargin;
				mainContentBorder.SetResourceReference(Border.BackgroundProperty, "ThemeBackgroundBrush");
				mainContentBorder.BorderThickness = new Thickness(1.0);
				mainContentBorder.CornerRadius = new CornerRadius(0.0, 0.0, 6.0, 6.0);
				ApplyHudChromeLayout();
				filterPanel.Visibility = ((_mainContentView != MainContentView.Lookup) ? Visibility.Collapsed : Visibility.Visible);
				hudLeftControls.Visibility = Visibility.Collapsed;
				borderTopTarget.Margin = AlignContentHorizontalInset(NormalTopTargetMargin);
				borderTopTarget.Padding = NormalTopTargetPadding;
				borderTopTarget.Opacity = 1.0;
				txtTopTargetType.Visibility = Visibility.Visible;
				bdTargetIcon.Visibility = Visibility.Visible;
				colTargetIcon.Width = GridLength.Auto;
				foreach (DpsCardViewModel dpsCard2 in DpsCards)
				{
					dpsCard2.IsHudMode = false;
				}
				hudControls.Visibility = Visibility.Collapsed;
				base.ResizeMode = ResizeMode.CanResizeWithGrip;
				base.MinHeight = 240.0;
				base.Width = _preHudWidth;
				base.Height = _normalHeight;
				base.Topmost = _preHudTopmost;
				UpdateTopmostButtonUI();
				if (_partyOpen)
				{
					borderParty.Visibility = Visibility.Visible;
					ApplyExpandedLayoutColumns();
					ApplyExpandedWindowBounds(_partyWidth);
				}
				else
				{
					ApplyCompactLayoutColumns();
					ApplyCompactWindowBounds();
					base.Dispatcher.BeginInvoke(new Action(CaptureCompactDpsWidth), DispatcherPriority.Loaded);
				}
			}
		}
		finally
		{
			_isApplyingHudLayout = false;
		}
		ApplyNativeTopmostState();
		ApplyWindowOpacity();
		ApplyHeaderScale();
		ApplyDpsListSpacing();
		ApplyMeterScale(force: true);
		ApplyHudClickThroughMode();
		if (_isHudMode)
		{
			RefreshAutoMainContentView();
		}
		else
		{
			ApplyMainContentView();
		}
		InvalidateMeasure();
		mainGrid.InvalidateMeasure();
		mainContentBorder.InvalidateMeasure();
		lstDps.Items.Refresh();
		lstLookup.Items.Refresh();
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			UpdateLayout();
			CombatSnapshot snap = (_useDummyData ? CreateDummySnapshot() : GetSnapshotForCurrentFilter());
			RenderTiles(snap);
		}, DispatcherPriority.Loaded);
	}

	private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		if (_isApplyingHudLayout)
		{
			return;
		}
		if (_isHudMode)
		{
			_hudHeight = base.Height;
			_hudWidth = Math.Max(315.0, base.Width);
		}
		else
		{
			if (_partyOpen)
			{
				_partyWidth = base.Width;
			}
			else
			{
				_compactWidth = Math.Clamp(base.Width, 315.0, 1400.0);
			}
			_normalHeight = base.Height;
		}
		CaptureCompactDpsWidth();
		ApplyMeterScale();
		UpdateBalloonPlacement();
		RepositionLocalEncounterHistoryPopup();
		SaveConfig();
	}

	private void ApplyMeterScale(bool force = false)
	{
		double num = ((mainContentBorder.ActualWidth > 0.0) ? mainContentBorder.ActualWidth : base.Width);
		double num2 = (_isHudMode ? 315.0 : 360.0);
		double num3 = Math.Clamp(Math.Clamp(max: _isHudMode ? 1.35 : 1.65, min: _isHudMode ? 0.82 : 0.8, value: num / num2) * (_uiScale / 0.96), 0.75, 1.7);
		num3 = Math.Round(num3 * 100.0, MidpointRounding.AwayFromZero) / 100.0;
		double num4 = MeterScaleOptions.NormalizeTextScale(_textScale);
		int windowFontSizeDelta = GetWindowFontSizeDelta(num, num2);
		double num5 = 1.0;
		if (!force && Math.Abs(_meterLayoutScale - num3) < 0.01 && Math.Abs(_meterTextScale - num4) < 0.01 && _meterFontSizeDelta == windowFontSizeDelta && Math.Abs(_meterUiScale - num5) < 0.01)
		{
			return;
		}
		_meterLayoutScale = num3;
		_meterTextScale = num4;
		_meterFontSizeDelta = windowFontSizeDelta;
		_meterUiScale = num5;
		ApplyTopTargetVisualScale();
		foreach (DpsCardViewModel dpsCard in DpsCards)
		{
			dpsCard.UiScale = num5;
			dpsCard.Theme = CurrentThemeName;
			dpsCard.FontWeightMode = _fontWeightMode;
			dpsCard.SetVisualScale(num3, num4, windowFontSizeDelta);
		}
		foreach (PartyMemberItem partyMember in PartyMembers)
		{
			partyMember.UiScale = num5;
			partyMember.FontWeightMode = _fontWeightMode;
			partyMember.SetVisualScale(num3, num4, windowFontSizeDelta);
		}
	}

	private static int GetWindowFontSizeDelta(double width, double baseWidth)
	{
		return Math.Clamp((int)Math.Round((Math.Clamp(width, baseWidth * 0.75, baseWidth * 1.9) - baseWidth) / 40.0, MidpointRounding.AwayFromZero), -3, 8);
	}

	private void ApplyHeaderScale()
	{
		if (toolBar == null)
		{
			return;
		}
		double uiSizeScale = GetUiSizeScale();
		int num;
		double num2;
		if (!_isHudMode)
		{
			num = ((_displayPreset == MeterDisplayPreset.Minimal) ? 1 : 0);
			if (num != 0)
			{
				num2 = 32.0;
				goto IL_003b;
			}
		}
		else
		{
			num = 0;
		}
		num2 = 40.0;
		goto IL_003b;
		IL_003b:
		double num3 = num2;
		double num4 = ((num != 0) ? 28.0 : 32.0);
		double width = Math.Max(24.0, num4 - 4.0);
		double num5 = ((num != 0) ? 6.0 : 7.0);
		double contentHorizontalInset = ContentHorizontalInset;
		double num6 = num3 * uiSizeScale;
		double num7 = num4 * uiSizeScale;
		double num8 = Math.Floor((num6 - num7) / 2.0);
		double bottom = num6 - num7 - num8;
		toolBar.Height = num6;
		SetElementScale(headerLeftControls, uiSizeScale);
		SetElementScale(headerActionControls, uiSizeScale);
		SetElementScale(headerWindowControls, uiSizeScale);
		StackPanel stackPanel = headerLeftControls;
		StackPanel stackPanel2 = headerActionControls;
		double num9 = (headerWindowControls.Height = num4);
		double height = (stackPanel2.Height = num9);
		stackPanel.Height = height;
		StackPanel stackPanel3 = headerLeftControls;
		StackPanel stackPanel4 = headerActionControls;
		VerticalAlignment verticalAlignment = (headerWindowControls.VerticalAlignment = VerticalAlignment.Top);
		VerticalAlignment verticalAlignment3 = (stackPanel4.VerticalAlignment = verticalAlignment);
		stackPanel3.VerticalAlignment = verticalAlignment3;
		headerLeftControls.Margin = new Thickness(contentHorizontalInset, num8, 0.0, bottom);
		headerActionControls.Margin = new Thickness(0.0, num8, contentHorizontalInset, bottom);
		headerWindowControls.Margin = new Thickness(0.0, num8, contentHorizontalInset, bottom);
		StackPanel stackPanel5 = statusHeaderHitArea;
		height = (statusHeaderHitArea.Height = num4);
		stackPanel5.Width = height;
		Border border = statusHeaderIconHost;
		height = (statusHeaderIconHost.Height = num4);
		border.Width = height;
		System.Windows.Controls.Image image = imgStatusHeaderIcon;
		height = (imgStatusHeaderIcon.Height = num4 - 8.0);
		image.Width = height;
		bdMainViewModeHost.Height = num4;
		chkAutoMainView.Width = width;
		chkAutoMainView.Height = num4;
		btnMainViewSwap.Height = num4;
		btnMainViewSwap.FontSize = 11.0;
		btnMainViewSwap.MinWidth = 42.0;
		btnMainViewSwap.Padding = new Thickness(8.0, 0.0, 8.0, 0.0);
		mainViewModeDivider.Margin = new Thickness(0.0, num5, 0.0, num5);
		System.Windows.Controls.Button button = btnPause;
		height = (btnPause.Height = num4);
		button.Width = height;
		System.Windows.Controls.Button button2 = btnPrimaryAction;
		height = (btnPrimaryAction.Height = num4);
		button2.Width = height;
		ToggleButton toggleButton = chkHudMode;
		height = (chkHudMode.Height = num4);
		toggleButton.Width = height;
		btnStatusSettings.Width = num4;
		btnStatusSettings.Height = num4;
		System.Windows.Controls.Button button3 = btnTopmost;
		height = (btnTopmost.Height = num4);
		button3.Width = height;
		System.Windows.Controls.Button button4 = btnLocalEncounterHistory;
		height = (btnLocalEncounterHistory.Height = num4);
		button4.Width = height;
		System.Windows.Controls.Button button5 = btnClose;
		height = (btnClose.Height = num4);
		button5.Width = height;
		ApplyWindowChromeCaptionHeight(_isHudMode ? 0.0 : num6);
		UpdatePrimaryActionButtonUI();
		ApplyHudChromeLayout();
	}

	private void ApplyHudChromeLayout()
	{
		if (hudControls != null && hudLeftControls != null)
		{
			double uiSizeScale = GetUiSizeScale();
			bool isBloomTheme = IsBloomTheme;
			double num = (isBloomTheme ? 29.5 : 30.0);
			double num2 = (isBloomTheme ? 29.5 : 30.0);
			double num3 = 4.0;
			double contentHorizontalInset = ContentHorizontalInset;
			SetElementScale(hudLeftControls, uiSizeScale);
			SetElementScale(hudControls, uiSizeScale);
			hudLeftControls.Margin = new Thickness(contentHorizontalInset, 5.0, 0.0, 0.0);
			hudControls.Margin = new Thickness(0.0, 5.0, Math.Max(0.0, contentHorizontalInset - num3), 0.0);
			Grid grid = hudBrandIcon;
			double width = (hudBrandIcon.Height = num2);
			grid.Width = width;
			hudBrandIcon.Margin = new Thickness(0.0, 0.0, isBloomTheme ? 6 : 5, 0.0);
			System.Windows.Controls.Button button = btnUpdateBadgeHud;
			width = (btnUpdateBadgeHud.Height = 20.0);
			button.Width = width;
			btnUpdateBadgeHud.Margin = new Thickness(3.0, 0.0, 5.0, 0.0);
			bdHudMainViewModeHost.Height = num;
			chkAutoMainViewHud.Width = Math.Max(24.0, num - 4.0);
			chkAutoMainViewHud.Height = num;
			btnMainViewSwapHud.Height = num;
			btnMainViewSwapHud.MinWidth = 42.0;
			btnMainViewSwapHud.Padding = new Thickness(8.0, 0.0, 8.0, 0.0);
			btnMainViewSwapHud.FontSize = 11.0;
			SetHudButtonLayout(btnResetHud, num, num3, isBloomTheme);
			SetHudButtonLayout(btnExitHud, num, num3, isBloomTheme);
			SetHudButtonLayout(btnSettingsHud, num, num3, isBloomTheme);
			SetHudButtonLayout(btnLocalEncounterHistoryHud, num, num3, isBloomTheme);
			SetHudButtonLayout(btnClickThroughHud, num, num3, isBloomTheme);
			SetHudButtonLayout(btnExitAppHud, num, num3, isBloomTheme);
		}
	}

	private static void SetHudButtonLayout(System.Windows.Controls.Button button, double size, double rightMargin, bool bloom)
	{
		button.Width = size;
		button.Height = size;
		button.Margin = new Thickness(0.0, 0.0, rightMargin, 0.0);
		button.Opacity = 1.0;
	}

	private static void SetElementScale(FrameworkElement element, double scale)
	{
		scale = Math.Clamp(scale, 0.75, 1.2);
		if (Math.Abs(scale - 1.0) < 0.001)
		{
			element.LayoutTransform = null;
		}
		else if (!(element.LayoutTransform is ScaleTransform scaleTransform) || !(Math.Abs(scaleTransform.ScaleX - scale) < 0.001) || !(Math.Abs(scaleTransform.ScaleY - scale) < 0.001))
		{
			element.LayoutTransform = new ScaleTransform(scale, scale);
		}
	}

	private void ApplyWindowChromeCaptionHeight(double captionHeight)
	{
		WindowChrome windowChrome = WindowChrome.GetWindowChrome(this);
		if (windowChrome != null)
		{
			captionHeight = Math.Max(0.0, captionHeight);
			if (Math.Abs(windowChrome.CaptionHeight - captionHeight) > 0.1)
			{
				windowChrome.CaptionHeight = captionHeight;
			}
		}
	}

	private void ApplyDpsListSpacing()
	{
		Thickness dpsListMargin = DpsListMargin;
		Thickness padding = ((!IsBloomTheme) ? ((_displayPreset == MeterDisplayPreset.Minimal) ? MinimalDpsListPadding : NormalDpsListPadding) : ((_displayPreset == MeterDisplayPreset.Minimal) ? new Thickness(0.0, 2.0, 0.0, 2.0) : new Thickness(0.0, 3.0, 0.0, 2.0)));
		if (lstDps != null)
		{
			lstDps.Margin = dpsListMargin;
			lstDps.Padding = padding;
		}
		if (lstLookup != null)
		{
			lstLookup.Margin = dpsListMargin;
			lstLookup.Padding = padding;
		}
	}

	private void ApplyTopTargetVisualScale()
	{
		if (topTargetScale != null)
		{
			topTargetScale.ScaleX = 1.0;
			topTargetScale.ScaleY = 1.0;
			CombatSnapshot combatSnapshot = (_useDummyData ? CreateDummySnapshot() : GetSnapshotForCurrentFilter());
			if (combatSnapshot != null && ShouldShowTopTargetCard(combatSnapshot))
			{
				UpdateTopTargetCard(combatSnapshot);
			}
			else
			{
				ApplyTopTargetLayout(_displayPreset == MeterDisplayPreset.Minimal);
			}
		}
	}

	private double GetUiSizeScale()
	{
		return _uiScale;
	}

	private void ApplyFontFamily()
	{
		_fontFamilyName = MeterFontFamilies.NormalizeForStorage(_fontFamilyName);
		System.Windows.Media.FontFamily fontFamily = (base.FontFamily = MeterFontFamilies.CreateFontFamily(_fontFamilyName));
		if (_detailWindow != null)
		{
			_detailWindow.FontFamily = fontFamily;
		}
	}

	private void ApplyFontWeightMode()
	{
		base.FontWeight = MeterFontWeights.Text(_fontWeightMode);
		foreach (DpsCardViewModel dpsCard in DpsCards)
		{
			dpsCard.FontWeightMode = _fontWeightMode;
		}
		foreach (PartyMemberItem partyMember in PartyMembers)
		{
			partyMember.FontWeightMode = _fontWeightMode;
		}
	}

	private void ApplyTextShadowPreference()
	{
		Effect effect = ((_textShadowEnabled && !IsSoftDecorativeTheme) ? (TryFindResource("MeterTextShadowEffect") as Effect) : null);
		txtTopTargetName.Effect = effect;
		txtTopTargetDamage.Effect = effect;
		foreach (DpsCardViewModel dpsCard in DpsCards)
		{
			dpsCard.TextShadowEnabled = _textShadowEnabled;
		}
	}

	private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (_isHudMode && e.ButtonState == MouseButtonState.Pressed && IsHudHeaderDragHit(e))
		{
			DragMove();
			SaveConfig();
		}
	}

	private void WindowDragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ButtonState == MouseButtonState.Pressed && sender is FrameworkElement windowDragHandleSource)
		{
			_windowDragHandleSource = windowDragHandleSource;
			_windowDragHandleStart = e.GetPosition(this);
		}
	}

	private void WindowDragHandle_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
	{
		if (_windowDragHandleSource == null || sender != _windowDragHandleSource || e.LeftButton != MouseButtonState.Pressed)
		{
			return;
		}
		System.Windows.Point position = e.GetPosition(this);
		if (Math.Abs(position.X - _windowDragHandleStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _windowDragHandleStart.Y) < SystemParameters.MinimumVerticalDragDistance)
		{
			return;
		}
		Mouse.Capture(null);
		_windowDragHandleSource = null;
		SetWindowMouseTransparent(enabled: false);
		try
		{
			DragMove();
			SaveConfig();
		}
		catch (InvalidOperationException)
		{
		}
		finally
		{
			if (_isHudMode && _hudClickThrough)
			{
				UpdateHudClickThroughState();
			}
		}
		e.Handled = true;
	}

	private void WindowDragHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
	{
		if (_windowDragHandleSource != null && sender == _windowDragHandleSource)
		{
			_windowDragHandleSource = null;
		}
	}

	private bool IsHudHeaderDragHit(MouseButtonEventArgs e)
	{
		if (IsEventInside(e.OriginalSource as DependencyObject, lstDps) || IsEventInside(e.OriginalSource as DependencyObject, lstLookup) || IsEventInside(e.OriginalSource as DependencyObject, borderTopTarget))
		{
			return false;
		}
		double num = Math.Max(GetElementBottomInWindow(hudLeftControls), GetElementBottomInWindow(hudControls));
		if (num <= 0.0)
		{
			return false;
		}
		return e.GetPosition(this).Y <= num;
	}

	private double GetElementBottomInWindow(FrameworkElement? element)
	{
		if (element == null || !element.IsVisible || element.ActualHeight <= 0.0)
		{
			return 0.0;
		}
		return element.TranslatePoint(new System.Windows.Point(0.0, element.ActualHeight), this).Y;
	}

	private static bool IsEventInside(DependencyObject? source, DependencyObject? target)
	{
		if (source == null || target == null)
		{
			return false;
		}
		for (DependencyObject dependencyObject = source; dependencyObject != null; dependencyObject = GetParentObject(dependencyObject))
		{
			if (dependencyObject == target)
			{
				return true;
			}
		}
		return false;
	}

	private static DependencyObject? GetParentObject(DependencyObject current)
	{
		if ((current is Visual || current is Visual3D) ? true : false)
		{
			return VisualTreeHelper.GetParent(current);
		}
		return LogicalTreeHelper.GetParent(current);
	}

	private static T? FindVisualDescendant<T>(DependencyObject? root) where T : DependencyObject
	{
		if (root == null)
		{
			return null;
		}
		int childrenCount = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < childrenCount; i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is T result)
			{
				return result;
			}
			T val = FindVisualDescendant<T>(child);
			if (val != null)
			{
				return val;
			}
		}
		return null;
	}

	private void BtnTopmost_Click(object sender, RoutedEventArgs e)
	{
		SetTopmostState(!base.Topmost);
	}

	private void SetTopmostState(bool enabled)
	{
		base.Topmost = enabled;
		if (_localEncounterHistoryWindow != null)
		{
			_localEncounterHistoryWindow.Topmost = enabled;
		}
		if (_buffTimerWindow != null)
		{
			_buffTimerWindow.Topmost = enabled;
		}
		if (_isHudMode)
		{
			_preHudTopmost = base.Topmost;
		}
		ApplyNativeTopmostState();
		UpdateTopmostButtonUI();
		SaveConfig();
	}

	private void BtnHudClickThrough_Click(object sender, RoutedEventArgs e)
	{
		_hudClickThrough = !_hudClickThrough;
		ApplyHudClickThroughMode();
		SaveConfig();
	}

	private void UpdateStatusSettingsButtonUI(System.Windows.Media.Color color, string toolTip)
	{
		System.Windows.Media.Color? lastStatusColor = _lastStatusColor;
		if (!lastStatusColor.HasValue || !(lastStatusColor.GetValueOrDefault() == color) || !string.Equals(_lastStatusTooltip, toolTip, StringComparison.Ordinal))
		{
			_lastStatusColor = color;
			_lastStatusTooltip = toolTip;
			SolidColorBrush fill = new SolidColorBrush(color);
			elStatus.Fill = fill;
			elStatus.ToolTip = toolTip;
			elStatusHeader.Fill = fill;
			elStatusHeader.ToolTip = toolTip;
			statusHeaderHitArea.ToolTip = toolTip;
			UpdateTopmostButtonUI();
		}
	}

	private void UpdateTopmostButtonUI()
	{
		SolidColorBrush solidColorBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(byte.MaxValue, 107, 107));
		if (btnTopmost != null)
		{
			btnTopmost.Foreground = (base.Topmost ? solidColorBrush : System.Windows.Media.Brushes.White);
		}
		string toolTip = (base.Topmost ? "설정 / 항상 위 켜짐" : "설정 / 메뉴");
		if (btnStatusSettings != null)
		{
			btnStatusSettings.ToolTip = toolTip;
		}
		if (btnSettingsHud != null)
		{
			btnSettingsHud.ToolTip = toolTip;
		}
	}

	private void UpdateHudClickThroughButtonUI()
	{
		System.Windows.Media.Brush brush = (_hudClickThrough ? FindBrush("DpsValueBrush") : FindBrush("ToolbarActionIconBrush"));
		UpdateHudLockedControlState();
		btnClickThroughHud.Foreground = brush;
		btnClickThroughHud.IsHitTestVisible = true;
		btnClickThroughHud.Opacity = (_hudClickThrough ? 0.9 : 1.0);
		pathHudClickThroughIcon.Stroke = brush;
		pathHudClickThroughIcon.Opacity = (_hudClickThrough ? 1.0 : 0.9);
		btnClickThroughHud.ToolTip = (_hudClickThrough ? null : "클릭 잠금");
	}

	private void UpdateHudLockedControlState()
	{
		bool flag = _isHudMode && _hudClickThrough;
		hudLeftControls.Visibility = ((!_isHudMode || flag) ? Visibility.Collapsed : Visibility.Visible);
		hudBrandIcon.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		bdHudMainViewModeHost.Visibility = ((!_isHudMode || flag) ? Visibility.Collapsed : Visibility.Visible);
		btnResetHud.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		btnExitHud.Visibility = Visibility.Collapsed;
		btnSettingsHud.Visibility = (flag ? Visibility.Collapsed : Visibility.Visible);
		btnLocalEncounterHistoryHud.Visibility = ((!_isHudMode || flag) ? Visibility.Collapsed : Visibility.Visible);
		btnExitAppHud.Visibility = ((!_isHudMode) ? Visibility.Collapsed : Visibility.Visible);
		SetHudAuxButtonState(btnResetHud, !flag);
		SetHudAuxButtonState(btnExitHud, active: false);
		SetHudAuxButtonState(btnSettingsHud, !flag);
		SetHudAuxButtonState(btnLocalEncounterHistoryHud, _isHudMode && !flag);
		SetHudAuxButtonState(btnExitAppHud, _isHudMode && !flag);
		ApplyHudLockedSurfaceState();
	}

	private void ApplyHudLockedSurfaceState()
	{
		bool flag = _isHudMode && _hudClickThrough;
		ApplyBuffTimerLockedBackgroundState();
		if (!_isHudMode)
		{
			return;
		}
		if (flag)
		{
			rootBorder.Background = System.Windows.Media.Brushes.Transparent;
			rootBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
			rootBorder.BorderThickness = new Thickness(0.0);
			mainContentBorder.Background = System.Windows.Media.Brushes.Transparent;
			mainContentBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
			mainContentBorder.BorderThickness = new Thickness(0.0);
			bloomWindowFrame.Visibility = Visibility.Collapsed;
			crayonBackdrop.Visibility = Visibility.Collapsed;
			crayonWindowFrame.Visibility = Visibility.Collapsed;
			lstDps.Visibility = ((_mainContentView != MainContentView.Dps) ? Visibility.Collapsed : Visibility.Visible);
			lstLookup.Visibility = ((_mainContentView != MainContentView.Lookup) ? Visibility.Collapsed : Visibility.Visible);
			filterPanel.Visibility = Visibility.Collapsed;
			if (bdLookupDungeonInfo != null)
			{
				bdLookupDungeonInfo.Visibility = Visibility.Collapsed;
			}
		}
		else
		{
			ApplyHudSurfaceOpacity();
			rootBorder.BorderThickness = (IsBloomTheme ? new Thickness(1.0) : new Thickness(0.0));
			mainContentBorder.BorderThickness = (IsBloomTheme ? new Thickness(0.0) : new Thickness(1.0));
			bloomWindowFrame.Visibility = Visibility.Visible;
			crayonBackdrop.Visibility = Visibility.Visible;
			crayonWindowFrame.Visibility = Visibility.Visible;
			lstDps.Visibility = ((_mainContentView != MainContentView.Dps) ? Visibility.Collapsed : Visibility.Visible);
			lstLookup.Visibility = ((_mainContentView != MainContentView.Lookup) ? Visibility.Collapsed : Visibility.Visible);
			if (bdLookupDungeonInfo != null)
			{
				bdLookupDungeonInfo.Visibility = ((_mainContentView != MainContentView.Lookup) ? Visibility.Collapsed : Visibility.Visible);
			}
		}
	}

	private static void SetHudAuxButtonState(System.Windows.Controls.Button button, bool active)
	{
		button.IsHitTestVisible = active;
		button.Focusable = active;
		button.Opacity = (active ? 1.0 : 0.18);
	}

	private void BtnPrimaryAction_Click(object sender, RoutedEventArgs e)
	{
		if (_isEncounterReplayActive)
		{
			StopEncounterReplayFromUi();
		}
		else if (_isLogViewMode && _isPaused)
		{
			BtnPause_Click(sender, e);
		}
		else
		{
			BtnClear_Click(sender, e);
		}
	}

	private void OpenDetailForActor(int actorId)
	{
		DpsCardViewModel dpsCardViewModel = DpsCards.FirstOrDefault((DpsCardViewModel x) => x.ActorId == actorId);
		if (dpsCardViewModel != null && lstDps.SelectedItem != dpsCardViewModel)
		{
			lstDps.SelectedItem = dpsCardViewModel;
		}
		if (dpsCardViewModel != null)
		{
			SetSelectedDetailCard(dpsCardViewModel);
		}
		else
		{
			_selectedActorId = actorId;
		}
		EnsureCombatDetailWindow();
		CombatDetailWindow detailWindow = _detailWindow;
		UpdateDetailWindowTitle(dpsCardViewModel);
		detailWindow.SetCombatTime(_combatTimeText);
		if (!detailWindow.IsVisible)
		{
			PositionCombatDetailWindow(detailWindow);
		}
		detailWindow.Show();
		if (detailWindow.WindowState == WindowState.Minimized)
		{
			detailWindow.WindowState = WindowState.Normal;
		}
		detailWindow.Activate();
		RenderSelectedActorDetail();
	}

	private void SetSelectedDetailCard(DpsCardViewModel card)
	{
		_selectedActorId = card.ActorId;
		_selectedDetailCharacterKey = GetDpsCardCharacterKey(card);
		_selectedDetailTitle = card.Name;
	}

	private void ClearSelectedDetailTarget()
	{
		_selectedActorId = null;
		_selectedDetailCharacterKey = null;
		_selectedDetailTitle = null;
		_lastDoubleClickedActorId = null;
		_lastDoubleClickedCharacterKey = null;
	}

	private static string? GetDpsCardCharacterKey(DpsCardViewModel? card)
	{
		if (card == null || string.IsNullOrWhiteSpace(card.CharacterName) || string.IsNullOrWhiteSpace(card.ServerName) || string.Equals(card.ServerName, "Unknown", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		return GetCharacterKey(card.CharacterName, card.ServerName);
	}

	private DpsCardViewModel? FindDpsCardByCharacterKey(string characterKey)
	{
		return DpsCards.FirstOrDefault((DpsCardViewModel card) => string.Equals(GetDpsCardCharacterKey(card), characterKey, StringComparison.Ordinal));
	}

	private int? ResolveSelectedDetailActorId()
	{
		if (!string.IsNullOrWhiteSpace(_selectedDetailCharacterKey))
		{
			DpsCardViewModel dpsCardViewModel = FindDpsCardByCharacterKey(_selectedDetailCharacterKey);
			if (dpsCardViewModel != null)
			{
				if (_selectedActorId != dpsCardViewModel.ActorId)
				{
					_selectedActorId = dpsCardViewModel.ActorId;
				}
				if (lstDps.SelectedItem != dpsCardViewModel)
				{
					lstDps.SelectedItem = dpsCardViewModel;
				}
				UpdateDetailWindowTitle(dpsCardViewModel);
				return dpsCardViewModel.ActorId;
			}
		}
		return _selectedActorId;
	}

	private void RenderDetailForCurrentEncounter()
	{
		DpsCardViewModel dpsCardViewModel = ResolveDetailCardForCurrentEncounter();
		if (dpsCardViewModel == null)
		{
			ClearSelectedDetailTarget();
			ClearCombatDetailRows();
		}
		else if (lstDps.SelectedItem != dpsCardViewModel)
		{
			lstDps.SelectedItem = dpsCardViewModel;
		}
		else
		{
			SetSelectedDetailCard(dpsCardViewModel);
			UpdateDetailWindowTitle(dpsCardViewModel);
			RenderActorDetail(dpsCardViewModel.ActorId);
		}
	}

	private DpsCardViewModel? ResolveDetailCardForCurrentEncounter()
	{
		if (!string.IsNullOrWhiteSpace(_selectedDetailCharacterKey))
		{
			DpsCardViewModel dpsCardViewModel = FindDpsCardByCharacterKey(_selectedDetailCharacterKey);
			if (dpsCardViewModel != null)
			{
				return dpsCardViewModel;
			}
		}
		else if (_selectedActorId.HasValue)
		{
			DpsCardViewModel dpsCardViewModel2 = DpsCards.FirstOrDefault((DpsCardViewModel x) => x.ActorId == _selectedActorId.Value);
			if (dpsCardViewModel2 != null)
			{
				return dpsCardViewModel2;
			}
		}
		return DpsCards.FirstOrDefault((DpsCardViewModel x) => x.TotalDamage > 0 || x.TotalHealing > 0) ?? DpsCards.FirstOrDefault();
	}

	private void UpdateDetailWindowTitle(DpsCardViewModel? card = null)
	{
		if (_detailWindow != null)
		{
			string actorTitle = card?.Name ?? _selectedDetailTitle ?? (_selectedActorId.HasValue ? $"Actor {_selectedActorId.Value}" : "");
			_detailWindow.SetActorTitle(actorTitle);
		}
	}

	private void ClearCombatDetailRows()
	{
		_lastDetailRdpsRefreshTick = 0L;
		_lastAutoDetailRenderSignature = "";
		_detailWindow?.SetSummary(CreateEmptyCombatDetailSummary());
		_detailWindow?.SetSkillRows(Array.Empty<SkillRow>());
		_detailWindow?.SetHealingRows(Array.Empty<SkillRow>());
		_detailWindow?.SetLogRows(Array.Empty<LogRow>());
		_detailWindow?.SetDpsGraphRows(Array.Empty<DpsGraphRow>());
		_detailWindow?.SetBuffRows(Array.Empty<BuffUptimeRow>());
		_detailWindow?.SetRdpsRows(Array.Empty<RdpsBuffRow>());
	}

	private void EnsureCombatDetailWindow()
	{
		if (_detailWindow == null)
		{
			_detailWindow = new CombatDetailWindow
			{
				Owner = this,
				FontFamily = MeterFontFamilies.CreateFontFamily(_fontFamilyName),
				Opacity = (_hiddenForAionInactive ? 0.0 : 1.0)
			};
			_detailWindow.RefreshRequested += RenderSelectedActorDetail;
			_detailWindow.Closed += delegate
			{
				_detailWindow = null;
				_lastDetailRdpsRefreshTick = 0L;
				_lastAutoDetailRenderSignature = "";
				_lastDoubleClickedActorId = null;
				_lastDoubleClickedCharacterKey = null;
			};
		}
	}

	private void PositionCombatDetailWindow(CombatDetailWindow window)
	{
		Rect currentMonitorWorkAreaDip = GetCurrentMonitorWorkAreaDip();
		double num = 12.0;
		double left = base.Left;
		double top = base.Top;
		double num2 = ((base.ActualWidth > 0.0) ? base.ActualWidth : base.Width);
		double num3 = ((base.ActualHeight > 0.0) ? base.ActualHeight : base.Height);
		double num4 = ((window.Width > 0.0) ? window.Width : 980.0);
		double num5 = ((window.Height > 0.0) ? window.Height : 650.0);
		double max = Math.Max(currentMonitorWorkAreaDip.Left, currentMonitorWorkAreaDip.Right - num4);
		double num6 = Math.Max(currentMonitorWorkAreaDip.Top, currentMonitorWorkAreaDip.Bottom - num5);
		double num7 = left + num2 + num;
		double num8 = top;
		if (num7 + num4 > currentMonitorWorkAreaDip.Right)
		{
			num7 = left - num4 - num;
		}
		if (num7 < currentMonitorWorkAreaDip.Left)
		{
			num7 = Math.Clamp(left, currentMonitorWorkAreaDip.Left, max);
			num8 = top + num3 + num;
		}
		if (num8 + num5 > currentMonitorWorkAreaDip.Bottom)
		{
			num8 = num6;
		}
		window.Left = Math.Clamp(num7, currentMonitorWorkAreaDip.Left, max);
		window.Top = Math.Clamp(num8, currentMonitorWorkAreaDip.Top, num6);
	}

	private Rect GetCurrentMonitorWorkAreaDip()
	{
		try
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle != IntPtr.Zero)
			{
				nint hMonitor = MonitorFromWindow(handle, 2u);
				if (TryGetMonitorWorkAreaDip(hMonitor, out var workArea))
				{
					return workArea;
				}
			}
		}
		catch
		{
		}
		return SystemParameters.WorkArea;
	}

	private Rect GetMonitorWorkAreaDipFromPoint(System.Windows.Point dipPoint)
	{
		try
		{
			System.Windows.Point point = (PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity).Transform(dipPoint);
			nint hMonitor = MonitorFromPoint(new POINT
			{
				X = (int)Math.Round(point.X),
				Y = (int)Math.Round(point.Y)
			}, 2u);
			if (TryGetMonitorWorkAreaDip(hMonitor, out var workArea))
			{
				return workArea;
			}
		}
		catch
		{
		}
		return GetCurrentMonitorWorkAreaDip();
	}

	private bool TryGetMonitorWorkAreaDip(nint hMonitor, out Rect workArea)
	{
		workArea = Rect.Empty;
		if (hMonitor == IntPtr.Zero)
		{
			return false;
		}
		MONITORINFO lpmi = new MONITORINFO
		{
			cbSize = Marshal.SizeOf(typeof(MONITORINFO))
		};
		if (!GetMonitorInfo(hMonitor, ref lpmi))
		{
			return false;
		}
		System.Windows.Media.Matrix matrix = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
		System.Windows.Point point = matrix.Transform(new System.Windows.Point(lpmi.rcWork.left, lpmi.rcWork.top));
		System.Windows.Point point2 = matrix.Transform(new System.Windows.Point(lpmi.rcWork.right, lpmi.rcWork.bottom));
		workArea = new Rect(point, point2);
		return true;
	}

	private void RenderSelectedActorDetail()
	{
		int? num = ResolveSelectedDetailActorId();
		if (num.HasValue)
		{
			RenderActorDetail(num.Value);
		}
		else
		{
			ClearCombatDetailRows();
		}
	}

	private void CloseCombatDetailWindow()
	{
		_detailWindow?.Close();
	}

	private GridLength GetExpandedDpsColumnWidth()
	{
		return new GridLength(GetExpandedDpsColumnWidthValue());
	}

	private double GetExpandedDpsColumnWidthValue()
	{
		if (_lastCompactDpsWidth > 0.0)
		{
			return _lastCompactDpsWidth;
		}
		return Math.Max(315.0, _compactWidth - 14.0);
	}

	private void CaptureCompactDpsWidth()
	{
		if (!_isHudMode && !_partyOpen && mainContentBorder.ActualWidth > 0.0)
		{
			_lastCompactDpsWidth = mainContentBorder.ActualWidth;
		}
	}

	private void ApplyCompactWindowBounds()
	{
		base.MinWidth = 315.0;
		base.MaxWidth = double.PositiveInfinity;
		base.Width = Math.Clamp(_compactWidth, 315.0, 1400.0);
	}

	private void ApplyExpandedWindowBounds(double targetWidth)
	{
		base.MinWidth = _compactWidth;
		base.MaxWidth = double.PositiveInfinity;
		base.Width = targetWidth;
	}

	private void RefreshCurrentLayoutState()
	{
		if (!_isHudMode)
		{
			if (_partyOpen)
			{
				ApplyExpandedLayoutColumns();
				ApplyExpandedWindowBounds(_partyWidth);
			}
			else
			{
				ApplyCompactLayoutColumns();
				ApplyCompactWindowBounds();
			}
		}
	}

	private void ApplyCompactLayoutColumns()
	{
		colDps.Width = new GridLength(1.0, GridUnitType.Star);
		colSplitter.Width = new GridLength(0.0);
		colDetail.Width = new GridLength(0.0);
		colSplitterToolbar.Width = new GridLength(0.0);
		colDetailToolbar.Width = new GridLength(1.0, GridUnitType.Star);
	}

	private void ApplyExpandedLayoutColumns()
	{
		colDps.Width = GetExpandedDpsColumnWidth();
		colSplitter.Width = new GridLength(10.0);
		colDetail.Width = new GridLength(1.0, GridUnitType.Star);
		colSplitterToolbar.Width = new GridLength(10.0);
		colDetailToolbar.Width = new GridLength(1.0, GridUnitType.Star);
	}

	private void BtnDetailToggle_Click(object sender, RoutedEventArgs e)
	{
		if (IsCombatDetailWindowOpen())
		{
			CloseCombatDetailWindow();
			return;
		}
		int? num = ResolveSelectedDetailActorId();
		if (num.HasValue)
		{
			OpenDetailForActor(num.Value);
		}
	}

	private void BtnPartyToggle_Click(object sender, RoutedEventArgs e)
	{
		_partyOpen = !_partyOpen;
		if (_partyOpen)
		{
			CaptureCompactDpsWidth();
			borderParty.Visibility = Visibility.Visible;
			ApplyExpandedLayoutColumns();
			ApplyExpandedWindowBounds(_partyWidth);
			btnParty.ToolTip = "캐릭터정보 닫기";
			btnParty.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SideMenuIconActivePartyBrush");
		}
		else
		{
			_partyWidth = ((base.Width > 400.0) ? base.Width : _partyWidth);
			borderParty.Visibility = Visibility.Collapsed;
			ApplyCompactLayoutColumns();
			ApplyCompactWindowBounds();
			base.Dispatcher.BeginInvoke(new Action(CaptureCompactDpsWidth), DispatcherPriority.Loaded);
			btnParty.ToolTip = "캐릭터정보 열기";
			btnParty.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SideMenuIconInactiveBrush");
		}
	}

	private void BtnPause_Click(object sender, RoutedEventArgs e)
	{
		if (_isLogViewMode && _isPaused)
		{
			if (ThemedMessageBox.Show(this, "과거 로그 보기를 종료하고 실시간 분석으로 복귀하시겠습니까?", "알림", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
			{
				return;
			}
			BtnClear_Click(null, null);
			_isPaused = false;
			_isLogViewMode = false;
		}
		else
		{
			_isPaused = !_isPaused;
		}
		_pausedNowUtc = (_isPaused ? new DateTime?(DateTime.UtcNow) : ((DateTime?)null));
		UpdatePauseButtonUI();
		UpdateLoadLogButtonUI();
	}

	private void UpdatePauseButtonUI()
	{
		if (pathPauseIcon != null)
		{
			pathPauseIcon.Data = (_isPaused ? PlayIconGeometry : PauseIconGeometry);
		}
		btnPause.ToolTip = (_isPaused ? "재개" : "일시정지");
	}

	private void UpdatePrimaryActionButtonUI()
	{
		if (_isEncounterReplayActive)
		{
			pathPrimaryActionIcon.Data = StopIconGeometry;
			btnPrimaryAction.ToolTip = "리플레이 중지";
			pathResetHudIcon.Data = StopIconGeometry;
			btnResetHud.ToolTip = "리플레이 중지";
		}
		else if (_isLogViewMode && _isPaused)
		{
			pathPrimaryActionIcon.Data = PlayIconGeometry;
			btnPrimaryAction.ToolTip = "실시간 분석 재개";
			pathResetHudIcon.Data = PlayIconGeometry;
			btnResetHud.ToolTip = "실시간 분석 재개";
		}
		else
		{
			pathPrimaryActionIcon.Data = ResetIconGeometry;
			btnPrimaryAction.ToolTip = "초기화";
			pathResetHudIcon.Data = ResetIconGeometry;
			btnResetHud.ToolTip = "초기화";
		}
	}

	private void UpdateLoadLogButtonUI()
	{
		btnLoadLog.ToolTip = "전투 로그 불러오기";
		UpdatePrimaryActionButtonUI();
	}

	private void ClearUI(bool preserveDpsSelection = false)
	{
		bool flag = preserveDpsSelection || IsCombatDetailWindowOpen();
		lock (_sync)
		{
			DpsCards.Clear();
			ResetDpsRankReorderClock();
			if (!flag)
			{
				ClearSelectedDetailTarget();
			}
			else if (!string.IsNullOrWhiteSpace(_selectedDetailCharacterKey))
			{
				_selectedActorId = null;
			}
			ClearCombatDetailRows();
			_uiActors.Clear();
			_allBuffEvents.Clear();
			_pendingUiTargetResets.Clear();
		}
		ResetCombatScoreAutoBudget();
	}

	private void BtnClear_Click(object? sender, RoutedEventArgs? e)
	{
		ResetCurrentSession(!_isLogViewMode, clearArchivedHistory: false, startNewLog: true, preferLatestArchivedSelection: false);
	}

	private async void BtnLoadLog_Click(object sender, RoutedEventArgs e)
	{
		Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
		{
			Filter = "Combat Logs (*.inglog;*.csv)|*.inglog;*.csv|INGMeter Records (*.inglog)|*.inglog|CSV Logs (*.csv)|*.csv|All Files (*.*)|*.*",
			InitialDirectory = EncounterLogStore.RootDirectory
		};
		if (openFileDialog.ShowDialog() == true)
		{
			await LoadEncounterLogPathAsync(openFileDialog.FileName);
			ThemedMessageBox.Show(this, "전투 기록을 표시했습니다.\n실시간 측정은 계속 유지됩니다.");
		}
	}

	private async void BtnEncounterHistory_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			EncounterHistoryWindow window = new EncounterHistoryWindow
			{
				Owner = this
			};
			window.Loaded += async delegate
			{
				window.SetLoading(value: true);
				try
				{
					List<EncounterHistoryRow> records = await Task.Run((Func<List<EncounterHistoryRow>>)BuildEncounterHistoryRows);
					window.SetRecords(records);
				}
				catch (Exception ex2)
				{
					window.SetLoading(value: false);
					ThemedMessageBox.Show(window, "전투 기록을 불러올 수 없습니다.\n" + ex2.Message, "오류");
				}
			};
			if (window.ShowDialog() == true && window.SelectedRecord != null)
			{
				await LoadEncounterLogPathAsync(window.SelectedRecord.FullPath);
			}
		}
		catch (Exception ex)
		{
			ThemedMessageBox.Show(this, "전투 기록을 열 수 없습니다.\n" + ex.Message, "오류");
		}
	}

	private List<EncounterHistoryRow> BuildEncounterHistoryRows()
	{
		EncounterLogStore store = new EncounterLogStore();
		return (from row in (from record in store.ListRecords()
				select CreateEncounterHistoryRow(store, record)).Concat(CreateLegacyCsvHistoryRows())
			orderby row.StartUtc descending
			select row).ToList();
	}

	private EncounterHistoryRow CreateEncounterHistoryRow(EncounterLogStore store, EncounterLogIndexItem record)
	{
		string dungeonText = "";
		string categoryText = "";
		if (record.ContentCode > 0 && _dungeonContentMap.TryGet(record.ContentCode, out DungeonContentInfo info))
		{
			dungeonText = info.DisplayName;
			categoryText = info.Category;
		}
		else if (record.BossMobCode > 0)
		{
			IReadOnlyList<DungeonBossCatalogEntry> readOnlyList = _dungeonBossCatalogMap.FindDungeonsByBossCode(record.BossMobCode);
			DungeonBossCatalogEntry dungeonBossCatalogEntry = readOnlyList.FirstOrDefault();
			if (dungeonBossCatalogEntry != null)
			{
				dungeonText = dungeonBossCatalogEntry.DisplayName;
				categoryText = ((readOnlyList.Count > 1) ? $"{dungeonBossCatalogEntry.Category} 외 {readOnlyList.Count - 1:N0}개 후보" : dungeonBossCatalogEntry.Category);
			}
		}
		return new EncounterHistoryRow(record, store.ResolveRecordPath(record.FileName), dungeonText, categoryText);
	}

	private IEnumerable<EncounterHistoryRow> CreateLegacyCsvHistoryRows()
	{
		string rootDirectory = EncounterLogStore.RootDirectory;
		if (!Directory.Exists(rootDirectory))
		{
			yield break;
		}
		foreach (string item in Directory.EnumerateFiles(rootDirectory, "*.csv", SearchOption.TopDirectoryOnly))
		{
			string fileName = System.IO.Path.GetFileName(item);
			string fileNameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(item);
			if (fileNameWithoutExtension.Length >= 16 && DateTime.TryParseExact(fileNameWithoutExtension.Substring(0, 15), "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
			{
				object obj;
				if (fileNameWithoutExtension.Length <= 16)
				{
					obj = "CSV 로그";
				}
				else
				{
					string text = fileNameWithoutExtension;
					obj = text.Substring(16, text.Length - 16).Trim();
				}
				string text2 = (string)obj;
				EncounterLogIndexItem source = new EncounterLogIndexItem
				{
					Id = fileName,
					FileName = fileName,
					StartUtc = DateTime.SpecifyKind(result, DateTimeKind.Local).ToUniversalTime(),
					EndUtc = DateTime.SpecifyKind(result, DateTimeKind.Local).ToUniversalTime(),
					BossName = (string.IsNullOrWhiteSpace(text2) ? "CSV 로그" : text2)
				};
				yield return new EncounterHistoryRow(source, item, "CSV 로그", "이전 형식");
			}
		}
	}

	private async Task LoadEncounterLogPathAsync(string path, int? requestVersion = null, bool revealSelectedRow = true)
	{
		ArchivedBossRecord archivedBossRecord = FindArchivedBossRecordBySourcePath(path);
		if (archivedBossRecord != null && HasArchivedDetailEvents(archivedBossRecord))
		{
			if (!requestVersion.HasValue || IsLocalEncounterLogLoadCurrent(requestVersion.Value))
			{
				ApplyLoadedEncounterLogRecord(archivedBossRecord, revealSelectedRow);
			}
			return;
		}
		ArchivedBossRecord archivedBossRecord2 = await CreateArchivedBossRecordFromLogAsync(path);
		if (!requestVersion.HasValue || IsLocalEncounterLogLoadCurrent(requestVersion.Value))
		{
			if (archivedBossRecord2 == null)
			{
				throw new InvalidOperationException("표시할 전투 기록을 찾을 수 없습니다.");
			}
			ApplyLoadedEncounterLogRecord(archivedBossRecord2, revealSelectedRow);
		}
	}

	private void ApplyLoadedEncounterLogRecord(ArchivedBossRecord record, bool revealSelectedRow)
	{
		_isPaused = false;
		_pausedNowUtc = null;
		_isLogViewMode = false;
		UpdatePauseButtonUI();
		UpdateLoadLogButtonUI();
		AddArchivedBossRecords(new ArchivedBossRecord[1] { record });
		ArchivedBossRecord archivedBossRecord = FindArchivedBossRecordBySourcePath(record.SourceFullPath) ?? FindArchivedBossRecord(record.TargetId, record.Snapshot) ?? record;
		if (TrySelectArchivedBossRecord(archivedBossRecord.ArchivedRecordId))
		{
			PopulateTargetCombo();
			RenderTiles(archivedBossRecord.Snapshot);
			RefreshLocalEncounterPanelRows(GetLocalEncounterPanelKeyForArchivedRecord(archivedBossRecord), revealSelectedRow);
		}
		else
		{
			PopulateTargetCombo();
			RenderTiles(GetSnapshotForCurrentFilter());
		}
		if (IsCombatDetailWindowOpen())
		{
			RenderDetailForCurrentEncounter();
		}
	}

	private async Task<ArchivedBossRecord?> CreateArchivedBossRecordFromLogAsync(string path)
	{
		int archivedRecordId = _nextArchivedBossRecordId++;
		string sourceFullPath = NormalizeLogPath(path);
		string dungeonText = ResolveStoredEncounterDungeonText(path);
		string localPlayerDpsText = ResolveStoredEncounterLocalPlayerDpsText(path);
		return await Task.Run(async delegate
		{
			MeterEngine replayEngine = CreateHistoryReplayEngine();
			try
			{
				Dictionary<int, UiActorState> replayUiActors = new Dictionary<int, UiActorState>();
				replayEngine.DamageEventParsed += delegate(DamageEvent damageEvent)
				{
					ApplyReplayDamageEvent(replayEngine, replayUiActors, damageEvent);
				};
				replayEngine.BuffEventParsed += delegate(BuffEvent buffEvent)
				{
					ApplyReplayBuffEvent(replayEngine, replayUiActors, buffEvent);
				};
				await replayEngine.LoadLogFile(path);
				TargetInfo targetInfo = (from t in replayEngine.GetAllTargets()
					orderby t.TotalDamage descending, t.LastHit descending
					select t).FirstOrDefault();
				CombatSnapshot combatSnapshot = ((targetInfo != null) ? replayEngine.BuildSnapshotForTarget(targetInfo.TargetId) : replayEngine.LatestSnapshot);
				if (combatSnapshot == null || combatSnapshot.Actors.Count == 0)
				{
					return (ArchivedBossRecord)null;
				}
				int num = ((combatSnapshot.TopTargetId != 0) ? combatSnapshot.TopTargetId : (targetInfo?.TargetId ?? 0));
				if (num == 0)
				{
					num = combatSnapshot.TopTargetId;
				}
				string text = combatSnapshot.TopTargetName;
				if (string.IsNullOrWhiteSpace(text) || text == num.ToString(CultureInfo.InvariantCulture))
				{
					text = targetInfo?.Name ?? "";
				}
				if (string.IsNullOrWhiteSpace(text))
				{
					text = System.IO.Path.GetFileNameWithoutExtension(path);
				}
				return new ArchivedBossRecord
				{
					ArchivedRecordId = archivedRecordId,
					TargetId = num,
					BossMobCode = (targetInfo?.MobCode ?? 0),
					TargetName = text,
					DungeonText = dungeonText,
					LocalPlayerDpsText = localPlayerDpsText,
					SourceFullPath = sourceFullPath,
					DisplayTimeLocal = combatSnapshot.SessionStartUtc.ToLocalTime(),
					Snapshot = combatSnapshot,
					UiActors = replayUiActors
				};
			}
			finally
			{
				if (replayEngine != null)
				{
					((IDisposable)replayEngine).Dispose();
				}
			}
		});
	}

	private MeterEngine CreateHistoryReplayEngine()
	{
		MeterEngine meterEngine = new MeterEngine
		{
			ResolveSkillName = (int code) => _skillNames.GetNameOrCode(code),
			ContainsSkillCode = (int code) => _skillNames.HasKnownIdOrBase(code),
			ResolveMobName = (int code) => _mobNameMap.GetName(code),
			ResolveMobBossStatus = (int code) => _mobNameMap.IsBoss(code),
			BossOnlyMeasurement = false,
			SaveEncounterLogs = false
		};
		if (!string.IsNullOrWhiteSpace(_engine.LocalPlayerName))
		{
			meterEngine.SetLocalPlayer(_engine.LocalPlayerName);
		}
		return meterEngine;
	}

	private static void ApplyReplayDamageEvent(MeterEngine replayEngine, Dictionary<int, UiActorState> uiActors, DamageEvent damageEvent)
	{
		if (!uiActors.TryGetValue(damageEvent.ActorId, out UiActorState value))
		{
			value = new UiActorState(damageEvent.ActorId, damageEvent.TimestampUtc);
			uiActors[damageEvent.ActorId] = value;
		}
		value.Apply(damageEvent, IsSelfHealingReplayEvent(replayEngine, damageEvent));
		if (value.Recent.Count > 3000)
		{
			value.TrimRecent(2000);
		}
	}

	private static bool IsSelfHealingReplayEvent(MeterEngine replayEngine, DamageEvent damageEvent)
	{
		if (damageEvent.HealAmount <= 0 || damageEvent.TargetId <= 0)
		{
			return false;
		}
		int num = ((damageEvent.ActorId > 0) ? replayEngine.Names.ResolveActorId(damageEvent.ActorId) : 0);
		int num2 = ((damageEvent.TargetId > 0) ? replayEngine.Names.ResolveActorId(damageEvent.TargetId) : 0);
		if (num > 0 && num == num2)
		{
			return true;
		}
		if (damageEvent.Damage <= 0)
		{
			return damageEvent.MultiHitDamage > 0;
		}
		return true;
	}

	private static void ApplyReplayBuffEvent(MeterEngine replayEngine, Dictionary<int, UiActorState> uiActors, BuffEvent buffEvent)
	{
		int actorId = ((buffEvent.OwnerId > 0) ? buffEvent.OwnerId : buffEvent.TargetId);
		actorId = replayEngine.Names.ResolveActorId(actorId);
		if (actorId <= 0)
		{
			return;
		}
		UiBuffEvent e = new UiBuffEvent(buffEvent.TimestampUtc, buffEvent.Kind, actorId, buffEvent.TargetId, buffEvent.OwnerId, buffEvent.BuffId, buffEvent.SkillId, buffEvent.DurationMs, buffEvent.StartedAtMs, buffEvent.ExpiresAtMs, buffEvent.SkillLevel, buffEvent.BaseSkillLevel);
		HashSet<int> hashSet = new HashSet<int> { actorId };
		if (buffEvent.OwnerId > 0)
		{
			hashSet.Add(replayEngine.Names.ResolveActorId(buffEvent.OwnerId));
		}
		if (buffEvent.TargetId > 0)
		{
			hashSet.Add(replayEngine.Names.ResolveActorId(buffEvent.TargetId));
		}
		foreach (int item in hashSet.Where((int x) => x > 0))
		{
			if (!uiActors.TryGetValue(item, out UiActorState value))
			{
				value = (uiActors[item] = new UiActorState(item, buffEvent.TimestampUtc));
			}
			value.ApplyBuff(e);
			if (value.BuffEvents.Count > 1200)
			{
				value.TrimBuffEvents(800);
			}
		}
	}

	[DllImport("user32.dll")]
	private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vlc);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(nint hWnd, int id);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	private void ApplyHotkeys()
	{
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle == IntPtr.Zero)
		{
			return;
		}
		UnregisterHotKey(handle, 9000);
		UnregisterHotKey(handle, 9001);
		UnregisterHotKey(handle, 9002);
		UnregisterHotKey(handle, 9003);
		UnregisterHotKey(handle, 9004);
		UnregisterHotKey(handle, 9005);
		if (_pauseHotkey != "None")
		{
			var (fsModifiers, num) = ParseHotkey(_pauseHotkey);
			if (num != 0)
			{
				RegisterHotKey(handle, 9000, fsModifiers, num);
			}
		}
		if (_clearHotkey != "None")
		{
			var (fsModifiers2, num2) = ParseHotkey(_clearHotkey);
			if (num2 != 0)
			{
				RegisterHotKey(handle, 9001, fsModifiers2, num2);
			}
		}
		if (_hideHotkey != "None")
		{
			var (fsModifiers3, num3) = ParseHotkey(_hideHotkey);
			if (num3 != 0)
			{
				RegisterHotKey(handle, 9003, fsModifiers3, num3);
			}
		}
		if (_clickThroughHotkey != "None")
		{
			var (fsModifiers4, num4) = ParseHotkey(_clickThroughHotkey);
			if (num4 != 0)
			{
				RegisterHotKey(handle, 9004, fsModifiers4, num4);
			}
		}
		if (_mainViewHotkey != "None")
		{
			var (fsModifiers5, num5) = ParseHotkey(_mainViewHotkey);
			if (num5 != 0)
			{
				RegisterHotKey(handle, 9005, fsModifiers5, num5);
			}
		}
	}

	private (uint modifiers, uint vkey) ParseHotkey(string hotkey)
	{
		uint num = 0u;
		string text = hotkey;
		if (hotkey.Contains("Ctrl+"))
		{
			num |= 2;
			text = text.Replace("Ctrl+", "");
		}
		if (hotkey.Contains("Shift+"))
		{
			num |= 4;
			text = text.Replace("Shift+", "");
		}
		if (hotkey.Contains("Alt+"))
		{
			num |= 1;
			text = text.Replace("Alt+", "");
		}
		if (text == "~" || text == "`")
		{
			text = "Oem3";
		}
		if (Enum.TryParse<Key>(text, ignoreCase: true, out var result))
		{
			return (modifiers: num, vkey: (uint)KeyInterop.VirtualKeyFromKey(result));
		}
		return (modifiers: 0u, vkey: 0u);
	}

	private void ComponentDispatcher_ThreadPreprocessMessage(ref MSG msg, ref bool handled)
	{
		if (_isSettingsOpen || msg.message != 786)
		{
			return;
		}
		switch (((IntPtr)msg.wParam).ToInt32())
		{
		case 9000:
			handled = false;
			break;
		case 9001:
			HandleLatchedHotkey(9001, _clearHotkey, delegate
			{
				BtnClear_Click(null, null);
			}, ref handled);
			break;
		case 9003:
			HandleLatchedHotkey(9003, _hideHotkey, ToggleHiddenByHotkey, ref handled);
			break;
		case 9004:
			HandleLatchedHotkey(9004, _clickThroughHotkey, ToggleHudClickThroughByHotkey, ref handled);
			break;
		case 9005:
			HandleLatchedHotkey(9005, _mainViewHotkey, ToggleMainContentView, ref handled);
			break;
		}
	}

	private void HandleLatchedHotkey(int id, string hotkey, Action action, ref bool handled)
	{
		handled = true;
		if (!_latchedHotkeyIds.Contains(id))
		{
			_latchedHotkeyIds.Add(id);
			action();
			ReleaseHotkeyLatchAsync(id, hotkey);
		}
	}

	private void ToggleHiddenByHotkey()
	{
		if (base.Visibility == Visibility.Visible)
		{
			Hide();
			UpdateTrayIconVisibility();
		}
		else
		{
			ShowWithoutActivation();
			UpdateTrayIconVisibility();
		}
	}

	private void ShowWithoutActivation()
	{
		bool showActivated = base.ShowActivated;
		base.ShowActivated = false;
		if (base.WindowState == WindowState.Minimized)
		{
			base.WindowState = WindowState.Normal;
		}
		Show();
		nint handle = new WindowInteropHelper(this).Handle;
		if (handle != IntPtr.Zero)
		{
			SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, 23u);
		}
		base.ShowActivated = showActivated;
	}

	private void ToggleHudClickThroughByHotkey()
	{
		_hudClickThrough = !_hudClickThrough;
		ApplyHudClickThroughMode();
		SaveConfig();
	}

	private async Task ReleaseHotkeyLatchAsync(int id, string hotkey)
	{
		for (int i = 0; i < 120; i++)
		{
			await Task.Delay(25);
			if (!IsHotkeyPressed(hotkey))
			{
				break;
			}
		}
		_latchedHotkeyIds.Remove(id);
	}

	private bool IsHotkeyPressed(string hotkey)
	{
		if (string.IsNullOrWhiteSpace(hotkey) || hotkey == "None")
		{
			return false;
		}
		uint item = ParseHotkey(hotkey).vkey;
		if (item == 0 || !IsVirtualKeyDown((int)item))
		{
			return false;
		}
		if ((!hotkey.Contains("Ctrl+", StringComparison.OrdinalIgnoreCase) || IsVirtualKeyDown(17)) && (!hotkey.Contains("Shift+", StringComparison.OrdinalIgnoreCase) || IsVirtualKeyDown(16)))
		{
			if (hotkey.Contains("Alt+", StringComparison.OrdinalIgnoreCase))
			{
				return IsVirtualKeyDown(18);
			}
			return true;
		}
		return false;
	}

	private static bool IsVirtualKeyDown(int vkey)
	{
		return (GetAsyncKeyState(vkey) & -32768) != 0;
	}

	private void ApplySettingsWindowValues(SettingsWindow sw)
	{
		CaptureBackend captureBackend = sw.CaptureBackend;
		if (captureBackend == CaptureBackend.NpcapMirror && !NpcapMirrorCaptureService.TryValidateAvailable(out string message))
		{
			captureBackend = CaptureBackend.WinDivert;
			sw.SetCaptureBackendSelection(captureBackend);
			ShowSystemBalloon(message);
		}
		bool flag = _captureBackend != captureBackend;
		_pauseHotkey = "None";
		_clearHotkey = sw.ClearHotkey;
		_hudHotkey = "None";
		_hideHotkey = sw.HideHotkey;
		_clickThroughHotkey = sw.ClickThroughHotkey;
		_mainViewHotkey = sw.MainViewHotkey;
		_maxDpsCards = Math.Clamp(sw.MaxDpsCards, 1, 10);
		_showActorId = sw.ShowActorId;
		_useDummyData = sw.UseDummyData;
		_autoBossFilter = true;
		_bossOnlyMeasurement = true;
		_engine.BossOnlyMeasurement = true;
		_autoResetOnMapChange = false;
		_engine.MapChangeAutoReset = false;
		_autoResetOnNewBoss = true;
		_saveEncounterLogs = sw.SaveEncounterLogs;
		_engine.SaveEncounterLogs = _saveEncounterLogs;
		_hudClickThrough = sw.HudClickThrough;
		_showBossCard = sw.ShowBossCard;
		_showDpsCardCombatTime = sw.ShowDpsCardCombatTime;
		_autoHideBackground = sw.AutoHideBackground;
		_showOnlyWhenAionActive = sw.ShowOnlyWhenAionActive;
		_showInTaskbar = sw.ShowAppInTaskbar;
		if (TryParseCloseButtonBehavior(sw.CloseButtonBehaviorName, out var behavior))
		{
			_closeButtonBehavior = behavior;
		}
		_displayPreset = sw.DisplayPreset;
		_dpsCardNumberFormatMode = sw.DpsCardNumberFormatMode;
		_uiScale = MeterScaleOptions.NormalizeUiScale(sw.UiScale);
		_textScale = MeterScaleOptions.NormalizeTextScale(sw.TextScale);
		_fontWeightMode = sw.FontWeightMode;
		_fontFamilyName = sw.FontFamilyName;
		_textShadowEnabled = sw.TextShadowEnabled;
		_damageShareMode = sw.DamageShareMode;
		_damageShareGraphMode = sw.DamageShareGraphMode;
		_captureBackend = captureBackend;
		_devKey = sw.DevKey;
		ApplyDeveloperWebEndpoint();
		SetAppearance(AppearanceCatalog.FromLegacyThemeName(sw.Theme), applyResources: true);
		bool flag2 = _lookupSkillDisplayEnabled != sw.LookupSkillDisplayEnabled;
		_lookupSkillDisplayEnabled = sw.LookupSkillDisplayEnabled;
		Dictionary<string, HashSet<int>> dictionary = LookupSkillSelectionSerializer.Clone(sw.LookupSkillSelections);
		bool flag3 = !LookupSkillSelectionSerializer.AreEqual(_lookupSkillSelections, dictionary);
		HashSet<string> hashSet = LookupSkillClassSetSerializer.Clone(sw.LookupSkillDisabledClasses);
		bool flag4 = !LookupSkillClassSetSerializer.AreEqual(_lookupSkillDisabledClasses, hashSet);
		if (flag3 || flag2 || flag4)
		{
			if (flag3)
			{
				_lookupSkillSelections = dictionary;
			}
			if (flag4)
			{
				_lookupSkillDisabledClasses = hashSet;
			}
			Interlocked.Increment(ref _lookupSkillSelectionVersion);
			ClearOfficialSkillSummaryCache();
		}
		ApplyUpdateBadgeVisibility();
		RefreshVisibleMeterPresence();
		_windowOpacity = Math.Clamp((double)sw.WindowOpacityPercent / 100.0, 0.2, 1.0);
		_hudOpacity = Math.Clamp((double)sw.HudOpacityPercent / 100.0, 0.2, 1.0);
		ApplyWindowOpacity();
		ApplyFontFamily();
		ApplyFontWeightMode();
		ApplyTextShadowPreference();
		UpdateAionActiveVisibility();
		ApplyHudClickThroughMode();
		foreach (DpsCardViewModel dpsCard in DpsCards)
		{
			dpsCard.ShowCombatTime = _showDpsCardCombatTime;
		}
		ApplyDisplayPresetVisualState(forceScale: true);
		if (flag2 || flag4)
		{
			ApplyLookupSkillDisplayEnabledToItems();
		}
		ApplyShowInTaskbarPreference();
		ApplyHotkeys();
		SaveConfig();
		if (flag)
		{
			RestartCaptureService();
		}
		if ((flag3 || flag2 || flag4) && _lookupSkillDisplayEnabled)
		{
			RefreshOfficialSkillSummaries(force: true);
		}
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			RenderTiles(GetSnapshotForCurrentFilter());
			RefreshCurrentLayoutState();
			CaptureCompactDpsWidth();
		}, DispatcherPriority.Loaded);
	}

	private void BtnSettings_Click(object sender, RoutedEventArgs e)
	{
		OpenSettingsWindow();
	}

	private void BtnSettingsMenu_Click(object sender, RoutedEventArgs e)
	{
		Popup popup;
		if (sender is FrameworkElement placementTarget)
		{
			popup = new Popup
			{
				PlacementTarget = placementTarget,
				Placement = PlacementMode.Bottom,
				StaysOpen = false,
				AllowsTransparency = true,
				PopupAnimation = PopupAnimation.Fade
			};
			StackPanel stackPanel = new StackPanel
			{
				Width = 132.0
			};
			Border border = new Border
			{
				Height = 1.0,
				Margin = new Thickness(6.0, 5.0, 6.0, 5.0)
			};
			border.SetResourceReference(Border.BackgroundProperty, "ThemeBorderBrush");
			stackPanel.Children.Add(BuildRow("설정", null, OpenSettingsWindow));
			stackPanel.Children.Add(border);
			stackPanel.Children.Add(BuildRow("미니멀 표시", _displayPreset == MeterDisplayPreset.Minimal, delegate
			{
				SetDisplayPreset((_displayPreset == MeterDisplayPreset.Minimal) ? MeterDisplayPreset.Standard : MeterDisplayPreset.Minimal);
			}));
			stackPanel.Children.Add(BuildRow("항상 위", base.Topmost, delegate
			{
				SetTopmostState(!base.Topmost);
			}));
			stackPanel.Children.Add(BuildRow("버프 타이머", _buffTimerEnabled, delegate
			{
				SetBuffTimerEnabled(!_buffTimerEnabled);
			}));
			stackPanel.Children.Add(BuildSettingsOpacityMenuItem());
			Border border2 = new Border
			{
				Child = stackPanel,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(7.0),
				Padding = new Thickness(5.0, 5.0, 5.0, 6.0),
				SnapsToDevicePixels = true
			};
			border2.SetResourceReference(Border.BackgroundProperty, "ThemeSecondaryBackgroundBrush");
			border2.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
			popup.Child = border2;
			popup.IsOpen = true;
		}
		Border BuildRow(string text, bool? isChecked, Action action)
		{
			Border row = new Border
			{
				Height = 27.0,
				CornerRadius = new CornerRadius(5.0),
				Background = System.Windows.Media.Brushes.Transparent,
				Cursor = System.Windows.Input.Cursors.Hand
			};
			Grid grid = new Grid
			{
				Margin = new Thickness(8.0, 0.0, 7.0, 0.0)
			};
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1.0, GridUnitType.Star)
			});
			grid.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = (isChecked.HasValue ? new GridLength(14.0) : new GridLength(0.0))
			});
			TextBlock textBlock = new TextBlock
			{
				Text = text,
				FontSize = 12.0,
				VerticalAlignment = VerticalAlignment.Center
			};
			textBlock.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimaryBrush");
			TextBlock textBlock2 = new TextBlock
			{
				Text = "✓",
				FontSize = 11.0,
				FontWeight = FontWeights.Bold,
				Visibility = ((isChecked != true) ? Visibility.Hidden : Visibility.Visible),
				HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
				VerticalAlignment = VerticalAlignment.Center
			};
			textBlock2.SetResourceReference(TextBlock.ForegroundProperty, "ThemeAccentBrush");
			Grid.SetColumn(textBlock, 0);
			Grid.SetColumn(textBlock2, 1);
			grid.Children.Add(textBlock);
			grid.Children.Add(textBlock2);
			row.Child = grid;
			row.MouseEnter += delegate
			{
				row.SetResourceReference(Border.BackgroundProperty, "MenuItemHoverBackgroundBrush");
			};
			row.MouseLeave += delegate
			{
				row.Background = System.Windows.Media.Brushes.Transparent;
			};
			row.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs args)
			{
				action();
				popup.IsOpen = false;
				args.Handled = true;
			};
			return row;
		}
	}

	private FrameworkElement BuildSettingsOpacityMenuItem()
	{
		bool hudOpacity = _isHudMode;
		int num = (hudOpacity ? GetHudOpacityPercent() : GetWindowOpacityPercent());
		TextBlock valueText = new TextBlock
		{
			Text = $"{num}%",
			FontSize = 10.0,
			FontWeight = FontWeights.SemiBold,
			TextAlignment = TextAlignment.Right,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Center
		};
		valueText.SetResourceReference(TextBlock.ForegroundProperty, "ThemeAccentBrush");
		Border element = new Border
		{
			Width = 58.0,
			Height = 4.0,
			CornerRadius = new CornerRadius(2.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(82, 92, 104))
		};
		Border fill = new Border
		{
			Height = 4.0,
			CornerRadius = new CornerRadius(2.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 210, byte.MaxValue))
		};
		Border thumb = new Border
		{
			Width = 8.0,
			Height = 8.0,
			CornerRadius = new CornerRadius(4.0),
			Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(238, 244, 250))
		};
		Canvas bar = new Canvas
		{
			Width = 66.0,
			Height = 16.0,
			ClipToBounds = false,
			Background = System.Windows.Media.Brushes.Transparent,
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
			VerticalAlignment = VerticalAlignment.Center
		};
		Canvas.SetLeft(element, 4.0);
		Canvas.SetTop(element, 6.0);
		Canvas.SetLeft(fill, 4.0);
		Canvas.SetTop(fill, 6.0);
		Canvas.SetTop(thumb, 4.0);
		bar.Children.Add(element);
		bar.Children.Add(fill);
		bar.Children.Add(thumb);
		bar.SizeChanged += delegate
		{
			ApplyOpacityPercent(hudOpacity ? GetHudOpacityPercent() : GetWindowOpacityPercent(), save: false);
		};
		bar.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs args)
		{
			SetFromPoint(args.GetPosition(bar));
			bar.CaptureMouse();
			args.Handled = true;
		};
		bar.MouseMove += delegate(object _, System.Windows.Input.MouseEventArgs args)
		{
			if (bar.IsMouseCaptured && args.LeftButton == MouseButtonState.Pressed)
			{
				SetFromPoint(args.GetPosition(bar));
				args.Handled = true;
			}
		};
		bar.MouseLeftButtonUp += delegate(object _, MouseButtonEventArgs args)
		{
			if (bar.IsMouseCaptured)
			{
				bar.ReleaseMouseCapture();
			}
			args.Handled = true;
		};
		ApplyOpacityPercent(num, save: false);
		Grid grid = new Grid
		{
			Width = 106.0,
			Height = 18.0,
			Margin = new Thickness(8.0, 0.0, 8.0, 3.0),
			HorizontalAlignment = System.Windows.HorizontalAlignment.Left
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(66.0)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(32.0)
		});
		Grid.SetColumn(bar, 0);
		Grid.SetColumn(valueText, 2);
		grid.Children.Add(bar);
		grid.Children.Add(valueText);
		return new Border
		{
			Child = grid,
			Width = 122.0,
			Background = System.Windows.Media.Brushes.Transparent,
			CornerRadius = new CornerRadius(6.0)
		};
		void ApplyOpacityPercent(int percent, bool save)
		{
			percent = Math.Clamp((int)Math.Round((double)percent / 5.0) * 5, 20, 100);
			double num2 = (double)(percent - 20) / 80.0;
			fill.Width = 58.0 * num2;
			Canvas.SetLeft(thumb, 4.0 + 58.0 * num2 - 4.0);
			valueText.Text = $"{percent}%";
			if (hudOpacity)
			{
				_hudOpacity = (double)percent / 100.0;
			}
			else
			{
				_windowOpacity = (double)percent / 100.0;
			}
			ApplyWindowOpacity();
			if (save)
			{
				SaveConfig();
			}
		}
		void SetFromPoint(System.Windows.Point point)
		{
			double num2 = Math.Clamp((point.X - 4.0) / Math.Max(1.0, 58.0), 0.0, 1.0);
			ApplyOpacityPercent((int)Math.Round(20.0 + num2 * 80.0), save: true);
		}
	}

	private void OpenSettingsWindow()
	{
		if (_settingsWindow != null)
		{
			if (_settingsWindow.WindowState == WindowState.Minimized)
			{
				_settingsWindow.WindowState = WindowState.Normal;
			}
			_settingsWindow.Show();
			_settingsWindow.Activate();
			return;
		}
		_isSettingsOpen = true;
		try
		{
			SettingsWindow sw = new SettingsWindow(_clearHotkey, _hudHotkey, _hideHotkey, _clickThroughHotkey, _mainViewHotkey, _maxDpsCards, _showActorId, _useDummyData, _saveEncounterLogs, _hudClickThrough, _showBossCard, _showDpsCardCombatTime, _autoHideBackground, _showOnlyWhenAionActive, _showInTaskbar, _closeButtonBehavior.ToString(), GetWindowOpacityPercent(), GetHudOpacityPercent(), _isHudMode, _displayPreset, _dpsCardNumberFormatMode, _damageShareMode, _damageShareGraphMode, _uiScale, _textScale, _fontWeightMode, _fontFamilyName, _textShadowEnabled, _captureBackend, _devKey, CurrentThemeName, _lookupSkillDisplayEnabled, _lookupSkillCatalog, _lookupSkillSelections, _lookupSkillDisabledClasses, _updateService, _engine, _skillNames, GetCharacterConsentStatesSnapshot(), SetCharacterPublicConsentFromSettingsAsync, GetCharacterConsentStatesSnapshot);
			_settingsWindow = sw;
			sw.Owner = this;
			PositionSettingsWindow(sw);
			sw.SettingsChanged += ApplySettingsWindowValues;
			sw.Closed += delegate
			{
				sw.SettingsChanged -= ApplySettingsWindowValues;
				if (_settingsWindow == sw)
				{
					_settingsWindow = null;
				}
				_isSettingsOpen = false;
			};
			sw.Show();
		}
		catch (Exception ex)
		{
			_settingsWindow = null;
			_isSettingsOpen = false;
			AppendSettingsOpenError(ex);
			ThemedMessageBox.Show(this, "설정창을 열 수 없습니다.\n" + ex.Message, "설정", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private static void AppendSettingsOpenError(Exception ex)
	{
		string contents = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}";
		try
		{
			File.AppendAllText(System.IO.Path.Combine(AppPaths.LogsDirectory, "settings_open_error.log"), contents);
		}
		catch
		{
		}
	}

	private void PositionSettingsWindow(Window settingsWindow)
	{
		Rect workArea = SystemParameters.WorkArea;
		double num = 12.0;
		double num2 = (double.IsNaN(settingsWindow.Width) ? settingsWindow.MinWidth : settingsWindow.Width);
		double num3 = (double.IsNaN(settingsWindow.Height) ? settingsWindow.MinHeight : settingsWindow.Height);
		double num4 = ((base.ActualWidth > 0.0) ? base.ActualWidth : base.Width);
		double num5 = ((base.ActualHeight > 0.0) ? base.ActualHeight : base.Height);
		if (workArea.Width >= settingsWindow.MinWidth && num2 > workArea.Width)
		{
			settingsWindow.Width = workArea.Width;
			num2 = settingsWindow.Width;
		}
		if (workArea.Height >= settingsWindow.MinHeight && num3 > workArea.Height)
		{
			settingsWindow.Height = workArea.Height;
			num3 = settingsWindow.Height;
		}
		double num6 = base.Left + num4 + num;
		double num7 = base.Left - num2 - num;
		double left = workArea.Left;
		double max = Math.Max(left, workArea.Right - num2);
		if (num6 + num2 <= workArea.Right)
		{
			settingsWindow.Left = num6;
		}
		else if (num7 >= workArea.Left)
		{
			settingsWindow.Left = num7;
		}
		else
		{
			settingsWindow.Left = ClampWindowPosition(base.Left + (num4 - num2) / 2.0, left, max);
		}
		double value = base.Top + Math.Min(0.0, (num5 - num3) / 2.0);
		double top = workArea.Top;
		double max2 = Math.Max(top, workArea.Bottom - num3);
		settingsWindow.Top = ClampWindowPosition(value, top, max2);
	}

	private static double ClampWindowPosition(double value, double min, double max)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			return min;
		}
		return Math.Clamp(value, min, max);
	}

	private CombatSnapshot CreateDummySnapshot()
	{
		DateTime utcNow = DateTime.UtcNow;
		List<ActorStats> list = new List<ActorStats>
		{
			new ActorStats(1, "나의캐릭터", "시엘", JobClass.Gladiator, 201863354L, 1614907.0, 100, 30, 0.3, 5, IsMonster: false, default(DateTime), default(DateTime), null, 0L, 0.0, 0, 0L, 0L),
			new ActorStats(7, "백만DPS테스터", "시엘", JobClass.Assassin, 262422360L, 2099379.0, 72, 31, 0.43, 9, IsMonster: false, default(DateTime), default(DateTime), null, 0L, 0.0, 0, 0L, 0L),
			new ActorStats(2, "용맹한검성", "이스라펠", JobClass.Gladiator, 45419255L, 363354.0, 90, 25, 0.27, 4, IsMonster: false, default(DateTime), default(DateTime), null, 0L, 0.0, 0, 0L, 0L),
			new ActorStats(3, "치유의손길", "아리엘", JobClass.Cleric, 12111801L, 96894.0, 40, 5, 0.12, 1, IsMonster: false, default(DateTime), default(DateTime), null, 0L, 0.0, 0, 0L, 0L),
			new ActorStats(4, "강력한마도", "지켈", JobClass.Sorcerer, 47437888L, 379503.0, 60, 20, 0.33, 8, IsMonster: false, default(DateTime), default(DateTime), null, 0L, 0.0, 0, 0L, 0L),
			new ActorStats(5, "백발백중궁성", "코치룽", JobClass.Ranger, 42391304L, 339130.0, 80, 22, 0.25, 3, IsMonster: false, default(DateTime), default(DateTime), null, 0L, 0.0, 0, 0L, 0L),
			new ActorStats(6, "정령의소환사", "바바룽", JobClass.Spiritmaster, 38354038L, 306832.0, 75, 15, 0.2, 6, IsMonster: false, default(DateTime), default(DateTime), null, 0L, 0.0, 0, 0L, 0L)
		};
		long num = list.Sum((ActorStats a) => a.TotalDamage);
		int topTargetCurrentHp = Math.Max(0, 1000000000 - (int)Math.Min(1000000000L, num));
		return new CombatSnapshot(utcNow.AddSeconds(-125.0), utcNow, TimeSpan.FromSeconds(125L), list, 99999, "테스트용 보스", num, 500, TimeSpan.FromSeconds(125L), IsBossActive: true, IsBossConfirmed: true, 1000000000, topTargetCurrentHp);
	}

	private void BtnCopy_Click(object sender, RoutedEventArgs e)
	{
		if (lstDps.SelectedItems.Count > 0)
		{
			IEnumerable<string> values = lstDps.SelectedItems.Cast<DpsCardViewModel>().Select(delegate(DpsCardViewModel c)
			{
				string value = (string.IsNullOrWhiteSpace(c.ServerName) ? c.Name : (c.Name + "(" + c.ServerName + ")"));
				string value2 = ((c.TotalDamage >= 1000000) ? $"{(double)c.TotalDamage / 10000.0:0}만" : c.TotalDamage.ToString("N0"));
				string value3 = (string.IsNullOrWhiteSpace(c.DpsText) ? "--" : c.DpsText.Replace(",", ""));
				string value4 = (string.IsNullOrWhiteSpace(c.DamageSharePctText) ? "0.0%" : c.DamageSharePctText);
				string value5 = (string.IsNullOrWhiteSpace(c.CritRateText) ? "0.0%" : c.CritRateText);
				return $"{value} {value2} DPS: {value3}({value4}) Crit: {value5}";
			});
			string text = string.Join(" / ", values);
			if (!string.IsNullOrEmpty(text))
			{
				System.Windows.Clipboard.SetText(text);
			}
		}
	}

	private void OnSummonMerged(int summonId, int ownerId)
	{
		lock (_sync)
		{
			if (_uiActors.Remove(summonId, out UiActorState _))
			{
				DpsCardViewModel dpsCardViewModel = DpsCards.FirstOrDefault((DpsCardViewModel x) => x.ActorId == summonId);
				if (dpsCardViewModel != null)
				{
					DpsCards.Remove(dpsCardViewModel);
				}
			}
		}
	}

	private void OnDamageEventParsed(DamageEvent e)
	{
		Interlocked.Increment(ref _parsedDamageEvents);
		TrySwitchToDominantLiveBossForDamage(e);
		lock (_sync)
		{
			if (e.TargetId > 0 && _pendingUiTargetResets.Remove(e.TargetId))
			{
				ClearUiDamageForTargetLocked(e.TargetId);
			}
			if (!_uiActors.TryGetValue(e.ActorId, out UiActorState value))
			{
				value = new UiActorState(e.ActorId, e.TimestampUtc);
				_uiActors[e.ActorId] = value;
			}
			value.Apply(e, IsSelfHealingUiEvent(e));
			if (value.Recent.Count > 3000)
			{
				value.TrimRecent(2000);
			}
		}
	}

	private bool IsSelfHealingUiEvent(DamageEvent e)
	{
		if (e.HealAmount <= 0 || e.TargetId <= 0)
		{
			return false;
		}
		int num = ((e.ActorId > 0) ? _engine.Names.ResolveActorId(e.ActorId) : 0);
		int num2 = ((e.TargetId > 0) ? _engine.Names.ResolveActorId(e.TargetId) : 0);
		if (num > 0 && num == num2)
		{
			return true;
		}
		if (e.Damage <= 0)
		{
			return e.MultiHitDamage > 0;
		}
		return true;
	}

	private void TrySwitchToDominantLiveBossForDamage(DamageEvent e)
	{
		int targetId = ((e.TargetId > 0) ? _engine.Names.ResolveActorId(e.TargetId) : 0);
		if (targetId <= 0 || !IsConfirmedBossTarget(targetId))
		{
			return;
		}
		int num = ResolveLiveBossFocusTarget(targetId);
		if (num <= 0 || !IsConfirmedBossTarget(num) || (_encounterViewKind == EncounterViewKind.LiveBoss && _selectedTargetFilterOption.Kind == TargetFilterItemKind.LiveBoss && _selectedTargetFilterOption.TargetId == num))
		{
			return;
		}
		base.Dispatcher.BeginInvoke((Action)delegate
		{
			int num2 = ResolveLiveBossFocusTarget(targetId);
			if (num2 > 0 && IsConfirmedBossTarget(num2) && (_encounterViewKind != EncounterViewKind.LiveBoss || _selectedTargetFilterOption.Kind != TargetFilterItemKind.LiveBoss || _selectedTargetFilterOption.TargetId != num2))
			{
				SetMainContentView(MainContentView.Dps, manual: false, force: true);
				if (SetLiveBossEncounter(num2))
				{
					PopulateTargetCombo();
					RenderTiles(_engine.BuildSnapshotForTarget(num2) ?? GetSnapshotForCurrentFilter());
					RefreshLocalEncounterPanelRows(GetLiveEncounterPanelKey(num2));
					if (IsCombatDetailWindowOpen())
					{
						RenderDetailForCurrentEncounter();
					}
				}
			}
		}, DispatcherPriority.Background);
	}

	private bool IsConfirmedBossTarget(int targetId)
	{
		return _engine.IsConfirmedBossTarget(targetId);
	}

	private void OnBuffEventParsed(BuffEvent e)
	{
		Interlocked.Increment(ref _parsedBuffEvents);
		int actorId = ((e.OwnerId > 0) ? e.OwnerId : e.TargetId);
		actorId = _engine.Names.ResolveActorId(actorId);
		UiBuffEvent uiBuffEvent = new UiBuffEvent(e.TimestampUtc, e.Kind, actorId, e.TargetId, e.OwnerId, e.BuffId, e.SkillId, e.DurationMs, e.StartedAtMs, e.ExpiresAtMs, e.SkillLevel, e.BaseSkillLevel);
		lock (_sync)
		{
			UpdateActiveBuffEvent(uiBuffEvent);
			_allBuffEvents.Enqueue(uiBuffEvent);
			while (_allBuffEvents.Count > 10000)
			{
				_allBuffEvents.Dequeue();
			}
			if (actorId <= 0)
			{
				return;
			}
			HashSet<int> hashSet = new HashSet<int> { actorId };
			if (e.OwnerId > 0)
			{
				hashSet.Add(_engine.Names.ResolveActorId(e.OwnerId));
			}
			if (e.TargetId > 0)
			{
				hashSet.Add(_engine.Names.ResolveActorId(e.TargetId));
			}
			foreach (int item in hashSet.Where((int x) => x > 0))
			{
				if (!_uiActors.TryGetValue(item, out UiActorState value))
				{
					value = new UiActorState(item, e.TimestampUtc);
					_uiActors[item] = value;
				}
				value.ApplyBuff(uiBuffEvent);
				if (value.BuffEvents.Count > 1200)
				{
					value.TrimBuffEvents(800);
				}
			}
		}
		if (_buffTimerEnabled)
		{
			base.Dispatcher.BeginInvoke((Action)delegate
			{
				RefreshBuffTimerWindow(force: true);
			}, DispatcherPriority.Background);
		}
	}

	private void UpdateActiveBuffEvent(UiBuffEvent e)
	{
		int num = ((e.BuffId > 0) ? e.BuffId : e.SkillId);
		if (num <= 0)
		{
			return;
		}
		int? localPlayerActorId = _engine.LocalPlayerActorId;
		int num2;
		int num3;
		if (localPlayerActorId.HasValue && e.TargetId == localPlayerActorId.Value)
		{
			num2 = ((e.OwnerId == localPlayerActorId.Value) ? 1 : 0);
			if (num2 != 0)
			{
				num3 = e.TargetId;
				goto IL_0080;
			}
		}
		else
		{
			num2 = 0;
		}
		num3 = ((e.TargetId > 0) ? _engine.Names.ResolveActorId(e.TargetId) : 0);
		goto IL_0080;
		IL_0080:
		int num4 = num3;
		int num5 = ((num2 != 0) ? e.OwnerId : ((e.OwnerId > 0) ? _engine.Names.ResolveActorId(e.OwnerId) : 0));
		int num6 = ((num4 > 0) ? num4 : num5);
		if (num6 <= 0)
		{
			return;
		}
		UiBuffStateKey key = new UiBuffStateKey(num6, num);
		UiBuffEvent uiBuffEvent = e with
		{
			ActorId = num6,
			TargetId = ((num4 > 0) ? num4 : e.TargetId),
			OwnerId = ((num5 > 0) ? num5 : e.OwnerId)
		};
		if (!BuffIntervalUtilities.HasInterval(uiBuffEvent.DurationMs, uiBuffEvent.ExpiresAtMs))
		{
			_activeBuffEvents.Remove(key);
			return;
		}
		(DateTime, DateTime) interval = BuffIntervalUtilities.GetInterval(uiBuffEvent.TimestampUtc, uiBuffEvent.DurationMs, uiBuffEvent.StartedAtMs, uiBuffEvent.ExpiresAtMs);
		if (interval.Item2 <= interval.Item1 || interval.Item2 <= DateTime.UtcNow)
		{
			_activeBuffEvents.Remove(key);
			return;
		}
		_activeBuffEvents[key] = uiBuffEvent;
		PruneActiveBuffEvents(DateTime.UtcNow);
	}

	private void PruneActiveBuffEvents(DateTime utcNow)
	{
		foreach (UiBuffStateKey item in _activeBuffEvents.Keys.ToList())
		{
			UiBuffEvent uiBuffEvent = _activeBuffEvents[item];
			if (!BuffIntervalUtilities.HasInterval(uiBuffEvent.DurationMs, uiBuffEvent.ExpiresAtMs) || BuffIntervalUtilities.GetInterval(uiBuffEvent.TimestampUtc, uiBuffEvent.DurationMs, uiBuffEvent.StartedAtMs, uiBuffEvent.ExpiresAtMs).End <= utcNow)
			{
				_activeBuffEvents.Remove(item);
			}
		}
	}

	private void LoadSkillNames()
	{
		try
		{
			_skillNames.LoadFromResource();
		}
		catch
		{
		}
	}

	private static bool IsAdministrator()
	{
		using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	private bool ShouldAutoFetchCombatScore(ActorStats actor)
	{
		if (string.IsNullOrWhiteSpace(actor.Name) || string.IsNullOrWhiteSpace(actor.ServerName))
		{
			return false;
		}
		if (actor.Name.StartsWith("Actor ") || int.TryParse(actor.Name, out var _))
		{
			return false;
		}
		int aion2ServerId = PartyTracker.GetAion2ServerId(actor.ServerName);
		if (aion2ServerId <= 0)
		{
			return false;
		}
		string averageDpsCacheKey = GetAverageDpsCacheKey(actor.Name, aion2ServerId);
		lock (_combatScoreCache)
		{
			if (_combatScoreCache.ContainsKey(averageDpsCacheKey) || _combatScoreLoading.Contains(averageDpsCacheKey))
			{
				return true;
			}
			if (_combatScoreAutoRequestedThisSession.Contains(averageDpsCacheKey))
			{
				return false;
			}
			if (_combatScoreAutoRequestedThisSession.Count >= 10)
			{
				return false;
			}
			_combatScoreAutoRequestedThisSession.Add(averageDpsCacheKey);
			return true;
		}
	}

	private bool CanFetchAverageDpsForSnapshot(CombatSnapshot? snap)
	{
		if (snap == null || !snap.IsBossConfirmed || snap.TopTargetId <= 0)
		{
			return true;
		}
		if (TryGetAverageDpsBossMobCode(snap, out var mobCode))
		{
			return HasMobCodePrefix(mobCode, 23);
		}
		return false;
	}

	private bool TryGetAverageDpsBossMobCode(CombatSnapshot snap, out int mobCode)
	{
		mobCode = 0;
		if (_encounterViewKind == EncounterViewKind.ArchivedBoss)
		{
			ArchivedBossRecord selectedArchivedBossRecord = GetSelectedArchivedBossRecord();
			if (selectedArchivedBossRecord != null && selectedArchivedBossRecord.BossMobCode > 0)
			{
				mobCode = selectedArchivedBossRecord.BossMobCode;
				return true;
			}
		}
		TargetInfo targetInfo = _engine.GetAllTargets().FirstOrDefault((TargetInfo t) => t.TargetId == snap.TopTargetId);
		if (targetInfo != null && targetInfo.MobCode > 0)
		{
			mobCode = targetInfo.MobCode;
			return true;
		}
		return false;
	}

	private static bool HasMobCodePrefix(int mobCode, int prefix)
	{
		for (mobCode = Math.Abs(mobCode); mobCode >= 100; mobCode /= 10)
		{
		}
		return mobCode == prefix;
	}

	private static void ClearAverageDps(DpsCardViewModel card)
	{
		if (!string.IsNullOrEmpty(card.CombatScore))
		{
			card.CombatScore = "";
		}
		if (card.IsDungeonAverageDps)
		{
			card.IsDungeonAverageDps = false;
		}
		if (!string.IsNullOrEmpty(card.AverageDpsScopeKey))
		{
			card.AverageDpsScopeKey = "";
		}
	}

	private static string GetCombatScoreCacheKey(string characterName, int serverId)
	{
		return $"{serverId}:{characterName.Trim()}";
	}

	private string GetAverageDpsCacheKey(string characterName, int serverId)
	{
		return GetAverageDpsCacheKey(characterName, serverId, GetAverageDpsBossCodeScope(_currentDungeonContent));
	}

	private string GetAverageDpsCacheKey(string characterName, int serverId, string bossCodeScope)
	{
		return GetCombatScoreCacheKey(characterName, serverId) + ":bosses:" + bossCodeScope;
	}

	private static bool IsSameCharacter(string? leftName, string? leftServerName, string rightName, string? rightServerName)
	{
		if (string.IsNullOrWhiteSpace(leftName) || string.IsNullOrWhiteSpace(rightName))
		{
			return false;
		}
		if (!string.Equals(leftName.Trim(), rightName.Trim(), StringComparison.Ordinal))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(leftServerName) || string.IsNullOrWhiteSpace(rightServerName) || string.Equals(leftServerName, "Unknown", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return string.Equals(leftServerName.Trim(), rightServerName.Trim(), StringComparison.Ordinal);
	}

	internal static string FormatShortServerName(string? serverName)
	{
		if (string.IsNullOrWhiteSpace(serverName))
		{
			return "";
		}
		string text = serverName.Trim();
		if (text.Length > 2)
		{
			return text.Substring(0, 2);
		}
		return text;
	}

	private string FormatDpsCardDps(double dps)
	{
		if (!(dps <= 0.0))
		{
			return FormatDpsCardNumber((long)dps, _dpsCardNumberFormatMode);
		}
		return "--";
	}

	private string FormatDpsCardTotalDamage(long damage)
	{
		return FormatDpsCardNumber(Math.Max(0L, damage), _dpsCardNumberFormatMode);
	}

	private static string FormatDpsCardNumber(long value, DpsCardNumberFormatMode mode)
	{
		return mode switch
		{
			DpsCardNumberFormatMode.Metric => FormatMetricDpsCardNumber(value), 
			DpsCardNumberFormatMode.Korean => FormatKoreanDpsCardNumber(value), 
			_ => value.ToString("N0", KoreanCulture), 
		};
	}

	private static string FormatMetricDpsCardNumber(long value)
	{
		if (value >= 1000000)
		{
			return ((double)value / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
		}
		if (value >= 1000)
		{
			return ((double)value / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
		}
		return value.ToString("N0", KoreanCulture);
	}

	private static string FormatKoreanDpsCardNumber(long value)
	{
		if (value >= 100000000)
		{
			return ((double)value / 100000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "억";
		}
		if (value >= 10000)
		{
			return ((double)value / 10000.0).ToString("0.#", CultureInfo.InvariantCulture) + "만";
		}
		return value.ToString("N0", KoreanCulture);
	}

	private static string FormatCombatScore(int avgDps)
	{
		if (avgDps <= 0)
		{
			return "기록 없음";
		}
		if (avgDps >= 1000000)
		{
			return ((double)avgDps / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
		}
		if (avgDps >= 10000)
		{
			return ((double)avgDps / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
		}
		if (avgDps >= 1000)
		{
			return ((int)Math.Round((double)avgDps / 100.0, MidpointRounding.AwayFromZero) * 100).ToString("N0");
		}
		return avgDps.ToString("N0");
	}

	private static string FormatCombatPower(int combatPower)
	{
		if (combatPower <= 0)
		{
			return "";
		}
		if (combatPower >= 1000000)
		{
			return ((double)combatPower / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M";
		}
		if (combatPower >= 1000)
		{
			return ((double)combatPower / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K";
		}
		return combatPower.ToString("N0");
	}

	private void RememberCombatPower(string characterName, int serverId, int combatPower)
	{
		if (combatPower > 0 && !string.IsNullOrWhiteSpace(characterName) && !characterName.StartsWith("Actor ", StringComparison.Ordinal) && !int.TryParse(characterName, out var _) && serverId > 0)
		{
			string combatScoreCacheKey = GetCombatScoreCacheKey(characterName, serverId);
			lock (_combatScoreCache)
			{
				_packetCombatPowerCache[combatScoreCacheKey] = combatPower;
			}
			_engine.RememberCombatPower(characterName, serverId, combatPower);
		}
	}

	private string GetLookupCombatPowerText(string characterName, int serverId)
	{
		if (!TryGetPacketCombatPowerValue(characterName, serverId, out var combatPower))
		{
			return "대기";
		}
		return combatPower.ToString("N0");
	}

	private bool TryGetPacketCombatPowerText(string characterName, string serverName, out string text)
	{
		text = "";
		if (string.IsNullOrWhiteSpace(characterName) || characterName.StartsWith("Actor ", StringComparison.Ordinal) || int.TryParse(characterName, out var _))
		{
			return false;
		}
		int aion2ServerId = PartyTracker.GetAion2ServerId(serverName);
		if (aion2ServerId <= 0)
		{
			return false;
		}
		if (!TryGetPacketCombatPowerValue(characterName, aion2ServerId, out var combatPower))
		{
			return false;
		}
		text = FormatCombatPower(combatPower);
		return true;
	}

	private bool TryGetPacketCombatPowerValue(string characterName, int serverId, out int combatPower)
	{
		combatPower = 0;
		if (string.IsNullOrWhiteSpace(characterName) || characterName.StartsWith("Actor ", StringComparison.Ordinal) || int.TryParse(characterName, out var _) || serverId <= 0)
		{
			return false;
		}
		string combatScoreCacheKey = GetCombatScoreCacheKey(characterName, serverId);
		lock (_combatScoreCache)
		{
			if (_packetCombatPowerCache.TryGetValue(combatScoreCacheKey, out combatPower) && combatPower > 0)
			{
				return true;
			}
		}
		if (_engine.TryGetCombatPower(characterName, serverId, out combatPower) && combatPower > 0)
		{
			lock (_combatScoreCache)
			{
				_packetCombatPowerCache[combatScoreCacheKey] = combatPower;
			}
			return true;
		}
		combatPower = 0;
		return false;
	}

	private string BuildCharacterApiUrl(string characterName, int serverId, DungeonContentInfo? contentSnapshot = null)
	{
		StringBuilder stringBuilder = new StringBuilder(WebEndpoint.Url("/aion2data/aion2_char.php"));
		stringBuilder.Append("?name=").Append(Uri.EscapeDataString(characterName));
		stringBuilder.Append("&server_id=").Append(serverId.ToString(CultureInfo.InvariantCulture));
		DungeonContentInfo content = contentSnapshot ?? _currentDungeonContent;
		if (_dungeonBossCatalogMap.TryGetBossCodes(content, out int[] bossCodes) && bossCodes.Length != 0)
		{
			stringBuilder.Append("&boss_mob_codes=").Append(Uri.EscapeDataString(string.Join(",", bossCodes)));
		}
		return stringBuilder.ToString();
	}

	private static bool TryReadAverageDps(JsonElement root, JsonElement character, out double avg, out bool isDungeonAverage)
	{
		isDungeonAverage = TryGetPropertyIgnoreCase(root, "avg_dps_scope", out var value) && string.Equals(ReadJsonString(value), "dungeon", StringComparison.OrdinalIgnoreCase);
		if (TryGetPropertyIgnoreCase(root, "avg_dps_context_30", out var value2) && TryReadDouble(value2, out avg))
		{
			return true;
		}
		if (TryGetPropertyIgnoreCase(root, "avg_dps_context_10", out var value3) && TryReadDouble(value3, out avg))
		{
			return true;
		}
		isDungeonAverage = false;
		if (TryGetPropertyIgnoreCase(character, "avg_dps_10", out var value4) && TryReadDouble(value4, out avg))
		{
			return true;
		}
		avg = 0.0;
		return false;
	}

	private static string ReadJsonString(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.String)
		{
			return value.ToString();
		}
		return value.GetString() ?? "";
	}

	private bool TryReadMeterPresence(JsonElement character, out DateTime seenUtc)
	{
		seenUtc = DateTime.MinValue;
		if (!TryGetPropertyIgnoreCase(character, "last_refresh_at", out var value))
		{
			return false;
		}
		string text = ((value.ValueKind == JsonValueKind.String) ? value.GetString() : value.ToString());
		if (string.IsNullOrWhiteSpace(text) || !DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var result))
		{
			return false;
		}
		seenUtc = ((result.Kind == DateTimeKind.Utc) ? result : result.ToLocalTime().ToUniversalTime());
		return true;
	}

	private void RememberMeterPresence(string characterName, int serverId, DateTime seenUtc)
	{
		if (serverId <= 0 || string.IsNullOrWhiteSpace(characterName))
		{
			return;
		}
		string combatScoreCacheKey = GetCombatScoreCacheKey(characterName, serverId);
		lock (_combatScoreCache)
		{
			_meterPresenceCacheUtc[combatScoreCacheKey] = seenUtc;
		}
	}

	private bool IsRecentMeterPresence(string characterName, int serverId)
	{
		if (serverId <= 0 || string.IsNullOrWhiteSpace(characterName))
		{
			return false;
		}
		string combatScoreCacheKey = GetCombatScoreCacheKey(characterName, serverId);
		lock (_combatScoreCache)
		{
			DateTime value;
			return _meterPresenceCacheUtc.TryGetValue(combatScoreCacheKey, out value) && DateTime.UtcNow - value <= MeterPresenceFreshness && value <= DateTime.UtcNow.AddMinutes(2.0);
		}
	}

	private bool ShouldShowMeterUserMarker(string characterName, int serverId)
	{
		if (IsDeveloperUpdatePreviewEnabled())
		{
			return !string.IsNullOrWhiteSpace(characterName);
		}
		return IsRecentMeterPresence(characterName, serverId);
	}

	private bool TryGetLocalPresenceIdentity(out string characterName, out int serverId, out string serverName)
	{
		string candidateName = _engine.LocalPlayerName ?? "";
		serverId = _engine.LocalPlayerServerId;
		serverName = ((serverId > 0) ? PartyTracker.GetAion2ServerName(serverId) : "");
		if (string.IsNullOrWhiteSpace(candidateName))
		{
			candidateName = DpsCards.FirstOrDefault((DpsCardViewModel c) => !string.IsNullOrWhiteSpace(c.CharacterName))?.CharacterName ?? "";
		}
		if (serverId <= 0 && !string.IsNullOrWhiteSpace(candidateName))
		{
			DpsCardViewModel dpsCardViewModel = DpsCards.FirstOrDefault((DpsCardViewModel c) => string.Equals(c.CharacterName, candidateName, StringComparison.Ordinal));
			if (dpsCardViewModel != null)
			{
				serverName = dpsCardViewModel.ServerName;
				serverId = PartyTracker.GetAion2ServerId(serverName);
			}
		}
		if (serverId <= 0 && !string.IsNullOrWhiteSpace(candidateName))
		{
			PartyMemberItem partyMemberItem = PartyMembers.FirstOrDefault((PartyMemberItem p) => string.Equals(p.Name, candidateName, StringComparison.Ordinal));
			if (partyMemberItem != null)
			{
				serverName = partyMemberItem.ServerName;
				serverId = PartyTracker.GetAion2ServerId(serverName);
			}
		}
		if (serverId > 0 && string.IsNullOrWhiteSpace(serverName))
		{
			serverName = PartyTracker.GetAion2ServerName(serverId);
		}
		characterName = candidateName;
		if (!string.IsNullOrWhiteSpace(characterName))
		{
			return serverId > 0;
		}
		return false;
	}

	private void TryQueuePresenceHeartbeat()
	{
		if (!_isLogViewMode && TryGetLocalPresenceIdentity(out string characterName, out int serverId, out string serverName))
		{
			DateTime utcNow = DateTime.UtcNow;
			string combatScoreCacheKey = GetCombatScoreCacheKey(characterName, serverId);
			if (!string.Equals(_lastPresenceHeartbeatKey, combatScoreCacheKey, StringComparison.Ordinal) || !(utcNow - _lastPresenceHeartbeatUtc < MeterPresenceHeartbeatInterval))
			{
				_lastPresenceHeartbeatUtc = utcNow;
				_lastPresenceHeartbeatKey = combatScoreCacheKey;
				SendPresenceHeartbeatAsync(characterName, serverId, serverName);
			}
		}
	}

	private async Task SendPresenceHeartbeatAsync(string characterName, int serverId, string serverName)
	{
		try
		{
			string content = JsonSerializer.Serialize(new
			{
				api_key = "ing_meter_secret_2026",
				name = characterName,
				server_id = serverId,
				server_name = serverName
			});
			using StringContent content2 = new StringContent(content, Encoding.UTF8, "application/json");
			using HttpResponseMessage response = await _partyHttp.PostAsync(WebEndpoint.Url("/aion2data/aion2_presence.php"), content2);
			if (!response.IsSuccessStatusCode)
			{
				return;
			}
			DateTime seenUtc = DateTime.UtcNow;
			RememberMeterPresence(characterName, serverId, seenUtc);
			await base.Dispatcher.InvokeAsync(delegate
			{
				ApplyMeterPresenceToVisibleItems(characterName, serverId, seenUtc);
			});
		}
		catch
		{
		}
	}

	private void ApplyMeterPresenceToVisibleItems(string characterName, int serverId, DateTime seenUtc)
	{
		string aion2ServerName = PartyTracker.GetAion2ServerName(serverId);
		foreach (DpsCardViewModel dpsCard in DpsCards)
		{
			if (IsSameCharacter(dpsCard.CharacterName, dpsCard.ServerName, characterName, aion2ServerName))
			{
				dpsCard.IsMeterUserOnline = DateTime.UtcNow - seenUtc <= MeterPresenceFreshness;
			}
		}
		foreach (PartyMemberItem partyMember in PartyMembers)
		{
			if (IsSameCharacter(partyMember.Name, partyMember.ServerName, characterName, aion2ServerName))
			{
				partyMember.IsMeterUserOnline = DateTime.UtcNow - seenUtc <= MeterPresenceFreshness;
			}
		}
	}

	private void RefreshVisibleMeterPresence()
	{
		foreach (DpsCardViewModel dpsCard in DpsCards)
		{
			int aion2ServerId = PartyTracker.GetAion2ServerId(dpsCard.ServerName);
			bool flag = ShouldShowMeterUserMarker(dpsCard.CharacterName, aion2ServerId);
			if (dpsCard.IsMeterUserOnline != flag)
			{
				dpsCard.IsMeterUserOnline = flag;
			}
		}
		foreach (PartyMemberItem partyMember in PartyMembers)
		{
			int aion2ServerId2 = PartyTracker.GetAion2ServerId(partyMember.ServerName);
			bool flag2 = ShouldShowMeterUserMarker(partyMember.Name, aion2ServerId2);
			if (partyMember.IsMeterUserOnline != flag2)
			{
				partyMember.IsMeterUserOnline = flag2;
			}
		}
	}

	private static bool TryGetNestedProperty(JsonElement root, out JsonElement value, params string[] path)
	{
		value = root;
		foreach (string propertyName in path)
		{
			if (value.ValueKind != JsonValueKind.Object || !TryGetPropertyIgnoreCase(value, propertyName, out value))
			{
				value = default(JsonElement);
				return false;
			}
		}
		return true;
	}

	private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
	{
		if (element.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty item in element.EnumerateObject())
			{
				if (string.Equals(item.Name, propertyName, StringComparison.OrdinalIgnoreCase))
				{
					value = item.Value;
					return true;
				}
			}
		}
		value = default(JsonElement);
		return false;
	}

	private static bool TryReadDouble(JsonElement value, out double number)
	{
		if (value.ValueKind == JsonValueKind.Number)
		{
			return value.TryGetDouble(out number);
		}
		if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number))
		{
			return true;
		}
		number = 0.0;
		return false;
	}

	private void ResetCombatScoreAutoBudget()
	{
		lock (_combatScoreCache)
		{
			_combatScoreAutoRequestedThisSession.Clear();
		}
	}

	private async Task FetchCombatScoreAsync(DpsCardViewModel card, string characterName, string serverName, bool forceRefresh = false)
	{
		if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(serverName) || characterName.StartsWith("Actor ") || int.TryParse(characterName, out var _))
		{
			return;
		}
		if (!CanFetchAverageDpsForSnapshot(GetSnapshotForCurrentFilter()))
		{
			await base.Dispatcher.InvokeAsync(delegate
			{
				ClearAverageDps(card);
			});
			return;
		}
		int requestEpoch = Volatile.Read(in _averageDpsRefreshEpoch);
		int serverId = PartyTracker.GetAion2ServerId(serverName);
		if (serverId <= 0)
		{
			await base.Dispatcher.InvokeAsync(delegate
			{
				card.CombatScore = "";
				card.IsDungeonAverageDps = false;
				card.AverageDpsScopeKey = "";
				card.CombatPower = "";
			});
			return;
		}
		string cacheKey = GetAverageDpsCacheKey(characterName, serverId);
		string cachedScoreText = null;
		bool cachedIsDungeonAverage = false;
		string text;
		string packetCombatPowerText = (TryGetPacketCombatPowerText(characterName, serverName, out text) ? text : "");
		DungeonContentInfo contentSnapshot = await WaitForAverageDpsContentAsync(forceRefresh);
		string requestBossCodeScope = GetAverageDpsBossCodeScope(contentSnapshot);
		cacheKey = GetAverageDpsCacheKey(characterName, serverId, requestBossCodeScope);
		if (IsAverageDpsRefreshStale(requestEpoch))
		{
			return;
		}
		lock (_combatScoreCache)
		{
			int value;
			bool flag = _combatScoreCache.TryGetValue(cacheKey, out value);
			if (flag)
			{
				_combatScoreDungeonScopeCache.TryGetValue(cacheKey, out cachedIsDungeonAverage);
			}
			if (!forceRefresh && flag)
			{
				cachedScoreText = FormatCombatScore(value);
			}
			else
			{
				if (_combatScoreLoading.Contains(cacheKey))
				{
					return;
				}
				_combatScoreLoading.Add(cacheKey);
			}
		}
		if (cachedScoreText == null)
		{
			await base.Dispatcher.InvokeAsync(delegate
			{
				card.CombatScore = "조회 중...";
				card.IsDungeonAverageDps = false;
				card.AverageDpsScopeKey = cacheKey;
			});
			try
			{
				int num = 0;
				try
				{
					int avgInt = 0;
					bool isDungeonAverage = false;
					bool hasResponse = false;
					await _combatScoreRequestGate.WaitAsync();
					try
					{
						await Task.Delay(120);
						string requestUri = BuildCharacterApiUrl(characterName, serverId, contentSnapshot);
						HttpResponseMessage httpResponseMessage = await _partyHttp.GetAsync(requestUri);
						if (IsAverageDpsRefreshStale(requestEpoch))
						{
							return;
						}
						if (IsAverageDpsResponseStale(requestBossCodeScope))
						{
							await base.Dispatcher.InvokeAsync(delegate
							{
								card.CombatScore = "조회 중...";
								card.IsDungeonAverageDps = false;
								card.AverageDpsScopeKey = cacheKey;
							});
							base.Dispatcher.BeginInvoke((Action)delegate
							{
								FetchCombatScoreAsync(card, characterName, serverName, forceRefresh: true);
							});
							return;
						}
						if (httpResponseMessage.IsSuccessStatusCode)
						{
							hasResponse = true;
							using JsonDocument doc = JsonDocument.Parse(await httpResponseMessage.Content.ReadAsStringAsync());
							if (doc.RootElement.TryGetProperty("success", out var value2) && value2.GetBoolean() && doc.RootElement.TryGetProperty("character", out var value3))
							{
								if (TryReadAverageDps(doc.RootElement, value3, out var avg, out var isDungeonAverage2))
								{
									avgInt = (int)avg;
									isDungeonAverage = isDungeonAverage2;
								}
								if (TryReadMeterPresence(value3, out var seenUtc))
								{
									RememberMeterPresence(characterName, serverId, seenUtc);
									await base.Dispatcher.InvokeAsync(() => card.IsMeterUserOnline = ShouldShowMeterUserMarker(characterName, serverId));
								}
							}
						}
					}
					finally
					{
						_combatScoreRequestGate.Release();
					}
					if (!hasResponse)
					{
						await base.Dispatcher.InvokeAsync(delegate
						{
							card.CombatScore = "";
							card.IsDungeonAverageDps = false;
							card.AverageDpsScopeKey = cacheKey;
							card.CombatPower = (TryGetPacketCombatPowerText(characterName, serverName, out string text2) ? text2 : "");
						});
					}
					else
					{
						lock (_combatScoreCache)
						{
							_combatScoreCache[cacheKey] = avgInt;
							_combatScoreDungeonScopeCache[cacheKey] = isDungeonAverage;
						}
						await base.Dispatcher.InvokeAsync(delegate
						{
							card.CombatScore = FormatCombatScore(avgInt);
							card.IsDungeonAverageDps = isDungeonAverage;
							card.AverageDpsScopeKey = cacheKey;
							card.CombatPower = (TryGetPacketCombatPowerText(characterName, serverName, out string text2) ? text2 : "");
						});
					}
				}
				catch
				{
					num = 1;
				}
				if (num == 1 && !IsAverageDpsRefreshStale(requestEpoch))
				{
					await base.Dispatcher.InvokeAsync(delegate
					{
						card.CombatScore = "";
						card.IsDungeonAverageDps = false;
						card.AverageDpsScopeKey = cacheKey;
						card.CombatPower = (TryGetPacketCombatPowerText(characterName, serverName, out string text2) ? text2 : "");
					});
				}
				return;
			}
			finally
			{
				lock (_combatScoreCache)
				{
					_combatScoreLoading.Remove(cacheKey);
				}
			}
		}
		await base.Dispatcher.InvokeAsync(delegate
		{
			card.CombatScore = cachedScoreText;
			card.IsDungeonAverageDps = cachedIsDungeonAverage;
			card.AverageDpsScopeKey = cacheKey;
			card.CombatPower = packetCombatPowerText;
			card.IsMeterUserOnline = ShouldShowMeterUserMarker(characterName, serverId);
		});
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/mainwindow.xaml", UriKind.Relative);
			System.Windows.Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	internal Delegate _CreateDelegate(Type delegateType, string handler)
	{
		return Delegate.CreateDelegate(delegateType, this, handler);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 2:
			rootBorder = (Border)target;
			rootBorder.MouseLeftButtonDown += RootBorder_MouseLeftButtonDown;
			break;
		case 3:
			titleBar = (Border)target;
			break;
		case 4:
			titleBarSeparator = (Border)target;
			break;
		case 5:
			toolBar = (Grid)target;
			break;
		case 6:
			colDpsToolbar = (ColumnDefinition)target;
			break;
		case 7:
			colSplitterToolbar = (ColumnDefinition)target;
			break;
		case 8:
			colDetailToolbar = (ColumnDefinition)target;
			break;
		case 9:
			headerLeftControls = (StackPanel)target;
			headerLeftControls.PreviewMouseLeftButtonDown += WindowDragHandle_PreviewMouseLeftButtonDown;
			headerLeftControls.PreviewMouseMove += WindowDragHandle_PreviewMouseMove;
			headerLeftControls.PreviewMouseLeftButtonUp += WindowDragHandle_PreviewMouseLeftButtonUp;
			break;
		case 10:
			statusHeaderHitArea = (StackPanel)target;
			break;
		case 11:
			statusHeaderIconHost = (Border)target;
			break;
		case 12:
			imgStatusHeaderIcon = (System.Windows.Controls.Image)target;
			break;
		case 13:
			elStatusHeader = (Ellipse)target;
			break;
		case 14:
			bdMainViewModeHost = (Border)target;
			break;
		case 15:
			mainViewAutoFrame = (System.Windows.Shapes.Rectangle)target;
			break;
		case 16:
			chkAutoMainView = (System.Windows.Controls.CheckBox)target;
			chkAutoMainView.Click += AutoMainView_Click;
			break;
		case 17:
			mainViewModeDivider = (Border)target;
			break;
		case 18:
			btnMainViewSwap = (System.Windows.Controls.Button)target;
			btnMainViewSwap.Click += MainViewSwap_Click;
			break;
		case 19:
			btnUpdateBadge = (System.Windows.Controls.Button)target;
			btnUpdateBadge.Click += BtnUpdate_Click;
			break;
		case 20:
			updateBadgeGlow = (DropShadowEffect)target;
			break;
		case 21:
			pathUpdateArrow = (System.Windows.Shapes.Path)target;
			break;
		case 22:
			pathUpdateTray = (System.Windows.Shapes.Path)target;
			break;
		case 23:
			txtUpdateProgress = (TextBlock)target;
			break;
		case 24:
			headerActionControls = (StackPanel)target;
			headerActionControls.PreviewMouseLeftButtonDown += WindowDragHandle_PreviewMouseLeftButtonDown;
			headerActionControls.PreviewMouseMove += WindowDragHandle_PreviewMouseMove;
			headerActionControls.PreviewMouseLeftButtonUp += WindowDragHandle_PreviewMouseLeftButtonUp;
			break;
		case 25:
			btnPause = (System.Windows.Controls.Button)target;
			btnPause.Click += BtnPause_Click;
			break;
		case 26:
			pathPauseIcon = (System.Windows.Shapes.Path)target;
			break;
		case 27:
			btnPrimaryAction = (System.Windows.Controls.Button)target;
			btnPrimaryAction.Click += BtnPrimaryAction_Click;
			break;
		case 28:
			pathPrimaryActionIcon = (System.Windows.Shapes.Path)target;
			break;
		case 29:
			chkHudMode = (ToggleButton)target;
			chkHudMode.Click += BtnHudMode_Click;
			break;
		case 30:
			btnStatusSettings = (System.Windows.Controls.Button)target;
			btnStatusSettings.Click += BtnSettingsMenu_Click;
			break;
		case 31:
			pathSettingsMenuGear = (System.Windows.Shapes.Path)target;
			break;
		case 32:
			btnTopmost = (System.Windows.Controls.Button)target;
			btnTopmost.Click += BtnTopmost_Click;
			break;
		case 33:
			headerWindowControls = (StackPanel)target;
			headerWindowControls.PreviewMouseLeftButtonDown += WindowDragHandle_PreviewMouseLeftButtonDown;
			headerWindowControls.PreviewMouseMove += WindowDragHandle_PreviewMouseMove;
			headerWindowControls.PreviewMouseLeftButtonUp += WindowDragHandle_PreviewMouseLeftButtonUp;
			break;
		case 34:
			btnMaximize = (System.Windows.Controls.Button)target;
			btnMaximize.Click += BtnMaximize_Click;
			break;
		case 35:
			pathMaximizeIcon = (System.Windows.Shapes.Path)target;
			break;
		case 36:
			btnLocalEncounterHistory = (System.Windows.Controls.Button)target;
			btnLocalEncounterHistory.Click += BtnLocalEncounterHistory_Click;
			break;
		case 37:
			btnClose = (System.Windows.Controls.Button)target;
			btnClose.Click += BtnClose_Click;
			break;
		case 38:
			mainGrid = (Grid)target;
			break;
		case 39:
			colDps = (ColumnDefinition)target;
			break;
		case 40:
			colSplitter = (ColumnDefinition)target;
			break;
		case 41:
			colDetail = (ColumnDefinition)target;
			break;
		case 42:
			sideMenu = (Grid)target;
			break;
		case 43:
			btnDetail = (System.Windows.Controls.Button)target;
			btnDetail.Click += BtnDetailToggle_Click;
			break;
		case 44:
			btnParty = (System.Windows.Controls.Button)target;
			btnParty.Click += BtnPartyToggle_Click;
			break;
		case 45:
			chkShowUnknown = (ToggleButton)target;
			chkShowUnknown.Click += BtnShowUnknown_Click;
			break;
		case 46:
			chkHideNickname = (ToggleButton)target;
			chkHideNickname.Click += BtnHideNickname_Click;
			break;
		case 47:
			btnLoadLog = (System.Windows.Controls.Button)target;
			btnLoadLog.Click += BtnLoadLog_Click;
			break;
		case 48:
			btnHome = (System.Windows.Controls.Button)target;
			btnHome.Click += BtnHome_Click;
			break;
		case 49:
			btnSettingsSide = (System.Windows.Controls.Button)target;
			btnSettingsSide.Click += BtnSettings_Click;
			break;
		case 50:
			elStatus = (Ellipse)target;
			break;
		case 51:
			popUpload = (Popup)target;
			break;
		case 52:
			bdUploadPopup = (Border)target;
			break;
		case 53:
			popApplicant = (Popup)target;
			break;
		case 54:
			stackApplicants = (StackPanel)target;
			break;
		case 55:
			mainContentBorder = (Border)target;
			break;
		case 56:
			bloomWindowFrame = (BloomDpsCardFrame)target;
			break;
		case 57:
			crayonBackdrop = (CrayonPaperBackdrop)target;
			break;
		case 58:
			crayonWindowFrame = (CrayonDoodleFrame)target;
			break;
		case 59:
			filterPanel = (Grid)target;
			break;
		case 60:
			cmbFilterClass = (System.Windows.Controls.ComboBox)target;
			cmbFilterClass.SelectionChanged += Filter_SelectionChanged;
			break;
		case 61:
			cmbFilterTarget = (System.Windows.Controls.ComboBox)target;
			cmbFilterTarget.SelectionChanged += Filter_SelectionChanged;
			break;
		case 62:
			bdLookupDungeonInfo = (Border)target;
			break;
		case 63:
			txtLookupDungeonCategory = (TextBlock)target;
			break;
		case 64:
			bdLookupDungeonDetail = (Border)target;
			break;
		case 65:
			txtLookupDungeonDetail = (TextBlock)target;
			break;
		case 66:
			txtLookupDungeonName = (TextBlock)target;
			break;
		case 67:
			borderTopTarget = (Border)target;
			break;
		case 68:
			topTargetScale = (ScaleTransform)target;
			break;
		case 69:
			topTargetRootGrid = (Grid)target;
			break;
		case 70:
			topTargetContentRow = (RowDefinition)target;
			break;
		case 71:
			topTargetHpRow = (RowDefinition)target;
			break;
		case 72:
			colTargetIcon = (ColumnDefinition)target;
			break;
		case 73:
			bossBloomFrame = (BloomDpsCardFrame)target;
			break;
		case 74:
			bossCrayonFrame = (CrayonDoodleFrame)target;
			break;
		case 75:
			bossNeonFrameHost = (Canvas)target;
			break;
		case 76:
			bossNeonFrame = (NeonDpsCardFrame)target;
			break;
		case 77:
			bossAbyssFrame = (AbyssDpsCardFrame)target;
			break;
		case 78:
			bdTargetIcon = (Border)target;
			break;
		case 79:
			topTargetInfoGrid = (Grid)target;
			break;
		case 80:
			topTargetNameTextRow = (RowDefinition)target;
			break;
		case 81:
			topTargetHpTextRow = (RowDefinition)target;
			break;
		case 82:
			topTargetNeonHpRow = (RowDefinition)target;
			break;
		case 83:
			topTargetNameLineGrid = (Grid)target;
			break;
		case 84:
			txtTopTargetName = (TextBlock)target;
			break;
		case 85:
			topTargetHpSummaryStack = (StackPanel)target;
			break;
		case 86:
			txtTopTargetHpValue = (TextBlock)target;
			break;
		case 87:
			txtTopTargetHpPercent = (TextBlock)target;
			break;
		case 88:
			txtTopTargetDuration = (TextBlock)target;
			break;
		case 89:
			topTargetInlineMetricsStack = (StackPanel)target;
			break;
		case 90:
			txtTopTargetInlineDamage = (TextBlock)target;
			break;
		case 91:
			txtTopTargetInlineDuration = (TextBlock)target;
			break;
		case 92:
			txtTopTargetType = (TextBlock)target;
			break;
		case 93:
			topTargetDamageStack = (StackPanel)target;
			break;
		case 94:
			txtTopTargetDamageLabel = (TextBlock)target;
			break;
		case 95:
			txtTopTargetDamage = (TextBlock)target;
			break;
		case 96:
			txtTopTargetHits = (TextBlock)target;
			break;
		case 97:
			neonBossHpBar = (NeonBossHpBar)target;
			break;
		case 98:
			bossHpTrack = (Border)target;
			bossHpTrack.SizeChanged += BossHpTrack_SizeChanged;
			break;
		case 99:
			bossHpFill = (Border)target;
			break;
		case 100:
			lstDps = (System.Windows.Controls.ListBox)target;
			lstDps.SelectionChanged += LstDps_SelectionChanged;
			lstDps.PreviewMouseDown += LstDps_PreviewMouseDown;
			lstDps.MouseDoubleClick += LstDps_MouseDoubleClick;
			lstDps.PreviewKeyDown += LstDps_PreviewKeyDown;
			lstDps.PreviewMouseRightButtonDown += LstDps_PreviewMouseRightButtonDown;
			lstDps.ContextMenuOpening += LstDps_ContextMenuOpening;
			lstDps.PreviewMouseWheel += LstDps_PreviewMouseWheel;
			break;
		case 101:
			lstLookup = (System.Windows.Controls.ListBox)target;
			lstLookup.PreviewMouseWheel += LstDps_PreviewMouseWheel;
			break;
		case 102:
			txtLookupEmpty = (TextBlock)target;
			break;
		case 103:
			hudLeftControls = (StackPanel)target;
			hudLeftControls.PreviewMouseLeftButtonDown += WindowDragHandle_PreviewMouseLeftButtonDown;
			hudLeftControls.PreviewMouseMove += WindowDragHandle_PreviewMouseMove;
			hudLeftControls.PreviewMouseLeftButtonUp += WindowDragHandle_PreviewMouseLeftButtonUp;
			break;
		case 104:
			hudBrandIcon = (Grid)target;
			break;
		case 105:
			btnUpdateBadgeHud = (System.Windows.Controls.Button)target;
			btnUpdateBadgeHud.Click += BtnUpdate_Click;
			break;
		case 106:
			updateBadgeGlowHud = (DropShadowEffect)target;
			break;
		case 107:
			pathUpdateArrowHud = (System.Windows.Shapes.Path)target;
			break;
		case 108:
			pathUpdateTrayHud = (System.Windows.Shapes.Path)target;
			break;
		case 109:
			txtUpdateProgressHud = (TextBlock)target;
			break;
		case 110:
			bdHudMainViewModeHost = (Border)target;
			break;
		case 111:
			hudMainViewAutoFrame = (System.Windows.Shapes.Rectangle)target;
			break;
		case 112:
			chkAutoMainViewHud = (System.Windows.Controls.CheckBox)target;
			chkAutoMainViewHud.Click += AutoMainView_Click;
			break;
		case 113:
			btnMainViewSwapHud = (System.Windows.Controls.Button)target;
			btnMainViewSwapHud.Click += MainViewSwap_Click;
			break;
		case 114:
			hudControls = (StackPanel)target;
			hudControls.PreviewMouseLeftButtonDown += WindowDragHandle_PreviewMouseLeftButtonDown;
			hudControls.PreviewMouseMove += WindowDragHandle_PreviewMouseMove;
			hudControls.PreviewMouseLeftButtonUp += WindowDragHandle_PreviewMouseLeftButtonUp;
			break;
		case 115:
			btnResetHud = (System.Windows.Controls.Button)target;
			btnResetHud.Click += BtnPrimaryAction_Click;
			break;
		case 116:
			pathResetHudIcon = (System.Windows.Shapes.Path)target;
			break;
		case 117:
			btnExitHud = (System.Windows.Controls.Button)target;
			btnExitHud.Click += BtnExitHud_Click;
			break;
		case 118:
			btnSettingsHud = (System.Windows.Controls.Button)target;
			btnSettingsHud.Click += BtnSettingsMenu_Click;
			break;
		case 119:
			pathSettingsHudGear = (System.Windows.Shapes.Path)target;
			break;
		case 120:
			btnLocalEncounterHistoryHud = (System.Windows.Controls.Button)target;
			btnLocalEncounterHistoryHud.Click += BtnLocalEncounterHistory_Click;
			break;
		case 121:
			btnClickThroughHud = (System.Windows.Controls.Button)target;
			btnClickThroughHud.Click += BtnHudClickThrough_Click;
			break;
		case 122:
			pathHudClickThroughIcon = (System.Windows.Shapes.Path)target;
			break;
		case 123:
			btnExitAppHud = (System.Windows.Controls.Button)target;
			btnExitAppHud.Click += BtnExitAppHud_Click;
			break;
		case 124:
			popLocalEncounterHistory = (Popup)target;
			break;
		case 125:
			bdLocalEncounterHistoryPanel = (Border)target;
			bdLocalEncounterHistoryPanel.PreviewMouseLeftButtonDown += LocalEncounterHistoryPanel_PreviewMouseLeftButtonDown;
			bdLocalEncounterHistoryPanel.MouseLeftButtonDown += LocalEncounterHistoryPanel_MouseLeftButtonDown;
			break;
		case 126:
			((Grid)target).MouseLeftButtonDown += LocalEncounterHistoryTitleBar_MouseLeftButtonDown;
			break;
		case 127:
			txtLocalEncounterCount = (TextBlock)target;
			break;
		case 128:
			((System.Windows.Controls.Button)target).Click += LocalEncounterHistoryClose_Click;
			break;
		case 129:
			bdLocalEncounterBossSearch = (Border)target;
			bdLocalEncounterBossSearch.PreviewMouseLeftButtonDown += LocalEncounterSearch_PreviewMouseLeftButtonDown;
			bdLocalEncounterBossSearch.MouseEnter += LocalEncounterSearch_MouseEnter;
			bdLocalEncounterBossSearch.MouseLeave += LocalEncounterSearch_MouseLeave;
			break;
		case 130:
			txtLocalEncounterBossSearchInput = (System.Windows.Controls.TextBox)target;
			break;
		case 131:
			txtLocalEncounterBossSearchPlaceholder = (TextBlock)target;
			break;
		case 132:
			btnLocalEncounterBossSearchClear = (System.Windows.Controls.Button)target;
			btnLocalEncounterBossSearchClear.Click += LocalEncounterBossSearchClear_Click;
			break;
		case 133:
			lstLocalEncounterBossSuggestions = (System.Windows.Controls.ListBox)target;
			lstLocalEncounterBossSuggestions.SelectionChanged += LocalEncounterBossSuggestion_SelectionChanged;
			break;
		case 134:
			lstLocalEncounterHistory = (System.Windows.Controls.ListBox)target;
			lstLocalEncounterHistory.SelectionChanged += LocalEncounterHistory_SelectionChanged;
			lstLocalEncounterHistory.PreviewMouseLeftButtonDown += LocalEncounterHistory_PreviewMouseLeftButtonDown;
			lstLocalEncounterHistory.PreviewKeyDown += LocalEncounterHistory_PreviewKeyDown;
			lstLocalEncounterHistory.PreviewMouseWheel += LocalEncounterHistory_PreviewMouseWheel;
			break;
		case 136:
			txtLocalEncounterEmpty = (TextBlock)target;
			break;
		case 137:
			thumbLocalEncounterHistoryResize = (Thumb)target;
			thumbLocalEncounterHistoryResize.DragDelta += LocalEncounterHistoryResizeThumb_DragDelta;
			break;
		case 138:
			borderParty = (Border)target;
			break;
		case 139:
			((System.Windows.Controls.Button)target).Click += BtnPartyToggle_Click;
			break;
		case 140:
			lstParty = (System.Windows.Controls.ListBox)target;
			break;
		case 141:
			locatePulseOverlay = (Border)target;
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
		case 1:
			((System.Windows.Controls.Button)target).Click += LookupCharacterLink_Click;
			break;
		case 135:
			((System.Windows.Controls.Button)target).Click += LocalEncounterReplay_Click;
			break;
		}
	}
}
