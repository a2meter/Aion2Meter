using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows.Forms;

namespace A2Meter.Core;

/// Persisted application settings. Singleton. Backend-only fields
/// (skill catalog, uploader id) will be added when those modules land.
internal sealed class AppSettings
{
    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static AppSettings? _instance;
    public static AppSettings Instance => _instance ??= Load();

    // ── overlay behavior ──
    public bool OverlayOnlyWhenAion { get; set; } = true;
    public string GpuMode { get; set; } = "on";   // "on" | "off"
    public bool GpuModeUserOverride { get; set; }

    // ── visual ──
    public int Opacity { get; set; } = 90;       // 0..100  — background opacity
    public int BarOpacity { get; set; } = 100;     // 0..100  — DPS bar opacity
    public string FontName { get; set; } = "Malgun Gothic";
    public int FontWeight { get; set; } = 400;  // 100~900 (Normal=400, Bold=700)
    public float FontSize { get; set; } = 9f;

    /// UI theme colors as hex (#RRGGBB). null entries = built-in default.
    public ThemeColors Theme { get; set; } = new();

    internal sealed class ThemeColors
    {
        public string Background { get; set; } = "#1E1E2A";  // form bg
        public string Header     { get; set; } = "#252535";  // header bg
        public string Border     { get; set; } = "#3A3A4A";  // border/dividers
        public string TextPrimary   { get; set; } = "#D5D5DB"; // main text
        public string TextSecondary { get; set; } = "#C2C2CD"; // dim text
        public string Accent     { get; set; } = "#4DE8E0";  // accent/highlight
        public string Elyos      { get; set; } = "#8CD1FF";  // 천족 이름
        public string Asmodian   { get; set; } = "#C2A6FF";  // 마족 이름

        // ── helpers (not serialized) ──
        [JsonIgnore] public System.Drawing.Color BgColor      => ParseHex(Background);
        [JsonIgnore] public System.Drawing.Color HeaderColor  => ParseHex(Header);
        [JsonIgnore] public System.Drawing.Color BorderColor  => ParseHex(Border);
        [JsonIgnore] public System.Drawing.Color TextColor    => ParseHex(TextPrimary);
        [JsonIgnore] public System.Drawing.Color TextDimColor => ParseHex(TextSecondary);
        [JsonIgnore] public System.Drawing.Color AccentColor  => ParseHex(Accent);
        [JsonIgnore] public System.Drawing.Color ElyosColor   => ParseHex(Elyos);
        [JsonIgnore] public System.Drawing.Color AsmodianColor => ParseHex(Asmodian);

        public static System.Drawing.Color ParseHex(string hex)
        {
            try { return System.Drawing.ColorTranslator.FromHtml(hex); }
            catch { return System.Drawing.Color.Gray; }
        }
    }
    public int FontScale { get; set; } = 100;
    public int RowHeight { get; set; } = 90;

    // ── snap-to-edge ──
    public bool SnapEnabled { get; set; } = true;
    public int SnapDistance { get; set; } = 8;  // 1..8 px

    // ── shortcuts ──
    public ShortcutSettings Shortcuts { get; set; } = new();

    // ── DPS panel preferences ──
    public string DpsPercentMode { get; set; } = "boss"; // "boss" | "party"
    public string NumberFormat { get; set; } = "full";  // "full" | "abbreviated"

    // ── toggle display ──
    public bool ShowCombatPower { get; set; } = true;
    public bool ShowCombatScore { get; set; } = true;

    // ── 직업별 DPS 바 색상 (hex) — null이면 기본값 사용 ──
    public JobBarColorSettings JobBarColors { get; set; } = new();

    internal sealed class JobBarColorSettings
    {
        public string 검성  { get; set; } = "#86DDF3";
        public string 궁성  { get; set; } = "#62B18F";
        public string 마도성 { get; set; } = "#B78CF2";
        public string 살성  { get; set; } = "#A4E79B";
        public string 수호성 { get; set; } = "#7DA0F9";
        public string 정령성 { get; set; } = "#CF6BD0";
        public string 치유성 { get; set; } = "#E7CF7D";
        public string 호법성 { get; set; } = "#E4A55B";
        public string 권성  { get; set; } = "#00B3C7";

        public string GetHex(string jobName) => jobName switch
        {
            "검성"  => 검성,
            "궁성"  => 궁성,
            "마도성" => 마도성,
            "살성"  => 살성,
            "수호성" => 수호성,
            "정령성" => 정령성,
            "치유성" => 치유성,
            "호법성" => 호법성,
            "권성"  => 권성,
            _       => "#B3B3B3",
        };

        public void SetHex(string jobName, string hex)
        {
            switch (jobName)
            {
                case "검성":  검성 = hex; break;
                case "궁성":  궁성 = hex; break;
                case "마도성": 마도성 = hex; break;
                case "살성":  살성 = hex; break;
                case "수호성": 수호성 = hex; break;
                case "정령성": 정령성 = hex; break;
                case "치유성": 치유성 = hex; break;
                case "호법성": 호법성 = hex; break;
                case "권성":  권성 = hex; break;
            }
        }
    }

    // ── DPS bar layout: 3 configurable slots ──
    public BarSlotConfig BarSlot1 { get; set; } = new() { Content = "damage", FontSize = 8f, Color = "#DADADE" };
    public BarSlotConfig BarSlot2 { get; set; } = new() { Content = "dps",  FontSize = 8f, Color = "#DADADE" };
    public BarSlotConfig BarSlot3 { get; set; } = new() { Content = "percent",     FontSize = 9f, Color = "#E8C84D" };

    // ── secondary windows ──
    public int DetailPanelX { get; set; } = -1;
    public int DetailPanelY { get; set; } = -1;
    public int DetailPanelWidth { get; set; } = 900;
    public int DetailPanelHeight { get; set; } = 400;
    public int CombatRecordsPanelX { get; set; } = -1;
    public int CombatRecordsPanelY { get; set; } = -1;
    public int CombatRecordsPanelWidth { get; set; } = 620;
    public int CombatRecordsPanelHeight { get; set; } = 520;
    public int SettingsPanelX { get; set; } = -1;
    public int SettingsPanelY { get; set; } = -1;
    public int SettingsPanelWidth { get; set; } = 400;
    public int SettingsPanelHeight { get; set; } = 420;

    // ── 진단 / 크래시 리포트 ──
    private bool _crashReportingEnabled = true;
    public bool CrashReportingEnabled
    {
        get => _crashReportingEnabled;
        set
        {
            if (_crashReportingEnabled == value) return;
            _crashReportingEnabled = value;
            if (value && string.IsNullOrEmpty(CrashReportingActivatedAt))
                CrashReportingActivatedAt = DateTime.UtcNow.ToString("o");
        }
    }
    /// ISO-8601 UTC timestamp recorded the first time the user opts in. Crashes
    /// older than this are never sent — protects against bulk-uploading old logs.
    public string? CrashReportingActivatedAt { get; set; }

    // ── Web upload ──
    public bool   WebUploadEnabled { get; set; } = true;
    public string WebUploadUrl     { get; set; } = "https://api.aion2meter.com";
    public bool   LookupToastEnabled { get; set; } = true;
    public bool?  PartyRequestToastEnabled { get; set; }

    [JsonIgnore]
    public bool EffectivePartyRequestToastEnabled
    {
        get => PartyRequestToastEnabled ?? LookupToastEnabled;
        set
        {
            PartyRequestToastEnabled = value;
            LookupToastEnabled = value;
        }
    }

    // ── per-machine state stored in a sibling file ──
    [JsonIgnore]
    public WindowState WindowState { get; set; } = new();

    // ── debounced save ──
    private static readonly object _saveLock = new();
    private static System.Threading.Timer? _debounceTimer;
    private const int DebounceMs = 400;

    public void Save()
    {
        SaveJson("app_settings.json", this);
        SaveJson("window_state.json", WindowState);
    }

    /// Coalesces rapid Save() calls within ~400ms into one disk write.
    public void SaveDebounced()
    {
        lock (_saveLock)
        {
            _debounceTimer ??= new System.Threading.Timer(_ =>
            {
                try { Save(); } catch { /* swallow — best-effort persistence */ }
            }, null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(DebounceMs, Timeout.Infinite);
        }
    }

    private static AppSettings Load()
    {
        Directory.CreateDirectory(CacheDir);

        var settings = LoadJson<AppSettings>("app_settings.json") ?? new AppSettings();

        var ws = LoadJson<WindowState>("window_state.json");
        if (ws != null)
        {
            // Sanitize obviously bogus coordinates from legacy/multimonitor configs.
            if (ws.X <= -9000 || ws.Y <= -9000) { ws.X = -1; ws.Y = 20; }
            if (ws.X == -1)
                ws.X = (Screen.PrimaryScreen?.WorkingArea.Width ?? 1920) - Math.Max(1, ws.Width) - 20;
            settings.WindowState = ws;
        }
        else
        {
            // Default position: top-right of primary screen. Size needs to fit
            // the focused row (30) + focus stats line (14) + 6 skill bars (~108)
            // + 3 more rows (~100) plus header (28) + boss bar (16) + paddings.
            settings.WindowState = new WindowState
            {
                X = (Screen.PrimaryScreen?.WorkingArea.Width ?? 1920) - 480,
                Y = 80,
                Width = 460,
                Height = 500,
            };
        }

        // GPU mode autocorrect: if the user never explicitly chose "off", revert.
        if (!settings.GpuModeUserOverride
            && string.Equals(settings.GpuMode, "off", StringComparison.OrdinalIgnoreCase))
        {
            settings.GpuMode = "on";
        }

        settings.Shortcuts ??= new ShortcutSettings();
        return settings;
    }

    private static T? LoadJson<T>(string filename) where T : class
    {
        foreach (var path in EnumerateReadCandidates(Path.Combine(CacheDir, filename)))
        {
            try
            {
                if (!File.Exists(path)) continue;
                return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonOpts);
            }
            catch { /* try next candidate */ }
        }
        return null;
    }

    private static void SaveJson<T>(string filename, T data)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var content = JsonSerializer.Serialize(data, JsonOpts);
            WriteTextAtomically(Path.Combine(CacheDir, filename), content);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[settings] save error: " + ex.Message);
        }
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateReadCandidates(string path)
    {
        yield return path;
        yield return path + ".bak";
    }

    /// Atomic write with a .bak fallback so a crash mid-write never loses settings.
    private static void WriteTextAtomically(string path, string content)
    {
        var tmp = path + ".tmp";
        var bak = path + ".bak";
        File.WriteAllText(tmp, content);
        if (File.Exists(path))
            File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
        else
        {
            File.Move(tmp, path, overwrite: true);
            try { File.Copy(path, bak, overwrite: true); } catch { }
        }
    }
}

/// Configurable slot for DPS bar layout.
/// Content: "none" | "percent" | "damage" | "dps"
internal sealed class BarSlotConfig
{
    public string Content { get; set; } = "none";
    public float FontSize { get; set; } = 8f;
    public string Color { get; set; } = "#6E6E80";
}
