using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using INGMeter.Core;
using INGMeter.WpfUI;

namespace INGMeter.App;

public class DpsCardViewModel : INotifyPropertyChanged
{
	private string _characterName = "";

	private JobClass _job;

	private string _name = "";

	private string _serverName = "";

	private string _dpsText = "";

	private string _subText = "";

	private string _combatTimeText = "00:00";

	private bool _showCombatTime;

	private double _damageShareRatio;

	private double _damageShareBackgroundRatio;

	private string _damageSharePctText = "";

	private bool _isBossHpShareMode;

	private string _critRateText = "";

	private bool _isHudMode;

	private AppearanceSelection _appearance = AppearanceSelection.Default;

	private MeterSkinProfile _skinProfile = AppearanceCatalog.GetSkinProfile(MeterSkin.Default);

	private double _uiScale = 1.0;

	private MeterDisplayPreset _displayPreset = MeterDisplayPreset.Standard;

	private MeterFontWeightMode _fontWeightMode = MeterFontWeightMode.Normal;

	private bool _textShadowEnabled = true;

	private double _visualLayoutScale = 1.0;

	private double _visualTextScale = 1.0;

	private int _visualFontSizeDelta;

	private string _combatScore = "";

	private string _averageDpsScopeKey = "";

	private bool _isDungeonAverageDps;

	private string _combatPower = "";

	private bool _isMeterUserOnline;

	private bool _isTopDamageRank;

	private int _hits;

	public int ActorId { get; set; }

	public long TotalDamage { get; set; }

	public long TotalHealing { get; set; }

	public string CharacterName
	{
		get
		{
			return _characterName;
		}
		set
		{
			_characterName = value;
			OnPropertyChanged("CharacterName");
			OnPropertyChanged("CrayonSketchPhase");
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
			OnPropertyChanged("DamageShareBackgroundBrush");
			OnPropertyChanged("DamageShareBorderBrush");
			OnPropertyChanged("AetherVeilDamageShareTextBrush");
			OnPropertyChanged("NeonDamageShareBackgroundBrush");
			OnPropertyChanged("NeonDamageShareBorderBrush");
			OnPropertyChanged("BloomDamageShareBackgroundBrush");
			OnPropertyChanged("BloomDamageShareBorderBrush");
			OnPropertyChanged("CrayonSketchPhase");
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
			OnPropertyChanged("CrayonSketchPhase");
		}
	}

	public double CrayonSketchPhase => CreateCrayonSketchPhase(ActorId, CharacterName, Name, Job);

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

	public string DpsText
	{
		get
		{
			return _dpsText;
		}
		set
		{
			_dpsText = value;
			OnPropertyChanged("DpsText");
		}
	}

	public string SubText
	{
		get
		{
			return _subText;
		}
		set
		{
			_subText = value;
			OnPropertyChanged("SubText");
		}
	}

	public string CombatTimeText
	{
		get
		{
			return _combatTimeText;
		}
		set
		{
			_combatTimeText = value;
			OnPropertyChanged("CombatTimeText");
		}
	}

	public bool ShowCombatTime
	{
		get
		{
			return _showCombatTime;
		}
		set
		{
			_showCombatTime = value;
			OnPropertyChanged("ShowCombatTime");
		}
	}

	public Brush DamageShareBackgroundBrush
	{
		get
		{
			if (!IsBlueMistTheme)
			{
				return JobClassVisuals.AccentBrushFor(Job);
			}
			return JobClassVisuals.PastelBrushFor(Job);
		}
	}

	public SolidColorBrush DamageShareBorderBrush
	{
		get
		{
			if (!IsBlueMistTheme)
			{
				return JobClassVisuals.BorderBrushFor(Job);
			}
			return JobClassVisuals.PastelBorderBrushFor(Job);
		}
	}

	public SolidColorBrush AetherVeilDamageShareTextBrush => JobClassVisuals.AetherVeilTextBrushFor(Job);

	public Brush NeonDamageShareBackgroundBrush => JobClassVisuals.NeonBrushFor(Job);

	public SolidColorBrush NeonDamageShareBorderBrush => JobClassVisuals.NeonBorderBrushFor(Job);

	public Brush BloomDamageShareBackgroundBrush
	{
		get
		{
			if (!IsBloomTheme)
			{
				return DamageShareBackgroundBrush;
			}
			return JobClassVisuals.BloomBrushFor(Job);
		}
	}

	public SolidColorBrush BloomDamageShareBorderBrush
	{
		get
		{
			if (!IsBloomTheme)
			{
				return DamageShareBorderBrush;
			}
			return JobClassVisuals.BloomBorderBrushFor(Job);
		}
	}

	public double DamageShareBackgroundRatio
	{
		get
		{
			return _damageShareBackgroundRatio;
		}
		set
		{
			_damageShareBackgroundRatio = value;
			OnPropertyChanged("DamageShareBackgroundRatio");
			OnPropertyChanged("DamageShareFillCornerRadius");
		}
	}

	public double DamageShareRatio
	{
		get
		{
			return _damageShareRatio;
		}
		set
		{
			_damageShareRatio = value;
			OnPropertyChanged("DamageShareRatio");
			OnPropertyChanged("UsesCriticalDamageShareText");
			OnPropertyChanged("UsesWarningDamageShareText");
			OnPropertyChanged("DamageShareFillCornerRadius");
		}
	}

	public bool UsesCriticalDamageShareText => _damageShareRatio <= 3.0;

	public bool UsesWarningDamageShareText
	{
		get
		{
			if (_damageShareRatio > 3.0)
			{
				return _damageShareRatio <= 5.0;
			}
			return false;
		}
	}

	public CornerRadius DamageShareFillCornerRadius
	{
		get
		{
			if (!(_damageShareBackgroundRatio >= 99.95))
			{
				return new CornerRadius(5.0, 0.0, 0.0, 5.0);
			}
			return new CornerRadius(5.0);
		}
	}

	public string DamageSharePctText
	{
		get
		{
			return _damageSharePctText;
		}
		set
		{
			_damageSharePctText = value;
			OnPropertyChanged("DamageSharePctText");
		}
	}

	public bool IsBossHpShareMode
	{
		get
		{
			return _isBossHpShareMode;
		}
		set
		{
			_isBossHpShareMode = value;
			OnPropertyChanged("IsBossHpShareMode");
		}
	}

	public string CritRateText
	{
		get
		{
			return _critRateText;
		}
		set
		{
			_critRateText = value;
			OnPropertyChanged("CritRateText");
		}
	}

	public bool IsHudMode
	{
		get
		{
			return _isHudMode;
		}
		set
		{
			_isHudMode = value;
			OnPropertyChanged("IsHudMode");
			OnVisualMetricsChanged();
		}
	}

	public string Theme
	{
		get
		{
			return _appearance.ResourceThemeName;
		}
		set
		{
			AppearanceSelection appearanceSelection = AppearanceCatalog.FromLegacyThemeName(value);
			if (!_appearance.Equals(appearanceSelection))
			{
				_appearance = appearanceSelection;
				_skinProfile = AppearanceCatalog.GetSkinProfile(appearanceSelection.Skin);
				OnPropertyChanged("Theme");
				OnPropertyChanged("UsesDecorativeSubTextBadge");
				OnPropertyChanged("UsesAbyssDamageShareText");
				OnPropertyChanged("UsesAetherVeilDamageShareText");
				OnPropertyChanged("DamageShareBackgroundBrush");
				OnPropertyChanged("DamageShareBorderBrush");
				OnPropertyChanged("BloomDamageShareBackgroundBrush");
				OnPropertyChanged("BloomDamageShareBorderBrush");
				OnPropertyChanged("TextShadowEnabled");
				OnVisualMetricsChanged();
			}
		}
	}

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

	public MeterDisplayPreset DisplayPreset
	{
		get
		{
			return _displayPreset;
		}
		set
		{
			_displayPreset = value;
			OnPropertyChanged("DisplayPreset");
			OnPropertyChanged("ShowNameCombatPowerBadge");
			OnPropertyChanged("ShowNameAverageDpsBadge");
			OnPropertyChanged("ShowMetricCombatPowerBadge");
			OnVisualMetricsChanged();
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

	public bool TextShadowEnabled
	{
		get
		{
			if (_textShadowEnabled)
			{
				return !IsSoftDecorativeTheme;
			}
			return false;
		}
		set
		{
			if (_textShadowEnabled != value)
			{
				_textShadowEnabled = value;
				OnPropertyChanged("TextShadowEnabled");
				OnPropertyChanged("TextShadowPreference");
			}
		}
	}

	public bool TextShadowPreference => _textShadowEnabled;

	private bool IsMinimal => DisplayPreset == MeterDisplayPreset.Minimal;

	private bool IsBloomTheme => _skinProfile.UsesBloomLayoutFamily;

	private bool IsBlueMistTheme
	{
		get
		{
			if (_appearance.Skin == MeterSkin.Default)
			{
				return _appearance.Palette == MeterPalette.BlueMist;
			}
			return false;
		}
	}

	private bool IsAbyssTheme => _skinProfile.IsAbyss;

	private bool IsAetherVeilTheme => _skinProfile.IsAetherVeil;

	private bool IsDefaultSkin => _appearance.Skin == MeterSkin.Default;

	private bool IsCompactTwoLine => !IsMinimal;

	private bool IsSoftDecorativeTheme => _skinProfile.UsesSoftDecoration;

	private bool IsNeonTheme => _skinProfile.UsesNeonDecoration;

	private bool HasNameInlineMetrics
	{
		get
		{
			if (IsMinimal)
			{
				if (string.IsNullOrWhiteSpace(CombatPower))
				{
					return !string.IsNullOrWhiteSpace(CombatScoreBadgeText);
				}
				return true;
			}
			return false;
		}
	}

	public bool UsesDecorativeSubTextBadge => _skinProfile.UsesDecorativeSubTextBadge;

	public bool UsesAbyssDamageShareText => _skinProfile.UsesAbyssDamageShareText;

	public bool UsesAetherVeilDamageShareText => _skinProfile.UsesAetherVeilDamageShareText;

	private double ThemeLayoutScale
	{
		get
		{
			if (!IsBloomTheme || IsAbyssTheme)
			{
				return _visualLayoutScale;
			}
			return Math.Clamp(_visualLayoutScale * 0.94, 0.75, 1.7);
		}
	}

	private double TextScale => Math.Clamp(_visualTextScale * ((IsBloomTheme && !IsAbyssTheme) ? 0.96 : 1.0), 0.6, 1.4);

	private double CardDensity => MeterVisualScale.CardDensity(TextScale);

	private double CardWidthDensity => MeterVisualScale.CardWidthDensity(TextScale);

	private double MinimalVerticalScale
	{
		get
		{
			if (!IsMinimal)
			{
				return ThemeLayoutScale;
			}
			return Math.Clamp(ThemeLayoutScale + Math.Max(0.0, ThemeLayoutScale - 1.0) * (IsHudMode ? 0.95 : 0.5), 0.75, 1.62);
		}
	}

	private double CompactTwoLineVerticalScale => Math.Clamp(ThemeLayoutScale * 0.78, 0.8, 1.2);

	public double CardItemHeight
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return double.NaN;
			}
			return DimCompactTwoLine(IsHudMode ? 37 : 39);
		}
	}

	public Thickness CardContentMargin
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				if (!IsDefaultSkin)
				{
					if (!IsAetherVeilTheme)
					{
						if (!IsBloomTheme)
						{
							if (!IsHudMode)
							{
								if (!IsMinimal)
								{
									return ThickDense(6.0, 4.0, 6.0, 4.0);
								}
								return ThickDense(6.0, 2.0, 6.0, 2.0);
							}
							if (!IsMinimal)
							{
								return ThickDense(4.0, 1.0, 4.0, 1.0);
							}
							return ThickDense(4.0, 0.0, 4.0, 0.0);
						}
						if (!IsHudMode)
						{
							if (!IsMinimal)
							{
								return ThickDense(8.0, 4.0, 8.0, 4.0);
							}
							return ThickDense(7.0, 2.0, 7.0, 2.0);
						}
						if (!IsMinimal)
						{
							return ThickDense(6.0, 2.0, 6.0, 2.0);
						}
						return ThickDense(5.0, 1.0, 5.0, 1.0);
					}
					if (!IsHudMode)
					{
						if (!IsMinimal)
						{
							return ThickDense(8.0, 3.0, 8.0, 3.0);
						}
						return ThickDense(7.0, 1.0, 7.0, 1.0);
					}
					if (!IsMinimal)
					{
						return ThickDense(6.0, 1.5, 6.0, 1.5);
					}
					return ThickDense(5.0, 0.5, 5.0, 0.5);
				}
				if (!IsHudMode)
				{
					if (!IsMinimal)
					{
						return ThickDense(8.0, 0.0, 8.0, 0.0);
					}
					return ThickDense(5.0, 0.5, 5.0, 0.5);
				}
				if (!IsMinimal)
				{
					return ThickDense(6.0, 1.0, 6.0, 1.0);
				}
				return ThickDense(4.0, 0.0, 4.0, 0.0);
			}
			return ThickCompactTwoLine(4.0, 0.0, 6.0, 0.0);
		}
	}

	public double Row0Height
	{
		get
		{
			if (!IsMinimal)
			{
				return DimCompactTwoLine(21.0);
			}
			return DimDenseVertical((!IsDefaultSkin) ? ((!IsAetherVeilTheme) ? ((!IsBloomTheme) ? (IsHudMode ? 21 : 28) : (IsHudMode ? 24 : 30)) : (IsHudMode ? 23 : 28)) : (IsHudMode ? 22 : 27));
		}
	}

	public double Row1Height
	{
		get
		{
			if (!IsMinimal)
			{
				return DimCompactTwoLine(13.0);
			}
			return 0.0;
		}
	}

	public double JobIconHostWidth
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return DimDense((!IsAbyssTheme) ? ((!IsAetherVeilTheme && !IsDefaultSkin) ? ((!IsBloomTheme) ? ((!IsNeonTheme) ? ((!IsHudMode) ? (IsMinimal ? 28 : 40) : (IsMinimal ? 22 : 30)) : ((!IsHudMode) ? (IsMinimal ? 40 : 54) : (IsMinimal ? 31 : 40))) : ((!IsHudMode) ? (IsMinimal ? 32 : 46) : (IsMinimal ? 26 : 36))) : ((!IsHudMode) ? (IsMinimal ? 27 : 46) : (IsMinimal ? 24 : 36))) : ((!IsHudMode) ? (IsMinimal ? 34 : 48) : (IsMinimal ? 28 : 39)));
			}
			return DimCompactTwoLine(30.0);
		}
	}

	public double JobIconHostHeight
	{
		get
		{
			if (!IsMinimal)
			{
				if (!IsCompactTwoLine)
				{
					return DimDense((!IsAbyssTheme) ? ((!IsDefaultSkin) ? ((!IsAetherVeilTheme) ? ((!IsBloomTheme) ? ((!IsNeonTheme) ? (IsHudMode ? 33 : 40) : (IsHudMode ? 43 : 54)) : (IsHudMode ? 40 : 50)) : (IsHudMode ? 37 : 46)) : (IsHudMode ? 34 : 22)) : (IsHudMode ? 43 : 52));
				}
				return DimCompactTwoLine(30.0);
			}
			return DimDenseVertical((!IsAbyssTheme) ? ((!IsDefaultSkin) ? ((!IsAetherVeilTheme) ? ((!IsBloomTheme) ? ((!IsNeonTheme) ? (IsHudMode ? 20 : 24) : (IsHudMode ? 28 : 35)) : (IsHudMode ? 24 : 30)) : (IsHudMode ? 23 : 28)) : (IsHudMode ? 22 : 27)) : (IsHudMode ? 27 : 33));
		}
	}

	public Thickness JobIconHostMargin
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				if (!IsBloomTheme && !IsDefaultSkin)
				{
					if (!IsNeonTheme)
					{
						if (!IsHudMode)
						{
							return ThickDense(0.0, 0.0, IsMinimal ? 5 : 7, 0.0);
						}
						return ThickDense(0.0, 0.0, 4.0, 0.0);
					}
					return ThickDense(0.0, 0.0, IsMinimal ? 6 : 8, 0.0);
				}
				return ThickDense(0.0, 0.0, IsMinimal ? 1 : 8, 0.0);
			}
			return ThickCompactTwoLine(2.0, 0.0, 4.0, 0.0);
		}
	}

	public double JobIconSize
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return DimDense((!IsAbyssTheme) ? ((!IsDefaultSkin) ? ((!IsAetherVeilTheme) ? ((!IsBloomTheme) ? ((!IsNeonTheme) ? ((!IsHudMode) ? (IsMinimal ? 22 : 32) : (IsMinimal ? 18 : 24)) : ((!IsHudMode) ? (IsMinimal ? 34 : 46) : (IsMinimal ? 26 : 33))) : ((!IsHudMode) ? (IsMinimal ? 28 : 38) : (IsMinimal ? 22 : 30))) : ((!IsHudMode) ? (IsMinimal ? 26 : 36) : (IsMinimal ? 21 : 29))) : ((!IsHudMode) ? (IsMinimal ? 25 : 21) : (IsMinimal ? 20 : 27))) : ((!IsHudMode) ? (IsMinimal ? 30 : 40) : (IsMinimal ? 24 : 33)));
			}
			return DimCompactTwoLine(26.0);
		}
	}

	public CornerRadius JobIconCornerRadius => MeterVisualScale.Radius(JobIconSize / 2.0);

	public double JobFallbackFontSize => Font(10.0);

	public Thickness CombatTimeBadgePadding => Thick(2.0, 0.0, 2.0, 0.0);

	public double CombatTimeBadgeHeight => DimDense(IsHudMode ? 10 : 12);

	public double CombatTimeBadgeMinWidth => DimWide(IsHudMode ? 29 : 32);

	public Thickness CombatTimeBadgeMargin => ThickDense(1.0, IsHudMode ? 1 : 2, 0.0, 0.0);

	public double CombatTimeFontSize => Math.Min(Font(IsHudMode ? 6.8 : 7.2), IsHudMode ? 7.3 : 7.8);

	public double NameFontSize
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return Font((!IsAbyssTheme) ? ((!IsBloomTheme) ? ((double)((!IsHudMode) ? (IsMinimal ? 12 : 12) : (IsMinimal ? 11 : 12))) : ((!IsHudMode) ? (IsMinimal ? 13.0 : 14.5) : (IsMinimal ? 12.0 : 13.2))) : ((!IsHudMode) ? (IsMinimal ? 13.6 : 15.0) : (IsMinimal ? 12.6 : 13.7)));
			}
			return CompactTwoLineFont(11.8);
		}
	}

	public double NameLineHeight => NameFontSize + (double)(IsCompactTwoLine ? 1 : 2);

	public double NameMaxWidth => Dim((!IsBloomTheme) ? ((!IsHudMode) ? ((!IsMinimal) ? 128 : (HasNameInlineMetrics ? 100 : 120)) : ((!IsMinimal) ? 90 : (HasNameInlineMetrics ? 70 : 78))) : ((!IsHudMode) ? ((!IsMinimal) ? 140 : (HasNameInlineMetrics ? 108 : 120)) : ((!IsMinimal) ? 116 : (HasNameInlineMetrics ? 84 : 92))));

	public double ServerFontSize
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return Font(IsBloomTheme ? 11.5 : 11.0);
			}
			return CompactTwoLineFont(10.0);
		}
	}

	public double ServerLineHeight => ServerFontSize + 1.0;

	public double ServerMaxWidth => Dim(IsBloomTheme ? 36 : 26);

	public double NameGuardWidth => NameMaxWidth + ServerMaxWidth + DimWide(IsMinimal ? 18 : 22);

	public double SegmentLeftTextCutWidth => CardContentMargin.Left + JobIconHostWidth + DimWide(IsCompactTwoLine ? 2 : (IsMinimal ? 4 : 6));

	public double BloomShareLeftPadding
	{
		get
		{
			if (!IsBloomTheme || !IsMinimal)
			{
				return SegmentLeftTextCutWidth;
			}
			return 0.0;
		}
	}

	public double MetricBadgeHeight => Math.Max(IsCompactTwoLine ? DimCompactTwoLine(15.0) : DimDense(16.0), MetricValueLineHeight + (IsCompactTwoLine ? DimCompactTwoLine(3.0) : Dim(3.0)));

	public Thickness CombatPowerBadgePadding
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return Thick(3.0, 1.0, 4.0, 1.0);
			}
			return ThickCompactTwoLine(3.0, 1.0, 4.0, 1.0);
		}
	}

	public Thickness AvgBadgePadding
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return Thick(3.0, 1.0, 3.0, 1.0);
			}
			return ThickCompactTwoLine(3.0, 1.0, 4.0, 1.0);
		}
	}

	public double MetricValueFontSize
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return Font(8.5);
			}
			return CompactTwoLineFont(8.0);
		}
	}

	public double MetricValueLineHeight => MetricValueFontSize + 1.0;

	public double CombatPowerIconSize => Math.Max(6.0, Math.Min(MetricBadgeHeight - (IsCompactTwoLine ? DimCompactTwoLine(3.0) : Dim(3.0)), MetricValueLineHeight));

	public double AvgLabelFontSize
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return Font(7.0);
			}
			return CompactTwoLineFont(7.0);
		}
	}

	public double AvgLabelLineHeight => AvgLabelFontSize + 1.0;

	public Thickness MetaLineMargin
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				if (!IsDefaultSkin)
				{
					return ThickDense(0.0, -1.0, 6.0, 0.0);
				}
				return ThickDense(0.0, IsHudMode ? (-3) : (-2), 6.0, 0.0);
			}
			return ThickCompactTwoLine(-1.0, 0.0, 6.0, 0.0);
		}
	}

	public Thickness LeftTextStackMargin
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return ThickDense(0.0, 0.0, 0.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 0.0, 0.0);
		}
	}

	public Thickness RightValueStackMargin
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return ThickDense(0.0, 0.0, 0.0, 0.0);
			}
			return ThickCompactTwoLine(0.0, 0.0, 1.0, 0.0);
		}
	}

	public Thickness DpsStackMargin
	{
		get
		{
			if (!IsHudMode || !IsMinimal)
			{
				if (!IsMinimal)
				{
					if (!IsCompactTwoLine)
					{
						return ThickDense(0.0, -1.0, (!IsHudMode) ? 1 : 0, 0.0);
					}
					return ThickCompactTwoLine(0.0, 0.0, 0.0, 0.0);
				}
				return ThickDense(0.0, 0.0, 2.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 0.0, 0.0);
		}
	}

	private double DpsValueColumnWidthValue => DimWide((!IsBloomTheme) ? ((!IsHudMode) ? (IsMinimal ? 72 : 98) : (IsMinimal ? 72 : 82)) : ((!IsHudMode) ? (IsMinimal ? 100 : 100) : (IsMinimal ? 80 : 84)));

	private double DpsShareColumnWidthValue => DimWide((!IsBloomTheme) ? ((!IsHudMode) ? (IsMinimal ? 42 : 54) : (IsMinimal ? 42 : 50)) : ((!IsHudMode) ? (IsMinimal ? 50 : 54) : (IsMinimal ? 46 : 50)));

	public GridLength DpsValueColumnWidth => new GridLength(DpsValueColumnWidthValue);

	public GridLength DpsShareColumnWidth => new GridLength(DpsShareColumnWidthValue);

	public double DpsStackMinWidth => DpsValueColumnWidthValue + DpsShareColumnWidthValue;

	public double SegmentRightTextCutWidth => CardContentMargin.Right + DpsStackMinWidth + DimWide((!IsBloomTheme) ? (IsMinimal ? 6 : 10) : (IsMinimal ? 3 : 4));

	public double BloomShareRightPadding
	{
		get
		{
			if (!IsBloomTheme)
			{
				return SegmentRightTextCutWidth;
			}
			return 0.0;
		}
	}

	public double DpsFontSize
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return Font((!IsAbyssTheme) ? ((!IsBloomTheme) ? (IsHudMode ? ((double)(IsMinimal ? 12 : 13)) : (IsMinimal ? 13.0 : 13.5)) : ((!IsHudMode) ? ((double)(IsMinimal ? 14 : 16)) : (IsMinimal ? 12.8 : 14.0))) : ((!IsHudMode) ? (IsMinimal ? 14.2 : 16.2) : (IsMinimal ? 13.0 : 14.4)));
			}
			return CompactTwoLineFont(13.0);
		}
	}

	public double DpsLineHeight => DpsFontSize + 1.0;

	public Thickness DpsValueMargin
	{
		get
		{
			if (!IsHudMode)
			{
				if (!IsMinimal)
				{
					return ThickDense(0.0, 0.0, 5.0, 0.0);
				}
				return ThickDense(0.0, 0.0, 3.0, 0.0);
			}
			return ThickDense(0.0, 0.0, 2.0, 0.0);
		}
	}

	public Thickness SubStackMargin
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				if (!IsDefaultSkin)
				{
					if (!IsHudMode)
					{
						return ThickDense(0.0, -1.0, 1.0, 0.0);
					}
					return ThickDense(0.0, 1.0, 0.0, 0.0);
				}
				return ThickDense(0.0, IsHudMode ? (-2) : (-4), 1.0, 0.0);
			}
			return ThickCompactTwoLine(0.0, 0.0, 1.0, 0.0);
		}
	}

	public double SubStackMinWidth => DpsStackMinWidth;

	public Thickness SubTextBadgePadding => ThickDense(IsBloomTheme ? 4 : 0, 0.0, IsBloomTheme ? 4 : 0, 0.0);

	public double SubFontSize
	{
		get
		{
			if (!IsCompactTwoLine)
			{
				return Font((!IsBloomTheme) ? ((double)(IsHudMode ? 9 : 10)) : (IsHudMode ? 9.8 : 11.0));
			}
			return CompactTwoLineFont(8.4);
		}
	}

	public double SubLineHeight => SubFontSize + 1.0;

	public Thickness MetricDividerMargin => ThickDense(IsBloomTheme ? 12 : 0, 0.0, IsBloomTheme ? 5 : 0, 0.0);

	public string CombatScore
	{
		get
		{
			return _combatScore;
		}
		set
		{
			_combatScore = value;
			OnPropertyChanged("CombatScore");
			OnPropertyChanged("CombatScoreBadgeText");
			OnPropertyChanged("ShowNameAverageDpsBadge");
			OnPropertyChanged("NameMaxWidth");
		}
	}

	public string CombatScoreBadgeText
	{
		get
		{
			if (string.Equals(CombatScore, "조회 중...", StringComparison.Ordinal))
			{
				return "...";
			}
			if (!HasAverageDpsDisplayValue(CombatScore))
			{
				return "";
			}
			return CombatScore;
		}
	}

	public string AverageDpsScopeKey
	{
		get
		{
			return _averageDpsScopeKey;
		}
		set
		{
			_averageDpsScopeKey = value ?? "";
			OnPropertyChanged("AverageDpsScopeKey");
		}
	}

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

	public string CombatPower
	{
		get
		{
			return _combatPower;
		}
		set
		{
			_combatPower = value;
			OnPropertyChanged("CombatPower");
			OnPropertyChanged("ShowNameCombatPowerBadge");
			OnPropertyChanged("ShowMetricCombatPowerBadge");
			OnPropertyChanged("NameMaxWidth");
		}
	}

	public bool ShowNameCombatPowerBadge
	{
		get
		{
			if (DisplayPreset == MeterDisplayPreset.Minimal)
			{
				return !string.IsNullOrWhiteSpace(CombatPower);
			}
			return false;
		}
	}

	public bool ShowNameAverageDpsBadge
	{
		get
		{
			if (DisplayPreset == MeterDisplayPreset.Minimal)
			{
				return !string.IsNullOrWhiteSpace(CombatScoreBadgeText);
			}
			return false;
		}
	}

	public bool ShowMetricCombatPowerBadge
	{
		get
		{
			if (DisplayPreset != MeterDisplayPreset.Minimal)
			{
				return !string.IsNullOrWhiteSpace(CombatPower);
			}
			return false;
		}
	}

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

	public bool IsTopDamageRank
	{
		get
		{
			return _isTopDamageRank;
		}
		set
		{
			if (_isTopDamageRank != value)
			{
				_isTopDamageRank = value;
				OnPropertyChanged("IsTopDamageRank");
			}
		}
	}

	public int Hits
	{
		get
		{
			return _hits;
		}
		set
		{
			_hits = value;
			OnPropertyChanged("Hits");
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private static double CreateCrayonSketchPhase(int actorId, string characterName, string name, JobClass job)
	{
		return (double)(MixHash(MixHash(MixHash(MixHash(2166136261u, actorId), (int)job), characterName), name) % 997) * 0.017;
	}

	private static uint MixHash(uint hash, int value)
	{
		hash ^= (uint)value;
		return hash * 16777619;
	}

	private static uint MixHash(uint hash, string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return hash;
		}
		foreach (char c in value)
		{
			hash ^= c;
			hash *= 16777619;
		}
		return hash;
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
		return MeterVisualScale.Dimension(value, ThemeLayoutScale);
	}

	private double DimDense(double value)
	{
		return MeterVisualScale.Dimension(value * CardDensity, ThemeLayoutScale);
	}

	private double DimWide(double value)
	{
		return MeterVisualScale.Dimension(value * CardWidthDensity, ThemeLayoutScale);
	}

	private double DimVertical(double value)
	{
		return MeterVisualScale.Dimension(value, MinimalVerticalScale);
	}

	private double DimDenseVertical(double value)
	{
		return MeterVisualScale.Dimension(value * CardDensity, MinimalVerticalScale);
	}

	private double DimCompactTwoLine(double value)
	{
		return MeterVisualScale.Dimension(value * CardDensity, CompactTwoLineVerticalScale);
	}

	private double Font(double value)
	{
		return MeterVisualScale.Font(value, TextScale, _visualFontSizeDelta);
	}

	private double CompactTwoLineFont(double value)
	{
		return MeterVisualScale.Font(value, Math.Clamp(TextScale, 0.6, 1.22), Math.Clamp(_visualFontSizeDelta, 0, 4));
	}

	private Thickness Thick(double left, double top, double right, double bottom)
	{
		return MeterVisualScale.Thickness(left, top, right, bottom, ThemeLayoutScale);
	}

	private Thickness ThickDense(double left, double top, double right, double bottom)
	{
		return MeterVisualScale.Thickness(left * CardWidthDensity, top * CardDensity, right * CardWidthDensity, bottom * CardDensity, ThemeLayoutScale);
	}

	private Thickness ThickCompactTwoLine(double left, double top, double right, double bottom)
	{
		return MeterVisualScale.Thickness(left * CardWidthDensity, top * CardDensity, right * CardWidthDensity, bottom * CardDensity, CompactTwoLineVerticalScale);
	}

	private void OnVisualMetricsChanged()
	{
		string[] array = new string[50]
		{
			"CardItemHeight", "CardContentMargin", "Row0Height", "Row1Height", "JobIconHostWidth", "JobIconHostHeight", "JobIconHostMargin", "JobIconSize", "JobIconCornerRadius", "JobFallbackFontSize",
			"CombatTimeBadgePadding", "CombatTimeBadgeHeight", "CombatTimeBadgeMinWidth", "CombatTimeBadgeMargin", "CombatTimeFontSize", "NameFontSize", "NameLineHeight", "NameMaxWidth", "ServerFontSize", "ServerLineHeight",
			"ServerMaxWidth", "NameGuardWidth", "SegmentLeftTextCutWidth", "BloomShareLeftPadding", "BloomShareRightPadding", "MetricBadgeHeight", "CombatPowerBadgePadding", "AvgBadgePadding", "CombatPowerIconSize", "MetricValueFontSize",
			"MetricValueLineHeight", "AvgLabelFontSize", "AvgLabelLineHeight", "MetaLineMargin", "LeftTextStackMargin", "RightValueStackMargin", "DpsStackMargin", "DpsValueColumnWidth", "DpsShareColumnWidth", "DpsStackMinWidth",
			"SegmentRightTextCutWidth", "DpsFontSize", "DpsLineHeight", "DpsValueMargin", "SubStackMargin", "SubStackMinWidth", "SubTextBadgePadding", "SubFontSize", "SubLineHeight", "MetricDividerMargin"
		};
		foreach (string name in array)
		{
			OnPropertyChanged(name);
		}
	}

	private static bool HasAverageDpsDisplayValue(string? value)
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
		if (string.Equals(text, "0", StringComparison.Ordinal) || string.Equals(text, "0.0", StringComparison.Ordinal) || string.Equals(text, "0K", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "0M", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return true;
	}

	protected void OnPropertyChanged(string name)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
	}
}
