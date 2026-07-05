using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using INGMeter.Core;

namespace INGMeter.App;

public class PartyMemberItem : INotifyPropertyChanged
{
	private string _name = "";

	private JobClass _job;

	private string _displayName = "";

	private string _serverName = "";

	private MeterDisplayPreset _displayPreset = MeterDisplayPreset.Standard;

	private bool _isInspectedCharacter;

	private bool _isTemporaryLookup;

	private int _partySlot = int.MaxValue;

	private string _avgDps10Text = "대기 중...";

	private bool _isDungeonAverageDps;

	private string _combatPowerText = "대기";

	private bool _isMeterUserOnline;

	private bool _showLookupSkillDisplay = true;

	private string _stigmaSummaryText = "스티그마 대기";

	private string _sourceText = "파티";

	private bool _isApplicant;

	private MeterFontWeightMode _fontWeightMode = MeterFontWeightMode.Normal;

	private double _uiScale = 1.0;

	private double _visualLayoutScale = 1.0;

	private double _visualTextScale = 1.0;

	private int _visualFontSizeDelta;

	public string Name
	{
		get
		{
			return _name;
		}
		set
		{
			_name = value;
			OnPropertyChanged("Name");
		}
	}

	public JobClass Job
	{
		get
		{
			return _job;
		}
		set
		{
			_job = value;
			OnPropertyChanged("Job");
			OnPropertyChanged("JobName");
			OnPropertyChanged("JobIconPath");
		}
	}

	public string? JobIconPath => JobClassIconPaths.For(Job);

	public string JobName => Job switch
	{
		JobClass.Gladiator => "검성", 
		JobClass.Templar => "수호", 
		JobClass.Assassin => "살성", 
		JobClass.Ranger => "궁성", 
		JobClass.Sorcerer => "마도", 
		JobClass.Spiritmaster => "정령", 
		JobClass.Cleric => "치유", 
		JobClass.Chanter => "호법", 
		JobClass.Brawler => "권성", 
		_ => "?", 
	};

	public string DisplayName
	{
		get
		{
			return _displayName;
		}
		set
		{
			_displayName = value;
			OnPropertyChanged("DisplayName");
		}
	}

	public string ServerName
	{
		get
		{
			return _serverName;
		}
		set
		{
			_serverName = value;
			OnPropertyChanged("ServerName");
			OnPropertyChanged("DisplayServerName");
		}
	}

	public string DisplayServerName => MainWindow.FormatShortServerName(ServerName);

	public MeterDisplayPreset DisplayPreset
	{
		get
		{
			return _displayPreset;
		}
		set
		{
			if (_displayPreset != value)
			{
				_displayPreset = value;
				OnPropertyChanged("DisplayPreset");
				OnVisualMetricsChanged();
			}
		}
	}

	public bool IsInspectedCharacter
	{
		get
		{
			return _isInspectedCharacter;
		}
		set
		{
			_isInspectedCharacter = value;
			OnPropertyChanged("IsInspectedCharacter");
		}
	}

	public bool IsTemporaryLookup
	{
		get
		{
			return _isTemporaryLookup;
		}
		set
		{
			_isTemporaryLookup = value;
			OnPropertyChanged("IsTemporaryLookup");
			OnPropertyChanged("LookupAccentBrush");
		}
	}

	public DateTime LookupExpiresAtUtc { get; set; } = DateTime.MaxValue;

	public string LookupAccentBrush
	{
		get
		{
			if (!IsTemporaryLookup)
			{
				return "#2f80ed";
			}
			return "#f59e0b";
		}
	}

	public int PartySlot
	{
		get
		{
			return _partySlot;
		}
		set
		{
			_partySlot = value;
			OnPropertyChanged("PartySlot");
		}
	}

	public string AvgDps10Text
	{
		get
		{
			return _avgDps10Text;
		}
		set
		{
			_avgDps10Text = value;
			OnPropertyChanged("AvgDps10Text");
			OnPropertyChanged("AvgDps10CompactText");
			OnPropertyChanged("IsAvgDps10Visible");
		}
	}

	public string AvgDps10CompactText => FormatCompactMetricText(AvgDps10Text);

	public bool IsAvgDps10Visible => IsAverageDpsVisible(AvgDps10Text);

	public bool IsDungeonAverageDps
	{
		get
		{
			return _isDungeonAverageDps;
		}
		set
		{
			if (_isDungeonAverageDps != value)
			{
				_isDungeonAverageDps = value;
				OnPropertyChanged("IsDungeonAverageDps");
				OnPropertyChanged("AverageDpsTooltip");
			}
		}
	}

	public string AverageDpsTooltip
	{
		get
		{
			if (!IsDungeonAverageDps)
			{
				return "전체 최근 평균 DPS";
			}
			return "해당 던전 최근 평균 DPS";
		}
	}

	public string CombatPowerText
	{
		get
		{
			return _combatPowerText;
		}
		set
		{
			_combatPowerText = value;
			OnPropertyChanged("CombatPowerText");
			OnPropertyChanged("CombatPowerCompactText");
		}
	}

	public string CombatPowerCompactText => FormatCompactMetricText(CombatPowerText);

	public bool IsMeterUserOnline
	{
		get
		{
			return _isMeterUserOnline;
		}
		set
		{
			if (_isMeterUserOnline != value)
			{
				_isMeterUserOnline = value;
				OnPropertyChanged("IsMeterUserOnline");
			}
		}
	}

	public bool ShowLookupSkillDisplay
	{
		get
		{
			return _showLookupSkillDisplay;
		}
		set
		{
			if (_showLookupSkillDisplay != value)
			{
				_showLookupSkillDisplay = value;
				OnPropertyChanged("ShowLookupSkillDisplay");
			}
		}
	}

	public string StigmaSummaryText
	{
		get
		{
			return _stigmaSummaryText;
		}
		set
		{
			_stigmaSummaryText = value;
			OnPropertyChanged("StigmaSummaryText");
		}
	}

	public ObservableCollection<StigmaBadgeItem> StigmaBadges { get; } = new ObservableCollection<StigmaBadgeItem>();

	public int StigmaBadgeCount => StigmaBadges.Count;

	public bool HasStigmaBadges => StigmaBadges.Count > 0;

	public string SourceText
	{
		get
		{
			return _sourceText;
		}
		set
		{
			_sourceText = value;
			OnPropertyChanged("SourceText");
		}
	}

	public bool IsApplicant
	{
		get
		{
			return _isApplicant;
		}
		set
		{
			_isApplicant = value;
			OnPropertyChanged("IsApplicant");
		}
	}

	public MeterFontWeightMode FontWeightMode
	{
		get
		{
			return _fontWeightMode;
		}
		set
		{
			if (_fontWeightMode != value)
			{
				_fontWeightMode = value;
				OnPropertyChanged("TextFontWeight");
				OnPropertyChanged("StrongTextFontWeight");
			}
		}
	}

	public FontWeight TextFontWeight => MeterFontWeights.Text(FontWeightMode);

	public FontWeight StrongTextFontWeight => MeterFontWeights.Strong(FontWeightMode);

	public double UiScale
	{
		get
		{
			return _uiScale;
		}
		set
		{
			_uiScale = value;
			OnPropertyChanged("UiScale");
		}
	}

	private double TextScale => Math.Clamp(_visualTextScale, 0.6, 1.4);

	private double CardDensity => MeterVisualScale.CardDensity(TextScale);

	private double CardWidthDensity => MeterVisualScale.CardWidthDensity(TextScale);

	private bool IsMinimal => DisplayPreset == MeterDisplayPreset.Minimal;

	public Thickness LookupCardPadding
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(8.0, 4.0, 8.0, 4.0);
			}
			return ThickDense(5.0, 2.0, 5.0, 2.0);
		}
	}

	public double LookupAccentWidth => DimWide(3.0);

	public Thickness LookupAccentMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(-8.0, -4.0, 0.0, -4.0);
			}
			return ThickDense(-5.0, -2.0, 0.0, -2.0);
		}
	}

	public Thickness LookupInnerMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(4.0, 0.0, 0.0, 0.0);
			}
			return ThickDense(3.0, 0.0, 0.0, 0.0);
		}
	}

	public double LookupHeaderHeight => DimDense(IsMinimal ? 20 : 22);

	public Thickness LookupHeaderMetricsMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(0.0, 0.0, 8.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 3.0, 0.0);
		}
	}

	public double LookupJobIconHostSize => DimDense(IsMinimal ? 18 : 20);

	public double LookupJobIconSize => DimDense(IsMinimal ? 15 : 17);

	public CornerRadius LookupJobIconCornerRadius => MeterVisualScale.Radius(5.0);

	public double LookupJobFallbackFontSize => Font(IsMinimal ? 8 : 9);

	public double LookupServerFontSize => Font(10.0);

	public double LookupServerMaxWidth => DimWide(52.0);

	public Thickness LookupServerMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(0.0, 0.0, 5.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 3.0, 0.0);
		}
	}

	public double LookupNameFontSize => Font(IsMinimal ? 12 : 13);

	public double LookupNameLineHeight => LookupNameFontSize + 1.0;

	public Thickness LookupNameMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(0.0, 0.0, 5.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 3.0, 0.0);
		}
	}

	public Thickness LookupLinkButtonMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(0.0, 0.0, 4.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 2.0, 0.0);
		}
	}

	public Thickness LookupMetricBadgePadding
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(4.0, 0.0, 4.0, 0.0);
			}
			return ThickDense(3.0, 0.0, 4.0, 0.0);
		}
	}

	public double LookupMetricBadgeMinWidth => DimWide(IsMinimal ? 42 : 48);

	public Thickness LookupCombatPowerPadding => LookupMetricBadgePadding;

	public double LookupCombatPowerMinWidth => LookupMetricBadgeMinWidth;

	public double LookupMetricFontSize => LookupAvgValueFontSize;

	public double LookupMetricIconSize => Dim(LookupMetricFontSize + 1.0);

	public Thickness LookupMetricIconMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(0.0, 0.0, 3.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 2.0, 0.0);
		}
	}

	public double LookupMetricLineHeight => LookupMetricFontSize + 1.0;

	public Thickness LookupAvgPadding => LookupMetricBadgePadding;

	public double LookupAvgMinWidth => LookupMetricBadgeMinWidth;

	public double LookupAvgLabelFontSize => Font(IsMinimal ? 8 : 9);

	public double LookupAvgLabelLineHeight => LookupAvgLabelFontSize + 1.0;

	public Thickness LookupAvgLabelMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(0.0, 0.0, 4.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 3.0, 0.0);
		}
	}

	public double LookupAvgValueFontSize => Font(IsMinimal ? 10 : 11);

	public double LookupAvgValueLineHeight => LookupAvgValueFontSize + 1.0;

	public double LookupMetricBadgeHeight => Dim(Math.Max(LookupMetricLineHeight, LookupAvgValueLineHeight) + (double)(IsMinimal ? 3 : 3));

	public Thickness LookupStigmaRowMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(0.0, 2.0, 0.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 0.0, 0.0);
		}
	}

	public double LookupStigmaRowHeight => DimDense(IsMinimal ? 16 : 18);

	public double LookupStigmaFontSize => Font(IsMinimal ? 9 : 10);

	public double LookupStigmaBadgeFontSize => Font(IsMinimal ? 8 : 9);

	public double LookupStigmaBadgeLineHeight => LookupStigmaBadgeFontSize + 1.0;

	public Thickness LookupStigmaBadgePadding
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(4.0, 0.0, 4.0, 0.0);
			}
			return ThickDense(3.0, 0.0, 3.0, 0.0);
		}
	}

	public Thickness LookupStigmaBadgeMargin
	{
		get
		{
			if (!IsMinimal)
			{
				return ThickDense(0.0, 0.0, 4.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 3.0, 0.0);
		}
	}

	public double LookupStigmaMarqueeSpeed
	{
		get
		{
			if (!IsMinimal)
			{
				return 12.0;
			}
			return 10.0;
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public void SetStigmaStatus(string text)
	{
		StigmaBadges.Clear();
		StigmaSummaryText = text;
		OnPropertyChanged("StigmaBadgeCount");
		OnPropertyChanged("HasStigmaBadges");
	}

	public void SetStigmaBadges(IEnumerable<StigmaBadgeItem> badges, string summary)
	{
		StigmaBadges.Clear();
		foreach (StigmaBadgeItem badge in badges)
		{
			StigmaBadges.Add(badge);
		}
		StigmaSummaryText = summary;
		OnPropertyChanged("StigmaBadgeCount");
		OnPropertyChanged("HasStigmaBadges");
	}

	public void SetVisualScale(double layoutScale, double textScale, int fontSizeDelta)
	{
		layoutScale = Math.Clamp(layoutScale, 0.75, 1.7);
		textScale = Math.Clamp(textScale, 0.6, 1.4);
		fontSizeDelta = Math.Clamp(fontSizeDelta, -3, 8);
		if (!(Math.Abs(_visualLayoutScale - layoutScale) < 0.01) || !(Math.Abs(_visualTextScale - textScale) < 0.01) || _visualFontSizeDelta != fontSizeDelta)
		{
			_visualLayoutScale = layoutScale;
			_visualTextScale = textScale;
			_visualFontSizeDelta = fontSizeDelta;
			OnVisualMetricsChanged();
		}
	}

	private double Dim(double value)
	{
		return MeterVisualScale.Dimension(value, _visualLayoutScale);
	}

	private double DimDense(double value)
	{
		return MeterVisualScale.Dimension(value * CardDensity, _visualLayoutScale);
	}

	private double DimWide(double value)
	{
		return MeterVisualScale.Dimension(value * CardWidthDensity, _visualLayoutScale);
	}

	private double Font(double value)
	{
		return MeterVisualScale.Font(value, TextScale, _visualFontSizeDelta);
	}

	private Thickness Thick(double left, double top, double right, double bottom)
	{
		return MeterVisualScale.Thickness(left, top, right, bottom, _visualLayoutScale);
	}

	private Thickness ThickDense(double left, double top, double right, double bottom)
	{
		return MeterVisualScale.Thickness(left * CardWidthDensity, top * CardDensity, right * CardWidthDensity, bottom * CardDensity, _visualLayoutScale);
	}

	private void OnVisualMetricsChanged()
	{
		string[] array = new string[41]
		{
			"LookupCardPadding", "LookupAccentWidth", "LookupAccentMargin", "LookupInnerMargin", "LookupHeaderHeight", "LookupHeaderMetricsMargin", "LookupJobIconHostSize", "LookupJobIconSize", "LookupJobIconCornerRadius", "LookupJobFallbackFontSize",
			"LookupServerFontSize", "LookupServerMaxWidth", "LookupServerMargin", "LookupNameFontSize", "LookupNameLineHeight", "LookupNameMargin", "LookupLinkButtonMargin", "LookupMetricBadgePadding", "LookupMetricBadgeMinWidth", "LookupCombatPowerPadding",
			"LookupCombatPowerMinWidth", "LookupMetricIconSize", "LookupMetricIconMargin", "LookupMetricFontSize", "LookupMetricLineHeight", "LookupAvgPadding", "LookupAvgMinWidth", "LookupAvgLabelFontSize", "LookupAvgLabelLineHeight", "LookupAvgLabelMargin",
			"LookupAvgValueFontSize", "LookupAvgValueLineHeight", "LookupMetricBadgeHeight", "LookupStigmaFontSize", "LookupStigmaBadgeFontSize", "LookupStigmaBadgePadding", "LookupStigmaBadgeMargin", "LookupStigmaRowMargin", "LookupStigmaRowHeight", "LookupStigmaMarqueeSpeed",
			"LookupStigmaBadgeLineHeight"
		};
		foreach (string propertyName in array)
		{
			OnPropertyChanged(propertyName);
		}
	}

	private static string FormatCompactMetricText(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "대기";
		}
		string text = value.Trim();
		if (text.Contains("기록 없음", StringComparison.Ordinal) || text.Contains("기록없음", StringComparison.Ordinal) || text.Contains("조회 실패", StringComparison.Ordinal) || text.Contains("서버 미지원", StringComparison.Ordinal))
		{
			return text;
		}
		string s = value.Replace(",", "").Trim();
		if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) && !double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out result))
		{
			return value;
		}
		double num = Math.Abs(result);
		if (num >= 1000000.0)
		{
			return $"{result / 1000000.0:0.#}M";
		}
		if (num >= 1000.0)
		{
			return $"{result / 1000.0:0.#}K";
		}
		return result.ToString("0");
	}

	private static bool IsAverageDpsVisible(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}
		string text = value.Trim();
		if (string.Equals(text, "조회 중...", StringComparison.Ordinal))
		{
			return true;
		}
		if (text.Contains("기록 없음", StringComparison.Ordinal) || text.Contains("기록없음", StringComparison.Ordinal) || text.Contains("조회 실패", StringComparison.Ordinal) || text.Contains("서버 미지원", StringComparison.Ordinal) || text.Contains("대기", StringComparison.Ordinal))
		{
			return false;
		}
		string s = text.Replace(",", "").Trim();
		if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) && !double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out result))
		{
			return false;
		}
		return result > 0.0;
	}

	protected virtual void OnPropertyChanged(string propertyName)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
