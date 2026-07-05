using System;
using System.CodeDom.Compiler;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using INGMeter.App.Updates;
using INGMeter.Core;
using INGMeter.WpfUI;

namespace INGMeter.App;

public class SettingsWindow : Window, IComponentConnector
{
	private sealed class FontFamilyOption
	{
		public string Source { get; }

		public string DisplayName { get; }

		public FontFamilyOption(string source, string displayName)
		{
			Source = source;
			DisplayName = displayName;
		}
	}

	private sealed record LookupSkillTileTag(string ClassKey, int SkillId);

	private const int MaxDpsCardLimit = 10;

	private readonly MeterEngine? _engine;

	private readonly SkillNameMap? _skillNames;

	private readonly AppUpdateService _updateService;

	private readonly LookupSkillCatalog _lookupSkillCatalog;

	private readonly Dictionary<string, HashSet<int>> _lookupSkillSelections;

	private readonly HashSet<string> _lookupSkillDisabledClasses;

	private readonly Func<CharacterConsentState, bool, Task<bool>>? _setCharacterConsentAsync;

	private readonly Func<IReadOnlyList<CharacterConsentState>>? _refreshCharacterConsentStates;

	private List<CharacterConsentState> _characterConsentStates = new List<CharacterConsentState>();

	private PacketLogWindow? _packetLogWindow;

	private bool _isInitializing;

	private readonly bool _opacitySliderTargetsHud;

	private string? _selectedLookupSkillClassKey;

	internal ComboBox cmbDisplayPreset;

	internal ComboBox cmbDamageShareMode;

	internal ComboBox cmbDamageShareGraphMode;

	internal ComboBox cmbDpsCardNumberFormatMode;

	internal CheckBox chkShowBossCard;

	internal CheckBox chkShowDpsCardCombatTime;

	internal Slider sldMaxDpsCards;

	internal TextBlock txtMaxDpsCardsValue;

	internal ComboBox cmbTheme;

	internal Slider sldUiScale;

	internal TextBlock txtUiScaleValue;

	internal Slider sldWindowOpacity;

	internal TextBlock txtWindowOpacityValue;

	internal CheckBox chkAutoHideBackground;

	internal Slider sldTextScale;

	internal TextBlock txtTextScaleValue;

	internal ComboBox cmbFontWeightMode;

	internal ComboBox cmbFontFamily;

	internal CheckBox chkTextShadowEnabled;

	internal CheckBox chkLookupSkillDisplayEnabled;

	internal TextBlock txtLookupSkillSummary;

	internal WrapPanel lookupClassPanel;

	internal TextBlock txtLookupSkillClassTitle;

	internal CheckBox chkLookupSkillClassEnabled;

	internal TextBlock txtLookupSkillClassCount;

	internal StackPanel lookupSkillSectionsPanel;

	internal TextBox txtClearKey;

	internal TextBox txtHudKey;

	internal TextBox txtHideKey;

	internal TextBox txtClickThroughKey;

	internal TextBox txtMainViewKey;

	internal CheckBox chkShowOnlyWhenAionActive;

	internal CheckBox chkShowInTaskbar;

	internal ComboBox cmbCloseButtonBehavior;

	internal ComboBox cmbCaptureBackend;

	internal CheckBox chkSaveEncounterLogs;

	internal TextBlock txtLogDirectory;

	internal Button btnClearLogs;

	internal PasswordBox txtDevKey;

	internal Border cardDev;

	internal CheckBox chkShowActorId;

	internal CheckBox chkUseDummyData;

	internal Button btnRefreshCharacterConsent;

	internal StackPanel panelCharacterConsent;

	internal TextBlock txtVersion;

	private bool _contentLoaded;

	public string ClearHotkey { get; private set; }

	public string HudHotkey { get; private set; }

	public string HideHotkey { get; private set; }

	public string ClickThroughHotkey { get; private set; }

	public string MainViewHotkey { get; private set; }

	public int MaxDpsCards { get; private set; }

	public bool ShowActorId { get; private set; }

	public bool UseDummyData { get; private set; }

	public bool SaveEncounterLogs { get; private set; } = true;

	public bool HudClickThrough { get; private set; }

	public bool ShowBossCard { get; private set; } = true;

	public bool ShowDpsCardCombatTime { get; private set; }

	public bool AutoHideBackground { get; private set; }

	public bool ShowOnlyWhenAionActive { get; private set; }

	public bool ShowAppInTaskbar { get; private set; }

	public string CloseButtonBehaviorName { get; private set; } = "Ask";

	public int WindowOpacityPercent { get; private set; }

	public int HudOpacityPercent { get; private set; }

	public MeterDisplayPreset DisplayPreset { get; private set; }

	public DpsCardNumberFormatMode DpsCardNumberFormatMode { get; private set; }

	public DamageShareMode DamageShareMode { get; private set; }

	public DamageShareGraphMode DamageShareGraphMode { get; private set; }

	public double UiScale { get; private set; } = 0.96;

	public double TextScale { get; private set; } = 1.1;

	public MeterFontWeightMode FontWeightMode { get; private set; }

	public string FontFamilyName { get; private set; }

	public bool TextShadowEnabled { get; private set; } = true;

	public CaptureBackend CaptureBackend { get; private set; }

	public bool LookupSkillDisplayEnabled { get; private set; } = true;

	public Dictionary<string, HashSet<int>> LookupSkillSelections { get; private set; } = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);

	public HashSet<string> LookupSkillDisabledClasses { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public string DevKey { get; private set; }

	public string Theme { get; private set; }

	public event Action<SettingsWindow>? SettingsChanged;

	public SettingsWindow(string currentClear, string currentHudHotkey, string currentHideHotkey, string currentClickThroughHotkey, string currentMainViewHotkey, int currentMaxCards, bool currentShowActorId, bool currentUseDummyData, bool currentSaveEncounterLogs, bool currentHudClickThrough, bool currentShowBossCard, bool currentShowDpsCardCombatTime, bool currentAutoHideBackground, bool currentShowOnlyWhenAionActive, bool currentShowInTaskbar, string currentCloseButtonBehaviorName, int currentWindowOpacityPercent, int currentHudOpacityPercent, bool currentOpacitySliderTargetsHud, MeterDisplayPreset currentDisplayPreset, DpsCardNumberFormatMode currentDpsCardNumberFormatMode, DamageShareMode currentDamageShareMode, DamageShareGraphMode currentDamageShareGraphMode, double currentUiScale, double currentTextScale, MeterFontWeightMode currentFontWeightMode, string currentFontFamilyName, bool currentTextShadowEnabled, CaptureBackend currentCaptureBackend, string devKey, string currentTheme, bool currentLookupSkillDisplayEnabled, LookupSkillCatalog lookupSkillCatalog, IReadOnlyDictionary<string, HashSet<int>> currentLookupSkillSelections, IReadOnlySet<string> currentLookupSkillDisabledClasses, AppUpdateService updateService, MeterEngine? engine = null, SkillNameMap? skillNames = null, IReadOnlyList<CharacterConsentState>? characterConsentStates = null, Func<CharacterConsentState, bool, Task<bool>>? setCharacterConsentAsync = null, Func<IReadOnlyList<CharacterConsentState>>? refreshCharacterConsentStates = null)
	{
		_isInitializing = true;
		_opacitySliderTargetsHud = currentOpacitySliderTargetsHud;
		InitializeComponent();
		_updateService = updateService;
		_lookupSkillCatalog = lookupSkillCatalog;
		_lookupSkillSelections = LookupSkillSelectionSerializer.Clone(currentLookupSkillSelections);
		_lookupSkillDisabledClasses = LookupSkillClassSetSerializer.Clone(currentLookupSkillDisabledClasses);
		LookupSkillSelections = LookupSkillSelectionSerializer.Clone(_lookupSkillSelections);
		LookupSkillDisabledClasses = LookupSkillClassSetSerializer.Clone(_lookupSkillDisabledClasses);
		txtLogDirectory.Text = GetSafeLogDirectoryText();
		DevKey = devKey;
		txtDevKey.Password = devKey;
		txtClearKey.Text = currentClear;
		txtHudKey.Text = "None";
		txtHideKey.Text = currentHideHotkey;
		txtClickThroughKey.Text = currentClickThroughHotkey;
		txtMainViewKey.Text = currentMainViewHotkey;
		ClearHotkey = currentClear;
		HudHotkey = "None";
		HideHotkey = currentHideHotkey;
		ClickThroughHotkey = currentClickThroughHotkey;
		MainViewHotkey = currentMainViewHotkey;
		MaxDpsCards = Math.Clamp(currentMaxCards, 1, 10);
		sldMaxDpsCards.Value = MaxDpsCards;
		UpdateMaxDpsCardsText();
		ShowActorId = currentShowActorId;
		chkShowActorId.IsChecked = ShowActorId;
		UseDummyData = currentUseDummyData;
		chkUseDummyData.IsChecked = UseDummyData;
		SaveEncounterLogs = currentSaveEncounterLogs;
		chkSaveEncounterLogs.IsChecked = SaveEncounterLogs;
		HudClickThrough = currentHudClickThrough;
		ShowBossCard = currentShowBossCard;
		chkShowBossCard.IsChecked = ShowBossCard;
		ShowDpsCardCombatTime = currentShowDpsCardCombatTime;
		chkShowDpsCardCombatTime.IsChecked = ShowDpsCardCombatTime;
		AutoHideBackground = currentAutoHideBackground;
		chkAutoHideBackground.IsChecked = AutoHideBackground;
		ShowOnlyWhenAionActive = currentShowOnlyWhenAionActive;
		chkShowOnlyWhenAionActive.IsChecked = ShowOnlyWhenAionActive;
		ShowAppInTaskbar = currentShowInTaskbar;
		chkShowInTaskbar.IsChecked = ShowAppInTaskbar;
		CloseButtonBehaviorName = NormalizeCloseButtonBehaviorName(currentCloseButtonBehaviorName);
		SelectComboByTag(cmbCloseButtonBehavior, CloseButtonBehaviorName);
		WindowOpacityPercent = Math.Clamp(currentWindowOpacityPercent, 20, 100);
		HudOpacityPercent = Math.Clamp(currentHudOpacityPercent, 20, 100);
		sldWindowOpacity.Value = (_opacitySliderTargetsHud ? HudOpacityPercent : WindowOpacityPercent);
		UpdateOpacityText();
		DisplayPreset = currentDisplayPreset;
		DpsCardNumberFormatMode = currentDpsCardNumberFormatMode;
		DamageShareMode = currentDamageShareMode;
		DamageShareGraphMode = currentDamageShareGraphMode;
		UiScale = MeterScaleOptions.NormalizeUiScale(currentUiScale);
		TextScale = MeterScaleOptions.NormalizeTextScale(currentTextScale);
		sldUiScale.Value = UiScale * 100.0;
		sldTextScale.Value = TextScale * 100.0;
		UpdateScaleTexts();
		FontWeightMode = currentFontWeightMode;
		FontFamilyName = MeterFontFamilies.NormalizeForStorage(currentFontFamilyName);
		TextShadowEnabled = currentTextShadowEnabled;
		chkTextShadowEnabled.IsChecked = TextShadowEnabled;
		PopulateFontFamilyComboBox(FontFamilyName);
		CaptureBackend = currentCaptureBackend;
		SelectComboByTag(cmbDisplayPreset, DisplayPreset.ToString());
		SelectComboByTag(cmbDpsCardNumberFormatMode, DpsCardNumberFormatMode.ToString());
		SelectComboByTag(cmbDamageShareMode, DamageShareMode.ToString());
		SelectComboByTag(cmbDamageShareGraphMode, DamageShareGraphMode.ToString());
		SelectComboByTag(cmbFontWeightMode, FontWeightMode.ToString());
		SelectComboByTag(cmbFontFamily, FontFamilyName);
		SelectComboByTag(cmbCaptureBackend, CaptureBackend.ToString());
		base.FontFamily = MeterFontFamilies.CreateFontFamily(FontFamilyName);
		base.FontWeight = MeterFontWeights.Text(FontWeightMode);
		PopulateThemeComboBox();
		Theme = AppearanceCatalog.NormalizeLegacyThemeName(currentTheme);
		SelectComboByTag(cmbTheme, Theme);
		LookupSkillDisplayEnabled = currentLookupSkillDisplayEnabled;
		chkLookupSkillDisplayEnabled.IsChecked = LookupSkillDisplayEnabled;
		InitializeLookupSkillSelector();
		_engine = engine;
		_skillNames = skillNames;
		_characterConsentStates = characterConsentStates?.ToList() ?? new List<CharacterConsentState>();
		_setCharacterConsentAsync = setCharacterConsentAsync;
		_refreshCharacterConsentStates = refreshCharacterConsentStates;
		RenderCharacterConsentList();
		if (WebEndpoint.IsDeveloperSecurityKey(DevKey))
		{
			cardDev.Visibility = Visibility.Visible;
		}
		Version version = Assembly.GetExecutingAssembly().GetName().Version;
		txtVersion.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";
		_isInitializing = false;
	}

	private void SelectComboByTag(ComboBox cb, string tagValue)
	{
		foreach (object item in (IEnumerable)cb.Items)
		{
			if (item is ComboBoxItem { Tag: var tag } comboBoxItem && tag?.ToString() == tagValue)
			{
				cb.SelectedItem = comboBoxItem;
				break;
			}
		}
	}

	private void PopulateThemeComboBox()
	{
		cmbTheme.Items.Clear();
		foreach (AppearanceOption themeOption in AppearanceCatalog.ThemeOptions)
		{
			cmbTheme.Items.Add(new ComboBoxItem
			{
				Content = themeOption.DisplayName,
				Tag = themeOption.ThemeName
			});
		}
	}

	private void PopulateFontFamilyComboBox(string selectedFontFamily)
	{
		cmbFontFamily.Items.Clear();
		Dictionary<string, FontFamilyOption> options = new Dictionary<string, FontFamilyOption>(StringComparer.OrdinalIgnoreCase);
		AddFontFamilyOption("Malgun Gothic", "Malgun Gothic");
		string text = MeterFontFamilies.NormalizeForStorage(selectedFontFamily);
		AddFontFamilyOption(text, text);
		foreach (FontFamily item in EnumerateSystemFontFamiliesSafely())
		{
			try
			{
				string text2 = item.Source?.Trim() ?? "";
				if (!string.IsNullOrWhiteSpace(text2))
				{
					AddFontFamilyOption(text2, GetFontFamilyDisplayName(item));
				}
			}
			catch
			{
			}
		}
		foreach (FontFamilyOption item2 in options.Values.OrderBy<FontFamilyOption, string>((FontFamilyOption x) => x.DisplayName, StringComparer.CurrentCultureIgnoreCase))
		{
			AddFontFamilyItem(item2.Source, item2.DisplayName);
		}
		void AddFontFamilyItem(string source, string? displayName = null)
		{
			cmbFontFamily.Items.Add(new ComboBoxItem
			{
				Content = (string.IsNullOrWhiteSpace(displayName) ? source : displayName),
				Tag = source
			});
		}
		void AddFontFamilyOption(string source, string displayName)
		{
			source = MeterFontFamilies.NormalizeForStorage(source);
			displayName = (string.IsNullOrWhiteSpace(displayName) ? source : displayName.Trim());
			if (!options.TryGetValue(source, out FontFamilyOption value) || (string.Equals(value.DisplayName, value.Source, StringComparison.OrdinalIgnoreCase) && !string.Equals(displayName, source, StringComparison.OrdinalIgnoreCase)))
			{
				options[source] = new FontFamilyOption(source, displayName);
			}
		}
	}

	private static string GetSafeLogDirectoryText()
	{
		try
		{
			return EncounterLogStore.RootDirectory;
		}
		catch
		{
			return "전투 기록 경로를 확인할 수 없습니다.";
		}
	}

	private static IReadOnlyList<FontFamily> EnumerateSystemFontFamiliesSafely()
	{
		try
		{
			return Fonts.SystemFontFamilies.ToArray();
		}
		catch
		{
			return Array.Empty<FontFamily>();
		}
	}

	private static string GetFontFamilyDisplayName(FontFamily family)
	{
		XmlLanguage language = XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.IetfLanguageTag);
		if (family.FamilyNames.TryGetValue(language, out var value) && !string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		XmlLanguage language2 = XmlLanguage.GetLanguage("ko-kr");
		if (!family.FamilyNames.TryGetValue(language2, out value) || string.IsNullOrWhiteSpace(value))
		{
			return family.Source?.Trim() ?? "";
		}
		return value.Trim();
	}

	public void SetCaptureBackendSelection(CaptureBackend backend)
	{
		bool isInitializing = _isInitializing;
		_isInitializing = true;
		CaptureBackend = backend;
		SelectComboByTag(cmbCaptureBackend, backend.ToString());
		_isInitializing = isInitializing;
	}

	private void InitializeLookupSkillSelector()
	{
		if (_lookupSkillCatalog.Classes.Count == 0)
		{
			txtLookupSkillSummary.Text = "스킬 카탈로그를 불러오지 못했습니다.";
			return;
		}
		_selectedLookupSkillClassKey = _lookupSkillCatalog.Classes[0].Key;
		RenderLookupSkillSelector();
	}

	private void RenderLookupSkillSelector()
	{
		if (lookupClassPanel == null || lookupSkillSectionsPanel == null)
		{
			return;
		}
		lookupClassPanel.Children.Clear();
		foreach (LookupSkillClass @class in _lookupSkillCatalog.Classes)
		{
			int lookupSkillSelectedCount = GetLookupSkillSelectedCount(@class.Key);
			bool flag = IsLookupSkillClassEnabled(@class.Key);
			string text = ((lookupSkillSelectedCount > 0) ? $"{@class.Name} {lookupSkillSelectedCount}" : @class.Name);
			ToggleButton toggleButton = new ToggleButton
			{
				Content = (flag ? text : (text + " 끔")),
				Tag = @class.Key,
				IsChecked = string.Equals(_selectedLookupSkillClassKey, @class.Key, StringComparison.OrdinalIgnoreCase),
				Style = (TryFindResource("LookupClassSelectorStyle") as Style),
				ToolTip = ((lookupSkillSelectedCount > 0) ? $"{@class.Name} 선택 {lookupSkillSelectedCount}개" : (@class.Name + " 기본 표시"))
			};
			toggleButton.Click += LookupClassButton_Click;
			lookupClassPanel.Children.Add(toggleButton);
		}
		RenderLookupSkillSections();
		UpdateLookupSkillSelectionSummary();
	}

	private void RenderLookupSkillSections()
	{
		lookupSkillSectionsPanel.Children.Clear();
		LookupSkillClass lookupSkillClass = _lookupSkillCatalog.FindClassByKey(_selectedLookupSkillClassKey);
		if (lookupSkillClass == null)
		{
			return;
		}
		txtLookupSkillClassTitle.Text = lookupSkillClass.Name;
		chkLookupSkillClassEnabled.IsChecked = IsLookupSkillClassEnabled(lookupSkillClass.Key);
		txtLookupSkillClassCount.Text = $"{GetLookupSkillSelectedCount(lookupSkillClass.Key)}개 선택";
		foreach (LookupSkillCategory category in _lookupSkillCatalog.Categories)
		{
			IReadOnlyList<LookupSkillInfo> skills = lookupSkillClass.GetSkills(category.Key);
			if (skills.Count == 0)
			{
				continue;
			}
			StackPanel stackPanel = new StackPanel
			{
				Margin = new Thickness(0.0, 0.0, 0.0, 14.0)
			};
			stackPanel.Children.Add(new TextBlock
			{
				Text = $"{category.Name} {skills.Count}",
				Style = (TryFindResource("SettingLabel") as Style),
				Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
			});
			WrapPanel wrapPanel = new WrapPanel
			{
				Margin = new Thickness(0.0, 0.0, -6.0, -6.0)
			};
			foreach (LookupSkillInfo item in skills)
			{
				wrapPanel.Children.Add(CreateLookupSkillTile(lookupSkillClass.Key, item));
			}
			stackPanel.Children.Add(wrapPanel);
			lookupSkillSectionsPanel.Children.Add(stackPanel);
		}
	}

	private ToggleButton CreateLookupSkillTile(string classKey, LookupSkillInfo skill)
	{
		HashSet<int> value2;
		bool value = _lookupSkillSelections.TryGetValue(classKey, out value2) && value2.Contains(skill.Id);
		Image image = null;
		if (Uri.TryCreate(skill.Icon, UriKind.Absolute, out Uri result))
		{
			try
			{
				image = new Image
				{
					Width = 26.0,
					Height = 26.0,
					Stretch = Stretch.Uniform,
					Margin = new Thickness(0.0, 0.0, 0.0, 4.0),
					Source = new BitmapImage(result)
				};
			}
			catch
			{
				image = null;
			}
		}
		TextBlock textBlock = new TextBlock
		{
			Text = skill.Name,
			TextAlignment = TextAlignment.Center,
			TextTrimming = TextTrimming.CharacterEllipsis,
			FontSize = 11.0,
			MaxWidth = 92.0
		};
		textBlock.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimaryBrush");
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Vertical,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		if (image != null)
		{
			stackPanel.Children.Add(image);
		}
		stackPanel.Children.Add(textBlock);
		ToggleButton toggleButton = new ToggleButton();
		toggleButton.Content = stackPanel;
		toggleButton.Tag = new LookupSkillTileTag(classKey, skill.Id);
		toggleButton.IsChecked = value;
		toggleButton.Style = TryFindResource("LookupSkillTileStyle") as Style;
		toggleButton.ToolTip = $"{skill.CategoryName} · {skill.Name} · Lv{skill.NeedLevel}";
		toggleButton.Click += LookupSkillTile_Click;
		return toggleButton;
	}

	private void LookupClassButton_Click(object sender, RoutedEventArgs e)
	{
		if (sender is ToggleButton { Tag: string tag })
		{
			_selectedLookupSkillClassKey = tag;
			RenderLookupSkillSelector();
		}
	}

	private void LookupSkillTile_Click(object sender, RoutedEventArgs e)
	{
		if (sender is ToggleButton { Tag: LookupSkillTileTag tag } toggleButton)
		{
			if (!_lookupSkillSelections.TryGetValue(tag.ClassKey, out HashSet<int> value))
			{
				value = new HashSet<int>();
				_lookupSkillSelections[tag.ClassKey] = value;
			}
			if (toggleButton.IsChecked == true)
			{
				value.Add(tag.SkillId);
			}
			else
			{
				value.Remove(tag.SkillId);
			}
			if (value.Count == 0)
			{
				_lookupSkillSelections.Remove(tag.ClassKey);
			}
			RenderLookupSkillSelector();
			NotifySettingsChanged();
		}
	}

	private void LookupClassEnabled_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(_selectedLookupSkillClassKey))
		{
			if (chkLookupSkillClassEnabled.IsChecked == true)
			{
				_lookupSkillDisabledClasses.Remove(_selectedLookupSkillClassKey);
			}
			else
			{
				_lookupSkillDisabledClasses.Add(_selectedLookupSkillClassKey);
			}
			RenderLookupSkillSelector();
			NotifySettingsChanged();
		}
	}

	private void BtnClearCurrentLookupSkills_Click(object sender, RoutedEventArgs e)
	{
		if (!string.IsNullOrWhiteSpace(_selectedLookupSkillClassKey))
		{
			_lookupSkillSelections.Remove(_selectedLookupSkillClassKey);
		}
		RenderLookupSkillSelector();
		NotifySettingsChanged();
	}

	private void BtnClearAllLookupSkills_Click(object sender, RoutedEventArgs e)
	{
		_lookupSkillSelections.Clear();
		RenderLookupSkillSelector();
		NotifySettingsChanged();
	}

	private int GetLookupSkillSelectedCount(string classKey)
	{
		if (!_lookupSkillSelections.TryGetValue(classKey, out HashSet<int> value))
		{
			return 0;
		}
		return value.Count;
	}

	private bool IsLookupSkillClassEnabled(string classKey)
	{
		return !_lookupSkillDisabledClasses.Contains(classKey);
	}

	private void UpdateLookupSkillSelectionSummary()
	{
		int num = _lookupSkillSelections.Values.Sum((HashSet<int> set) => set.Count);
		int count = _lookupSkillDisabledClasses.Count;
		string text = ((num == 0) ? "선택 없음 · 현재 조회 표시 유지" : $"선택 {num}개 · 선택한 스킬만 우선 표시");
		txtLookupSkillSummary.Text = ((count == 0) ? text : $"{text} · 직업 {count}개 꺼짐");
	}

	private static string NormalizeCloseButtonBehaviorName(string? value)
	{
		switch (value?.Trim() ?? "")
		{
		case "MinimizeToTray":
		case "Tray":
		case "Minimize":
			return "MinimizeToTray";
		case "Exit":
		case "Close":
			return "Exit";
		default:
			return "Ask";
		}
	}

	private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
	{
		if (ThemedMessageBox.Show(this, "설정을 기본값으로 되돌릴까요?\n변경 내용은 즉시 적용됩니다.", "기본값 복원", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
		{
			ApplyDefaultValuesToControls();
			NotifySettingsChanged();
		}
	}

	private void ApplyDefaultValuesToControls()
	{
		_isInitializing = true;
		txtClearKey.Text = "Ctrl+R";
		txtHudKey.Text = "None";
		txtHideKey.Text = "None";
		txtClickThroughKey.Text = "None";
		txtMainViewKey.Text = "None";
		sldMaxDpsCards.Value = 10.0;
		chkShowActorId.IsChecked = false;
		chkUseDummyData.IsChecked = false;
		chkSaveEncounterLogs.IsChecked = true;
		WindowOpacityPercent = 100;
		HudOpacityPercent = 90;
		chkShowBossCard.IsChecked = true;
		chkShowDpsCardCombatTime.IsChecked = false;
		chkAutoHideBackground.IsChecked = false;
		chkShowOnlyWhenAionActive.IsChecked = false;
		chkShowInTaskbar.IsChecked = true;
		sldWindowOpacity.Value = (_opacitySliderTargetsHud ? HudOpacityPercent : WindowOpacityPercent);
		SelectComboByTag(cmbDisplayPreset, MeterDisplayPreset.Minimal.ToString());
		SelectComboByTag(cmbDpsCardNumberFormatMode, DpsCardNumberFormatMode.Full.ToString());
		SelectComboByTag(cmbDamageShareMode, DamageShareMode.BossHpPercent.ToString());
		SelectComboByTag(cmbDamageShareGraphMode, DamageShareGraphMode.RelativeTop.ToString());
		sldUiScale.Value = 96.0;
		sldTextScale.Value = 110.00000000000001;
		SelectComboByTag(cmbFontWeightMode, MeterFontWeightMode.Normal.ToString());
		SelectComboByTag(cmbFontFamily, "Malgun Gothic");
		chkTextShadowEnabled.IsChecked = true;
		SelectComboByTag(cmbCloseButtonBehavior, "Ask");
		SelectComboByTag(cmbCaptureBackend, CaptureBackend.WinDivert.ToString());
		SelectComboByTag(cmbTheme, AppearanceSelection.Default.ResourceThemeName);
		_lookupSkillSelections.Clear();
		_lookupSkillDisabledClasses.Clear();
		RenderLookupSkillSelector();
		txtDevKey.Password = "";
		cardDev.Visibility = Visibility.Collapsed;
		_packetLogWindow?.Close();
		_packetLogWindow = null;
		UpdateOpacityText();
		UpdateMaxDpsCardsText();
		UpdateScaleTexts();
		_isInitializing = false;
		ApplyValuesFromControls();
		UiAppearanceManager.ApplyLegacyTheme(Theme);
	}

	private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		e.Handled = true;
		if (!(sender is TextBox textBox))
		{
			return;
		}
		Key key = ((e.Key == Key.System) ? e.SystemKey : e.Key);
		if (key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.None)
		{
			stringBuilder.Append("Ctrl+");
		}
		if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.None)
		{
			stringBuilder.Append("Shift+");
		}
		if ((Keyboard.Modifiers & ModifierKeys.Alt) != ModifierKeys.None)
		{
			stringBuilder.Append("Alt+");
		}
		stringBuilder.Append(key.ToString());
		string text = stringBuilder.ToString();
		TextBox[] array = new TextBox[5] { txtClearKey, txtHudKey, txtHideKey, txtClickThroughKey, txtMainViewKey };
		foreach (TextBox textBox2 in array)
		{
			if (textBox2 != textBox && textBox2.Text == text)
			{
				textBox2.Text = "None";
			}
		}
		textBox.Text = text;
		NotifySettingsChanged();
	}

	private void BtnClearClearKey_Click(object sender, RoutedEventArgs e)
	{
		txtClearKey.Text = "None";
		NotifySettingsChanged();
	}

	private void BtnClearHudKey_Click(object sender, RoutedEventArgs e)
	{
		txtHudKey.Text = "None";
		NotifySettingsChanged();
	}

	private void BtnClearHideKey_Click(object sender, RoutedEventArgs e)
	{
		txtHideKey.Text = "None";
		NotifySettingsChanged();
	}

	private void BtnClearClickThroughKey_Click(object sender, RoutedEventArgs e)
	{
		txtClickThroughKey.Text = "None";
		NotifySettingsChanged();
	}

	private void BtnClearMainViewKey_Click(object sender, RoutedEventArgs e)
	{
		txtMainViewKey.Text = "None";
		NotifySettingsChanged();
	}

	private void Header_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left)
		{
			DragMove();
		}
	}

	private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			Close();
		}
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
	{
		if (_updateService.State.IsChecking || _updateService.State.IsDownloading)
		{
			return;
		}
		Button button = sender as Button;
		object originalContent = button?.Content;
		PropertyChangedEventHandler progressHandler = delegate(object? _, PropertyChangedEventArgs args)
		{
			if (!(args.PropertyName != "DownloadProgress") || !(args.PropertyName != "IsDownloading"))
			{
				base.Dispatcher.BeginInvoke((Action)delegate
				{
					if (_updateService.State.IsDownloading)
					{
						SetButtonText($"다운로드 {_updateService.State.DownloadProgress}%");
					}
				});
			}
		};
		try
		{
			if (button != null)
			{
				button.IsEnabled = false;
			}
			SetButtonText("확인 중");
			await _updateService.CheckAsync(notifyWhenCurrent: true);
			AppUpdateState state = _updateService.State;
			if (!state.IsVelopackInstalled)
			{
				ThemedMessageBox.Show(this, "설치본에서만 자동 업데이트를 사용할 수 있습니다.", "업데이트 확인");
				return;
			}
			if (!state.IsUpdateAvailable)
			{
				ThemedMessageBox.Show(this, "현재 최신 버전입니다.", "업데이트 확인");
				return;
			}
			if (!state.IsReadyToInstall)
			{
				if (ThemedMessageBox.Show(this, "새 버전 v" + state.LatestVersion + "이 있습니다.\n지금 다운로드 후 설치하고 재시작할까요?", "업데이트", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
				{
					return;
				}
				_updateService.State.PropertyChanged += progressHandler;
				SetButtonText("다운로드 0%");
				await _updateService.DownloadAsync();
			}
			if (_updateService.State.IsReadyToInstall)
			{
				_updateService.ApplyAndRestart();
			}
		}
		finally
		{
			_updateService.State.PropertyChanged -= progressHandler;
			if (button != null)
			{
				button.Content = originalContent;
				button.IsEnabled = true;
			}
		}
		void SetButtonText(string text)
		{
			if (button != null)
			{
				button.Content = text;
			}
		}
	}

	private void BtnPacketLog_Click(object sender, RoutedEventArgs e)
	{
		if (!WebEndpoint.IsDeveloperSecurityKey(txtDevKey.Password))
		{
			cardDev.Visibility = Visibility.Collapsed;
			return;
		}
		if (_engine == null || _skillNames == null)
		{
			ThemedMessageBox.Show(this, "엔진이 초기화되지 않았습니다.");
			return;
		}
		PacketLogWindow? packetLogWindow = _packetLogWindow;
		if (packetLogWindow != null && packetLogWindow.IsVisible)
		{
			_packetLogWindow.Activate();
			return;
		}
		_packetLogWindow = new PacketLogWindow(_engine, _skillNames);
		_packetLogWindow.Closed += delegate
		{
			_packetLogWindow = null;
		};
		_packetLogWindow.Show();
	}

	private void RenderCharacterConsentList()
	{
		panelCharacterConsent.Children.Clear();
		if (_characterConsentStates.Count == 0)
		{
			panelCharacterConsent.Children.Add(new TextBlock
			{
				Text = "아직 인식된 로컬 캐릭터가 없습니다. 게임 접속 후 캐릭터가 인식되면 여기에 표시됩니다.",
				Style = (TryFindResource("CardDesc") as Style)
			});
			return;
		}
		foreach (CharacterConsentState characterConsentState in _characterConsentStates)
		{
			panelCharacterConsent.Children.Add(CreateCharacterConsentRow(characterConsentState));
		}
	}

	private Border CreateCharacterConsentRow(CharacterConsentState state)
	{
		Border border = new Border();
		border.BorderThickness = new Thickness(1.0);
		border.CornerRadius = new CornerRadius(6.0);
		border.Padding = new Thickness(10.0);
		border.Margin = new Thickness(0.0, 0.0, 0.0, 8.0);
		border.SetResourceReference(Border.BackgroundProperty, "ThemePanelBackgroundBrush");
		border.SetResourceReference(Border.BorderBrushProperty, "ThemeBorderBrush");
		Grid grid = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = new GridLength(1.0, GridUnitType.Star)
				},
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		StackPanel stackPanel = new StackPanel
		{
			VerticalAlignment = VerticalAlignment.Center
		};
		TextBlock textBlock = new TextBlock
		{
			Text = state.CharacterName + " [" + state.ServerName + "]",
			FontWeight = FontWeights.SemiBold,
			FontSize = 13.0
		};
		textBlock.SetResourceReference(TextBlock.ForegroundProperty, "ThemeTextPrimaryBrush");
		stackPanel.Children.Add(textBlock);
		stackPanel.Children.Add(CreateCharacterConsentStatusBadge(state.PublicConsent));
		Grid.SetColumn(stackPanel, 0);
		grid.Children.Add(stackPanel);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(12.0, 0.0, 0.0, 0.0)
		};
		Button button = CreateCharacterConsentButton("동의", state.PublicConsent != true);
		button.Click += async delegate
		{
			await SetCharacterConsentFromSettingsAsync(state, publicConsent: true);
		};
		stackPanel2.Children.Add(button);
		Button button2 = CreateCharacterConsentButton("철회", state.PublicConsent != false);
		button2.Margin = new Thickness(6.0, 0.0, 0.0, 0.0);
		button2.Click += async delegate
		{
			await SetCharacterConsentFromSettingsAsync(state, publicConsent: false);
		};
		stackPanel2.Children.Add(button2);
		Grid.SetColumn(stackPanel2, 1);
		grid.Children.Add(stackPanel2);
		border.Child = grid;
		return border;
	}

	private Border CreateCharacterConsentStatusBadge(bool? publicConsent)
	{
		Color color = ((!publicConsent.HasValue) ? Color.FromRgb(150, 158, 174) : ((publicConsent != true) ? Color.FromRgb(238, 100, 100) : Color.FromRgb(53, 208, 127)));
		Color color2 = color;
		return new Border
		{
			Background = new SolidColorBrush(Color.FromArgb(38, color2.R, color2.G, color2.B)),
			BorderBrush = new SolidColorBrush(Color.FromArgb(150, color2.R, color2.G, color2.B)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(4.0),
			Padding = new Thickness(7.0, 2.0, 7.0, 3.0),
			Margin = new Thickness(0.0, 6.0, 0.0, 0.0),
			HorizontalAlignment = HorizontalAlignment.Left,
			Child = new TextBlock
			{
				Text = GetCharacterConsentStatusText(publicConsent),
				FontSize = 11.0,
				FontWeight = FontWeights.SemiBold,
				Foreground = new SolidColorBrush(color2)
			}
		};
	}

	private Button CreateCharacterConsentButton(string text, bool isEnabled)
	{
		return new Button
		{
			Content = text,
			MinWidth = 62.0,
			Height = 30.0,
			Padding = new Thickness(10.0, 0.0, 10.0, 0.0),
			Cursor = Cursors.Hand,
			IsEnabled = isEnabled,
			Style = (TryFindResource("WindowBtn") as Style)
		};
	}

	private async Task SetCharacterConsentFromSettingsAsync(CharacterConsentState state, bool publicConsent)
	{
		if (_setCharacterConsentAsync == null)
		{
			ThemedMessageBox.Show(this, "캐릭터 공개 설정을 저장할 수 없습니다.", "캐릭터 공개 설정", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			return;
		}
		panelCharacterConsent.IsEnabled = false;
		btnRefreshCharacterConsent.IsEnabled = false;
		bool flag;
		try
		{
			flag = await _setCharacterConsentAsync(state, publicConsent);
		}
		finally
		{
			panelCharacterConsent.IsEnabled = true;
			btnRefreshCharacterConsent.IsEnabled = true;
		}
		if (!flag)
		{
			ThemedMessageBox.Show(this, "동의 상태를 저장하지 못했습니다. 잠시 후 다시 시도해 주세요.", "캐릭터 공개 설정", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
		else
		{
			RefreshCharacterConsentStates();
		}
	}

	private void BtnRefreshCharacterConsent_Click(object sender, RoutedEventArgs e)
	{
		RefreshCharacterConsentStates();
	}

	private void RefreshCharacterConsentStates()
	{
		if (_refreshCharacterConsentStates != null)
		{
			_characterConsentStates = _refreshCharacterConsentStates().ToList();
		}
		RenderCharacterConsentList();
	}

	private static string GetCharacterConsentStatusText(bool? publicConsent)
	{
		if (publicConsent.HasValue)
		{
			if (publicConsent == true)
			{
				return "공개 동의";
			}
			return "비공개";
		}
		return "미확인";
	}

	private void TxtDevKey_PasswordChanged(object sender, RoutedEventArgs e)
	{
		if (cardDev != null)
		{
			if (WebEndpoint.IsDeveloperSecurityKey(txtDevKey.Password))
			{
				cardDev.Visibility = Visibility.Visible;
			}
			else
			{
				cardDev.Visibility = Visibility.Collapsed;
				_packetLogWindow?.Close();
				_packetLogWindow = null;
			}
			NotifySettingsChanged();
		}
	}

	private void BtnNameLog_Click(object sender, RoutedEventArgs e)
	{
		if (_engine == null)
		{
			ThemedMessageBox.Show(this, "엔진이 초기화되지 않았습니다.");
		}
		else
		{
			new NameLogWindow(_engine.Names).Show();
		}
	}

	private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
	{
		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = EncounterLogStore.RootDirectory,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			ThemedMessageBox.Show(this, "로그 폴더를 열 수 없습니다.\n" + ex.Message, "로그 관리", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
	{
		string[] obj = new string[3]
		{
			EncounterLogStore.RootDirectory,
			AppPaths.LogsDirectory,
			Path.Combine(AppPaths.AppRootDirectory, "BossRecords")
		};
		List<string> list = new List<string>();
		int num = 0;
		string[] array = obj;
		foreach (string path in array)
		{
			if (!Directory.Exists(path))
			{
				continue;
			}
			string[] files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
			foreach (string text in files)
			{
				if (IsFileLocked(text))
				{
					num++;
				}
				else
				{
					list.Add(text);
				}
			}
		}
		if (list.Count == 0)
		{
			if (num > 0)
			{
				ThemedMessageBox.Show(this, $"현재 사용 중인 로그 파일 {num}개를 제외하고 삭제할 로그 파일이 없습니다.", "로그 삭제");
			}
			else
			{
				ThemedMessageBox.Show(this, "삭제할 로그 파일이 없습니다.", "로그 삭제");
			}
			return;
		}
		string text2 = $"저장된 전투 로그 파일 {list.Count}개를 삭제하시겠습니까?\n\n삭제 위치:\n{EncounterLogStore.RootDirectory}";
		if (num > 0)
		{
			text2 += $"\n\n현재 사용 중인 파일 {num}개는 제외됩니다.";
		}
		if (ThemedMessageBox.Show(this, text2, "로그 삭제 확인", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			return;
		}
		int num2 = 0;
		foreach (string item in list)
		{
			try
			{
				File.Delete(item);
				num2++;
			}
			catch
			{
			}
		}
		ThemedMessageBox.Show(this, $"{num2}개의 로그 파일을 삭제했습니다.", "완료");
	}

	private bool IsFileLocked(string filePath)
	{
		try
		{
			using (File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
			{
				return false;
			}
		}
		catch (IOException)
		{
			return true;
		}
		catch
		{
			return true;
		}
	}

	private void ApplyValuesFromControls()
	{
		ClearHotkey = (string.IsNullOrWhiteSpace(txtClearKey.Text) ? "None" : txtClearKey.Text);
		HudHotkey = "None";
		HideHotkey = (string.IsNullOrWhiteSpace(txtHideKey.Text) ? "None" : txtHideKey.Text);
		ClickThroughHotkey = (string.IsNullOrWhiteSpace(txtClickThroughKey.Text) ? "None" : txtClickThroughKey.Text);
		MainViewHotkey = (string.IsNullOrWhiteSpace(txtMainViewKey.Text) ? "None" : txtMainViewKey.Text);
		MaxDpsCards = ReadMaxDpsCardsSliderValue();
		if (Math.Abs(sldMaxDpsCards.Value - (double)MaxDpsCards) > 0.01)
		{
			sldMaxDpsCards.Value = MaxDpsCards;
		}
		UpdateMaxDpsCardsText();
		ShowActorId = chkShowActorId.IsChecked == true;
		UseDummyData = chkUseDummyData.IsChecked == true;
		SaveEncounterLogs = chkSaveEncounterLogs.IsChecked == true;
		ShowBossCard = chkShowBossCard.IsChecked == true;
		ShowDpsCardCombatTime = chkShowDpsCardCombatTime.IsChecked == true;
		AutoHideBackground = chkAutoHideBackground.IsChecked == true;
		ShowOnlyWhenAionActive = chkShowOnlyWhenAionActive.IsChecked == true;
		ShowAppInTaskbar = chkShowInTaskbar.IsChecked == true;
		int num = Math.Clamp((int)Math.Round(sldWindowOpacity.Value), 20, 100);
		if (_opacitySliderTargetsHud)
		{
			HudOpacityPercent = num;
		}
		else
		{
			WindowOpacityPercent = num;
		}
		if (cmbDisplayPreset.SelectedItem is ComboBoxItem { Tag: var tag } && Enum.TryParse<MeterDisplayPreset>(tag?.ToString(), out var result))
		{
			DisplayPreset = result;
		}
		if (cmbDpsCardNumberFormatMode.SelectedItem is ComboBoxItem { Tag: var tag2 } && Enum.TryParse<DpsCardNumberFormatMode>(tag2?.ToString(), out var result2))
		{
			DpsCardNumberFormatMode = result2;
		}
		if (cmbDamageShareMode.SelectedItem is ComboBoxItem { Tag: var tag3 } && Enum.TryParse<DamageShareMode>(tag3?.ToString(), out var result3))
		{
			DamageShareMode = result3;
		}
		if (cmbDamageShareGraphMode.SelectedItem is ComboBoxItem { Tag: var tag4 } && Enum.TryParse<DamageShareGraphMode>(tag4?.ToString(), out var result4))
		{
			DamageShareGraphMode = result4;
		}
		UiScale = MeterScaleOptions.NormalizeUiScale(sldUiScale.Value / 100.0);
		TextScale = MeterScaleOptions.NormalizeTextScale(sldTextScale.Value / 100.0);
		SyncScaleSlider(sldUiScale, UiScale);
		SyncScaleSlider(sldTextScale, TextScale);
		UpdateScaleTexts();
		if (cmbFontWeightMode.SelectedItem is ComboBoxItem { Tag: var tag5 } && Enum.TryParse<MeterFontWeightMode>(tag5?.ToString(), out var result5))
		{
			FontWeightMode = result5;
			base.FontWeight = MeterFontWeights.Text(FontWeightMode);
		}
		if (cmbFontFamily.SelectedItem is ComboBoxItem comboBoxItem6)
		{
			FontFamilyName = MeterFontFamilies.NormalizeForStorage(comboBoxItem6.Tag?.ToString());
			base.FontFamily = MeterFontFamilies.CreateFontFamily(FontFamilyName);
		}
		TextShadowEnabled = chkTextShadowEnabled.IsChecked == true;
		if (cmbCloseButtonBehavior.SelectedItem is ComboBoxItem comboBoxItem7)
		{
			CloseButtonBehaviorName = NormalizeCloseButtonBehaviorName(comboBoxItem7.Tag?.ToString());
		}
		if (cmbCaptureBackend.SelectedItem is ComboBoxItem { Tag: var tag6 } && Enum.TryParse<CaptureBackend>(tag6?.ToString(), out var result6))
		{
			CaptureBackend = result6;
		}
		DevKey = txtDevKey.Password;
		if (cmbTheme.SelectedItem is ComboBoxItem comboBoxItem9)
		{
			Theme = AppearanceCatalog.NormalizeLegacyThemeName(comboBoxItem9.Tag?.ToString());
		}
		LookupSkillDisplayEnabled = chkLookupSkillDisplayEnabled.IsChecked == true;
		LookupSkillSelections = LookupSkillSelectionSerializer.Clone(_lookupSkillSelections);
		LookupSkillDisabledClasses = LookupSkillClassSetSerializer.Clone(_lookupSkillDisabledClasses);
	}

	private void UpdateOpacityText()
	{
		if (txtWindowOpacityValue != null && sldWindowOpacity != null)
		{
			txtWindowOpacityValue.Text = $"{Math.Clamp((int)Math.Round(sldWindowOpacity.Value), 20, 100)}%";
		}
	}

	private void UpdateScaleTexts()
	{
		if (txtUiScaleValue != null && sldUiScale != null)
		{
			txtUiScaleValue.Text = $"{ReadScalePercent(sldUiScale, 0.75, 1.3)}%";
		}
		if (txtTextScaleValue != null && sldTextScale != null)
		{
			txtTextScaleValue.Text = $"{ReadScalePercent(sldTextScale, 0.6, 1.4)}%";
		}
	}

	private void UpdateMaxDpsCardsText()
	{
		if (txtMaxDpsCardsValue != null && sldMaxDpsCards != null)
		{
			txtMaxDpsCardsValue.Text = $"{ReadMaxDpsCardsSliderValue()}명";
		}
	}

	private int ReadMaxDpsCardsSliderValue()
	{
		return Math.Clamp((int)Math.Round(sldMaxDpsCards.Value), 1, 10);
	}

	private static int ReadScalePercent(Slider slider, double minScale, double maxScale)
	{
		return Math.Clamp((int)Math.Round(slider.Value), (int)Math.Round(minScale * 100.0), (int)Math.Round(maxScale * 100.0));
	}

	private static void SyncScaleSlider(Slider slider, double scale)
	{
		double num = scale * 100.0;
		if (Math.Abs(slider.Value - num) > 0.01)
		{
			slider.Value = num;
		}
	}

	private void NotifySettingsChanged()
	{
		if (!_isInitializing)
		{
			ApplyValuesFromControls();
			this.SettingsChanged?.Invoke(this);
		}
	}

	private void BtnSave_Click(object sender, RoutedEventArgs e)
	{
		NotifySettingsChanged();
		Close();
	}

	private void SettingsValueChanged(object sender, RoutedEventArgs e)
	{
		if (sender == chkSaveEncounterLogs && chkSaveEncounterLogs.IsChecked == false && ThemedMessageBox.Show(this, "보스별 로그 저장을 끄면 앞으로 종료되는 전투의 압축 기록이 로컬에 저장되지 않습니다.\n\n저장되지 않은 전투는 프로그램의 이전기록/전투 로그 불러오기에서 다시 볼 수 없습니다.\n이미 저장된 기록은 삭제되지 않습니다.\n\n그래도 끄시겠습니까?", "이전기록 저장 끄기", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
		{
			chkSaveEncounterLogs.IsChecked = true;
		}
		else
		{
			NotifySettingsChanged();
		}
	}

	private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		UpdateOpacityText();
		NotifySettingsChanged();
	}

	private void MaxDpsCardsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		UpdateMaxDpsCardsText();
		NotifySettingsChanged();
	}

	private void UiScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		UpdateScaleTexts();
		NotifySettingsChanged();
	}

	private void TextScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
	{
		UpdateScaleTexts();
		NotifySettingsChanged();
	}

	private void cmbTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (cmbTheme.SelectedItem is ComboBoxItem { Tag: var tag })
		{
			string themeName = (Theme = AppearanceCatalog.NormalizeLegacyThemeName(tag?.ToString()));
			UiAppearanceManager.ApplyLegacyTheme(themeName);
		}
		NotifySettingsChanged();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/settingswindow.xaml", UriKind.Relative);
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
			((SettingsWindow)target).PreviewKeyDown += Window_PreviewKeyDown;
			break;
		case 2:
			((Border)target).MouseDown += Header_MouseDown;
			break;
		case 3:
			((Button)target).Click += BtnClose_Click;
			break;
		case 4:
			cmbDisplayPreset = (ComboBox)target;
			cmbDisplayPreset.SelectionChanged += SettingsValueChanged;
			break;
		case 5:
			cmbDamageShareMode = (ComboBox)target;
			cmbDamageShareMode.SelectionChanged += SettingsValueChanged;
			break;
		case 6:
			cmbDamageShareGraphMode = (ComboBox)target;
			cmbDamageShareGraphMode.SelectionChanged += SettingsValueChanged;
			break;
		case 7:
			cmbDpsCardNumberFormatMode = (ComboBox)target;
			cmbDpsCardNumberFormatMode.SelectionChanged += SettingsValueChanged;
			break;
		case 8:
			chkShowBossCard = (CheckBox)target;
			chkShowBossCard.Click += SettingsValueChanged;
			break;
		case 9:
			chkShowDpsCardCombatTime = (CheckBox)target;
			chkShowDpsCardCombatTime.Click += SettingsValueChanged;
			break;
		case 10:
			sldMaxDpsCards = (Slider)target;
			sldMaxDpsCards.ValueChanged += MaxDpsCardsSlider_ValueChanged;
			break;
		case 11:
			txtMaxDpsCardsValue = (TextBlock)target;
			break;
		case 12:
			cmbTheme = (ComboBox)target;
			cmbTheme.SelectionChanged += cmbTheme_SelectionChanged;
			break;
		case 13:
			sldUiScale = (Slider)target;
			sldUiScale.ValueChanged += UiScaleSlider_ValueChanged;
			break;
		case 14:
			txtUiScaleValue = (TextBlock)target;
			break;
		case 15:
			sldWindowOpacity = (Slider)target;
			sldWindowOpacity.ValueChanged += OpacitySlider_ValueChanged;
			break;
		case 16:
			txtWindowOpacityValue = (TextBlock)target;
			break;
		case 17:
			chkAutoHideBackground = (CheckBox)target;
			chkAutoHideBackground.Click += SettingsValueChanged;
			break;
		case 18:
			sldTextScale = (Slider)target;
			sldTextScale.ValueChanged += TextScaleSlider_ValueChanged;
			break;
		case 19:
			txtTextScaleValue = (TextBlock)target;
			break;
		case 20:
			cmbFontWeightMode = (ComboBox)target;
			cmbFontWeightMode.SelectionChanged += SettingsValueChanged;
			break;
		case 21:
			cmbFontFamily = (ComboBox)target;
			cmbFontFamily.SelectionChanged += SettingsValueChanged;
			break;
		case 22:
			chkTextShadowEnabled = (CheckBox)target;
			chkTextShadowEnabled.Click += SettingsValueChanged;
			break;
		case 23:
			chkLookupSkillDisplayEnabled = (CheckBox)target;
			chkLookupSkillDisplayEnabled.Click += SettingsValueChanged;
			break;
		case 24:
			txtLookupSkillSummary = (TextBlock)target;
			break;
		case 25:
			((Button)target).Click += BtnClearAllLookupSkills_Click;
			break;
		case 26:
			lookupClassPanel = (WrapPanel)target;
			break;
		case 27:
			txtLookupSkillClassTitle = (TextBlock)target;
			break;
		case 28:
			chkLookupSkillClassEnabled = (CheckBox)target;
			chkLookupSkillClassEnabled.Click += LookupClassEnabled_Click;
			break;
		case 29:
			txtLookupSkillClassCount = (TextBlock)target;
			break;
		case 30:
			((Button)target).Click += BtnClearCurrentLookupSkills_Click;
			break;
		case 31:
			lookupSkillSectionsPanel = (StackPanel)target;
			break;
		case 32:
			txtClearKey = (TextBox)target;
			txtClearKey.PreviewKeyDown += TextBox_PreviewKeyDown;
			break;
		case 33:
			((Button)target).Click += BtnClearClearKey_Click;
			break;
		case 34:
			txtHudKey = (TextBox)target;
			txtHudKey.PreviewKeyDown += TextBox_PreviewKeyDown;
			break;
		case 35:
			((Button)target).Click += BtnClearHudKey_Click;
			break;
		case 36:
			txtHideKey = (TextBox)target;
			txtHideKey.PreviewKeyDown += TextBox_PreviewKeyDown;
			break;
		case 37:
			((Button)target).Click += BtnClearHideKey_Click;
			break;
		case 38:
			txtClickThroughKey = (TextBox)target;
			txtClickThroughKey.PreviewKeyDown += TextBox_PreviewKeyDown;
			break;
		case 39:
			((Button)target).Click += BtnClearClickThroughKey_Click;
			break;
		case 40:
			txtMainViewKey = (TextBox)target;
			txtMainViewKey.PreviewKeyDown += TextBox_PreviewKeyDown;
			break;
		case 41:
			((Button)target).Click += BtnClearMainViewKey_Click;
			break;
		case 42:
			chkShowOnlyWhenAionActive = (CheckBox)target;
			chkShowOnlyWhenAionActive.Click += SettingsValueChanged;
			break;
		case 43:
			chkShowInTaskbar = (CheckBox)target;
			chkShowInTaskbar.Click += SettingsValueChanged;
			break;
		case 44:
			cmbCloseButtonBehavior = (ComboBox)target;
			cmbCloseButtonBehavior.SelectionChanged += SettingsValueChanged;
			break;
		case 45:
			cmbCaptureBackend = (ComboBox)target;
			cmbCaptureBackend.SelectionChanged += SettingsValueChanged;
			break;
		case 46:
			chkSaveEncounterLogs = (CheckBox)target;
			chkSaveEncounterLogs.Click += SettingsValueChanged;
			break;
		case 47:
			txtLogDirectory = (TextBlock)target;
			break;
		case 48:
			((Button)target).Click += BtnOpenLogFolder_Click;
			break;
		case 49:
			btnClearLogs = (Button)target;
			btnClearLogs.Click += BtnClearLogs_Click;
			break;
		case 50:
			txtDevKey = (PasswordBox)target;
			txtDevKey.PasswordChanged += TxtDevKey_PasswordChanged;
			break;
		case 51:
			cardDev = (Border)target;
			break;
		case 52:
			chkShowActorId = (CheckBox)target;
			chkShowActorId.Click += SettingsValueChanged;
			break;
		case 53:
			chkUseDummyData = (CheckBox)target;
			chkUseDummyData.Click += SettingsValueChanged;
			break;
		case 54:
			((Button)target).Click += BtnPacketLog_Click;
			break;
		case 55:
			((Button)target).Click += BtnNameLog_Click;
			break;
		case 56:
			btnRefreshCharacterConsent = (Button)target;
			btnRefreshCharacterConsent.Click += BtnRefreshCharacterConsent_Click;
			break;
		case 57:
			panelCharacterConsent = (StackPanel)target;
			break;
		case 58:
			txtVersion = (TextBlock)target;
			break;
		case 59:
			((Button)target).Click += BtnCheckUpdate_Click;
			break;
		case 60:
			((Button)target).Click += BtnResetDefaults_Click;
			break;
		case 61:
			((Button)target).Click += BtnSave_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
