using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

/// Settings panel: font, font size, row height, GPU toggle.
internal sealed class SettingsPanelForm : Form
{
    private const int ResizeMargin = 14;
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public event Action? SettingsChanged;

    public SettingsPanelForm()
    {
        Text = "설정";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        MinimumSize = new Size(360, 340);
        Padding = new Padding(3);
        DoubleBuffered = true;

        var settings = AppSettings.Instance;
        Size = new Size(
            Math.Max(MinimumSize.Width, settings.SettingsPanelWidth),
            Math.Max(MinimumSize.Height, settings.SettingsPanelHeight));
        if (settings.SettingsPanelX >= 0 && settings.SettingsPanelY >= 0)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(settings.SettingsPanelX, settings.SettingsPanelY);
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
        }

        var theme = settings.Theme;
        BackColor = theme.BgColor;

        // ── Title bar ──
        var titleBar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = theme.HeaderColor };
        titleBar.Paint += (_, e) =>
        {
            using var pen = new Pen(theme.BorderColor);
            e.Graphics.DrawLine(pen, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1);
        };
        string _fn = settings.FontName;
        float _fs = settings.FontSize;
        var lblTitle = new Label
        {
            Text = "설정",
            ForeColor = theme.TextColor,
            Font = new Font(_fn, _fs + 0.5f, FontStyle.Bold),
            AutoSize = true, Location = new Point(10, 9), BackColor = Color.Transparent,
        };
        titleBar.Controls.Add(lblTitle);
        titleBar.MouseDown += (_, e) => Drag(e);
        lblTitle.MouseDown += (_, e) => Drag(e);

        var btnClose = new CloseButton();
        titleBar.Controls.Add(btnClose);
        titleBar.Resize += (_, _) => btnClose.Location = new Point(titleBar.Width - btnClose.Width - 8, (titleBar.Height - btnClose.Height) / 2);
        btnClose.Click += (_, _) => Close();

        // ── Content ──
        var scrollPanel = new DarkScrollPanel { Dock = DockStyle.Fill, BackColor = theme.BgColor };
        var content = new Panel { BackColor = theme.BgColor, Location = Point.Empty };

        int y = 20;
        const int left = 24;

        // ─── 0. UI Theme colors ─────────────────────────────────
        content.Controls.Add(SectionLabel("테마 색상", left, y));
        y += 22;

        string[] themeLabels = { "배경", "헤더", "보더", "텍스트", "보조 텍스트", "강조", "천족", "마족" };
        Func<string>[] themeGetters = { 
            () => theme.Background, () => theme.Header, () => theme.Border, 
            () => theme.TextPrimary, () => theme.TextSecondary, () => theme.Accent,
            () => theme.Elyos, () => theme.Asmo };
        Action<string>[] themeSetters = {
            v => theme.Background = v, v => theme.Header = v, v => theme.Border = v,
            v => theme.TextPrimary = v, v => theme.TextSecondary = v, v => theme.Accent = v,
            v => theme.Elyos = v, v => theme.Asmo = v };

        var themeSwatches = new ColorSwatch[themeLabels.Length];
        for (int i = 0; i < themeLabels.Length; i++)
        {
            int col = i % 3;
            int row = i / 3;
            int sx = left + col * 110;
            int sy = y + row * 32;

            Color c;
            try { c = ColorTranslator.FromHtml(themeGetters[i]()); } catch { c = Color.Gray; }

            var swatch = new ColorSwatch(c, i) { Location = new Point(sx, sy + 1) };
            int idx = i;
            swatch.ColorPicked += (_, newColor) =>
            {
                themeSetters[idx](ColorTranslator.ToHtml(newColor));
                settings.SaveDebounced();
                SettingsChanged?.Invoke();
            };
            themeSwatches[i] = swatch;
            content.Controls.Add(swatch);

            content.Controls.Add(new Label
            {
                Text = themeLabels[i],
                ForeColor = Color.FromArgb(140, 160, 190),
                Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 1.5f),
                AutoSize = true, Location = new Point(sx + 22, sy + 3), BackColor = Color.Transparent,
            });
        }
        y += 105;

        // Theme export/import/reset buttons
        var btnExport = StyledButton("내보내기", left, y, 90);
        btnExport.Click += (_, _) => ExportTheme(settings);
        content.Controls.Add(btnExport);

        var btnImport = StyledButton("불러오기", left + 100, y, 90);
        btnImport.Click += (_, _) =>
        {
            if (ImportTheme(settings))
            {
                SettingsChanged?.Invoke();
                var t = settings.Theme;
                string[] vals = { t.Background, t.Header, t.Border, t.TextPrimary, t.TextSecondary, t.Accent, t.Elyos, t.Asmo };
                for (int i = 0; i < themeLabels.Length; i++)
                    try { themeSwatches[i].SwatchColor = ColorTranslator.FromHtml(vals[i]); } catch { }
            }
        };
        content.Controls.Add(btnImport);

        var btnReset = StyledButton("초기화", left + 200, y, 76);
        btnReset.Click += (_, _) =>
        {
            var def = new AppSettings.ThemeColors();
            var t = settings.Theme;
            t.Background = def.Background;
            t.Header = def.Header;
            t.Border = def.Border;
            t.TextPrimary = def.TextPrimary;
            t.TextSecondary = def.TextSecondary;
            t.Accent = def.Accent;
            t.Elyos = def.Elyos;
            t.Asmo = def.Asmo;
            settings.SaveDebounced();
            SettingsChanged?.Invoke();
            string[] vals = { t.Background, t.Header, t.Border, t.TextPrimary, t.TextSecondary, t.Accent, t.Elyos, t.Asmo };
            for (int i = 0; i < themeLabels.Length; i++)
                try { themeSwatches[i].SwatchColor = ColorTranslator.FromHtml(vals[i]); } catch { }
        };
        content.Controls.Add(btnReset);
        y += 40;

        // 1. Font family (DirectWrite-based list)
        content.Controls.Add(SectionLabel("폰트", left, y));
        y += 22;
        var fontItems = GetFontList();
        var ddFont = new DarkDropdown(fontItems, FindIndex(fontItems, settings.FontName))
        {
            Location = new Point(left, y), Width = 230,
        };
        ddFont.SelectionChanged += idx =>
        {
            if (idx >= 0 && idx < fontItems.Count)
            {
                settings.FontName = fontItems[idx];
                settings.SaveDebounced();
                SettingsChanged?.Invoke();
            }
        };
        content.Controls.Add(ddFont);
        y += 34;

        // 1.5 Font weight
        content.Controls.Add(SectionLabel("굵기", left, y));
        y += 22;
        var weightItems = new List<string> { "Thin (100)", "Light (300)", "Regular (400)", "Medium (500)", "SemiBold (600)", "Bold (700)", "Black (900)" };
        int[] weightValues = { 100, 300, 400, 500, 600, 700, 900 };
        int curWeightIdx = 2; // default Regular
        for (int i = 0; i < weightValues.Length; i++)
            if (weightValues[i] == settings.FontWeight) { curWeightIdx = i; break; }
        var ddWeight = new DarkDropdown(weightItems, curWeightIdx)
        {
            Location = new Point(left, y), Width = 160,
        };
        ddWeight.SelectionChanged += idx =>
        {
            if (idx >= 0 && idx < weightValues.Length)
            {
                settings.FontWeight = weightValues[idx];
                settings.SaveDebounced();
                SettingsChanged?.Invoke();
            }
        };
        content.Controls.Add(ddWeight);
        y += 40;

        // 2. Font size
        content.Controls.Add(SectionLabel("폰트 크기", left, y));
        y += 22;
        var sizeItems = new List<string> { "7", "7.5", "8", "8.5", "9", "9.5", "10", "10.5", "11", "12", "13", "14" };
        var ddSize = new DarkDropdown(sizeItems, FindIndex(sizeItems, settings.FontSize.ToString("0.#")))
        {
            Location = new Point(left, y), Width = 90,
        };
        ddSize.SelectionChanged += idx =>
        {
            if (idx >= 0 && idx < sizeItems.Count && float.TryParse(sizeItems[idx], out float v))
            {
                settings.FontSize = v;
                settings.SaveDebounced();
                SettingsChanged?.Invoke();
            }
        };
        content.Controls.Add(ddSize);
        y += 42;

        // 3. Row height
        content.Controls.Add(SectionLabel("레이아웃 크기", left, y));
        y += 22;
        var slider = new StyledSlider(50, 150, settings.RowHeight)
        { Location = new Point(left, y), Size = new Size(280, 26) };
        slider.ValueChanged += v =>
        {
            settings.RowHeight = v;
            settings.SaveDebounced();
            SettingsChanged?.Invoke();
        };
        content.Controls.Add(slider);
        y += 40;

        // 3.5 Number format
        content.Controls.Add(SectionLabel("숫자 표기", left, y));
        y += 22;
        var numFmtItems = new List<string> { "축약 (1.5M)", "그대로 (1,500,000)" };
        int numFmtIdx = settings.NumberFormat == "full" ? 1 : 0;
        var ddNumFmt = new DarkDropdown(numFmtItems, numFmtIdx)
        {
            Location = new Point(left, y), Width = 180,
        };
        ddNumFmt.SelectionChanged += idx =>
        {
            settings.NumberFormat = idx == 1 ? "full" : "abbreviated";
            settings.SaveDebounced();
            SettingsChanged?.Invoke();
        };
        content.Controls.Add(ddNumFmt);
        y += 42;

        // 3.6 Contribution % mode
        content.Controls.Add(SectionLabel("기여도 표기", left, y));
        y += 22;
        var pctModeItems = new List<string> { "총 딜량 대비", "보스 최대체력 대비" };
        int pctModeIdx = string.Equals(settings.DpsPercentMode, "boss", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var ddPctMode = new DarkDropdown(pctModeItems, pctModeIdx)
        {
            Location = new Point(left, y), Width = 180,
        };
        ddPctMode.SelectionChanged += idx =>
        {
            settings.DpsPercentMode = idx == 1 ? "boss" : "party";
            settings.SaveDebounced();
            SettingsChanged?.Invoke();
        };
        content.Controls.Add(ddPctMode);
        y += 42;

        // 4. GPU
        var chkGpu = StyledCheckBox("GPU 가속 사용", left, y,
            string.Equals(settings.GpuMode, "on", StringComparison.OrdinalIgnoreCase));
        chkGpu.CheckedChanged += (_, _) =>
        {
            settings.GpuMode = chkGpu.Checked ? "on" : "off";
            settings.GpuModeUserOverride = true;
            settings.SaveDebounced();
            SettingsChanged?.Invoke();
        };
        content.Controls.Add(chkGpu);
        y += 32;

        content.Controls.Add(new Label
        {
            Text = "※ GPU 가속은 재시작 후 적용됩니다.",
            ForeColor = Color.FromArgb(80, 100, 130),
            Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 1f),
            AutoSize = true, Location = new Point(left, y), BackColor = Color.Transparent,
        });
        y += 34;

        // 4.5 Overlay only when Aion 2
        var chkAionOnly = StyledCheckBox("아이온2 활성화 시에만 오버레이 표시", left, y, settings.OverlayOnlyWhenAion);
        chkAionOnly.CheckedChanged += (_, _) =>
        {
            settings.OverlayOnlyWhenAion = chkAionOnly.Checked;
            settings.SaveDebounced();
            // Notify the overlay form via owner.
            if (Owner is OverlayForm overlay)
                overlay.SetOverlayOnlyWhenAion(chkAionOnly.Checked);
        };
        content.Controls.Add(chkAionOnly);
        y += 34;

        // ─── 5. Shortcuts ─────────────────────────────────
        content.Controls.Add(SectionLabel("단축키", left, y));
        y += 22;

        var shortcuts = settings.Shortcuts;
        y = AddShortcutRow(content, "리셋", shortcuts.Reset, left, y, v => { shortcuts.Reset = v; settings.SaveDebounced(); });
        y = AddShortcutRow(content, "프로그램 재시작", shortcuts.Restart, left, y, v => { shortcuts.Restart = v; settings.SaveDebounced(); });
        y = AddShortcutRow(content, "익명 모드", shortcuts.Anonymous, left, y, v => { shortcuts.Anonymous = v; settings.SaveDebounced(); });
        y = AddShortcutRow(content, "컴팩트 모드", shortcuts.Compact, left, y, v => { shortcuts.Compact = v; settings.SaveDebounced(); });

        y += 10;
        content.Size = new Size(ClientSize.Width, y);
        scrollPanel.Controls.Add(content);
        scrollPanel.SetContentHeight(y);

        Controls.Add(scrollPanel);
        Controls.Add(titleBar);

        Resize += (_, _) =>
        {
            content.Width = scrollPanel.ClientSize.Width;
            scrollPanel.SetContentHeight(content.Height);
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32Native.WS_EX_TOOLWINDOW | Win32Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    private static int FindIndex(List<string> items, string value)
    {
        for (int i = 0; i < items.Count; i++)
            if (items[i].Equals(value, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    private static List<string> GetFontList()
    {
        // Use DirectWrite font collection for accurate family names.
        try
        {
            using var factory = Vortice.DirectWrite.DWrite.DWriteCreateFactory<Vortice.DirectWrite.IDWriteFactory>();
            using var collection = factory.GetSystemFontCollection(false);
            var list = new List<string>();
            string[] preferred = { "Malgun Gothic", "Segoe UI", "Noto Sans KR", "Gmarket Sans", "D2Coding" };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in preferred) { list.Add(n); seen.Add(n); }

            int count = (int)collection.FontFamilyCount;
            for (int i = 0; i < count; i++)
            {
                using var fam = collection.GetFontFamily((uint)i);
                using var names = fam.FamilyNames;
                names.FindLocaleName("en-us", out uint idx);
                if (idx == uint.MaxValue) idx = 0;
                string name = names.GetString(idx);
                if (seen.Contains(name)) continue;
                seen.Add(name);
                list.Add(name);
            }
            return list;
        }
        catch
        {
            // Fallback to GDI if DirectWrite fails.
            var list = new List<string> { "Malgun Gothic", "Segoe UI" };
            using var ifc = new InstalledFontCollection();
            foreach (var f in ifc.Families) list.Add(f.Name);
            return list;
        }
    }

    private static Label SectionLabel(string text, int x, int y) => new()
    {
        Text = text,
        ForeColor = AppSettings.Instance.Theme.TextDimColor,
        Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f, FontStyle.Bold),
        AutoSize = true, Location = new Point(x, y), BackColor = Color.Transparent,
    };

    private static DarkToggle StyledCheckBox(string text, int x, int y, bool isChecked)
    {
        var toggle = new DarkToggle(text, isChecked) { Location = new Point(x, y) };
        return toggle;
    }

    private static Button StyledButton(string text, int x, int y, int width)
    {
        var t = AppSettings.Instance.Theme;
        var btn = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Width = width, Height = 26,
            FlatStyle = FlatStyle.Flat,
            BackColor = t.HeaderColor,
            ForeColor = t.TextColor,
            Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f),
            Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderColor = t.BorderColor;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(
            Math.Min(255, t.HeaderColor.R + 18),
            Math.Min(255, t.HeaderColor.G + 18),
            Math.Min(255, t.HeaderColor.B + 18));
        return btn;
    }

    private static int AddShortcutRow(Panel content, string label, string currentValue, int left, int y, Action<string> onChanged)
    {
        var t = AppSettings.Instance.Theme;
        content.Controls.Add(new Label
        {
            Text = label,
            ForeColor = t.TextColor,
            Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f),
            AutoSize = true, Location = new Point(left, y + 4), BackColor = Color.Transparent,
        });

        var txt = new TextBox
        {
            Text = currentValue,
            Location = new Point(left + 120, y),
            Width = 140, Height = 24,
            BackColor = t.HeaderColor,
            ForeColor = t.TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize),
        };
        txt.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            var parts = new List<string>();
            if (e.Alt) parts.Add("Alt");
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            var key = e.KeyCode;
            if (key != Keys.Menu && key != Keys.ControlKey && key != Keys.ShiftKey)
            {
                string keyName = key switch
                {
                    Keys.Oemtilde => "`",
                    Keys.OemMinus => "-",
                    Keys.OemQuestion => "/",
                    _ => key.ToString(),
                };
                parts.Add(keyName);
                txt.Text = string.Join("+", parts);
                onChanged(txt.Text);
            }
        };
        content.Controls.Add(txt);
        return y + 32;
    }

    private static void ExportTheme(AppSettings settings)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "테마 내보내기",
            Filter = "JSON 파일|*.json",
            FileName = "a2meter_theme.json",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var t = settings.Theme;
        var data = new
        {
            background = t.Background,
            header = t.Header,
            border = t.Border,
            textPrimary = t.TextPrimary,
            textSecondary = t.TextSecondary,
            accent = t.Accent,
            elyos = t.Elyos,
            asmo = t.Asmo,
        };
        var json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(dlg.FileName, json);
    }

    private static bool ImportTheme(AppSettings settings)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "테마 불러오기",
            Filter = "JSON 파일|*.json",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return false;
        try
        {
            var json = System.IO.File.ReadAllText(dlg.FileName);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var t = settings.Theme;
            if (root.TryGetProperty("background", out var v))    t.Background    = v.GetString() ?? t.Background;
            if (root.TryGetProperty("header", out v))            t.Header        = v.GetString() ?? t.Header;
            if (root.TryGetProperty("border", out v))            t.Border        = v.GetString() ?? t.Border;
            if (root.TryGetProperty("textPrimary", out v))       t.TextPrimary   = v.GetString() ?? t.TextPrimary;
            if (root.TryGetProperty("textSecondary", out v))     t.TextSecondary = v.GetString() ?? t.TextSecondary;
            if (root.TryGetProperty("accent", out v))            t.Accent        = v.GetString() ?? t.Accent;
            if (root.TryGetProperty("elyos", out v))            t.Elyos        = v.GetString() ?? t.Elyos;
            if (root.TryGetProperty("asmo", out v))            t.Asmo        = v.GetString() ?? t.Asmo;
            settings.SaveDebounced();
            return true;
        }
        catch { }
        return false;
    }

    private void PersistBounds()
    {
        if (WindowState != FormWindowState.Normal) return;
        var s = AppSettings.Instance;
        s.SettingsPanelX = Location.X;
        s.SettingsPanelY = Location.Y;
        s.SettingsPanelWidth = Size.Width;
        s.SettingsPanelHeight = Size.Height;
        s.SaveDebounced();
    }

    protected override void OnMove(EventArgs e) { base.OnMove(e); PersistBounds(); }
    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); PersistBounds(); }

    private void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(AppSettings.Instance.Theme.BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32Native.WM_NCHITTEST)
        {
            int lp = unchecked((int)(long)m.LParam);
            var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
            int hit = HitTestEdges(pt);
            if (hit != Win32Native.HTCLIENT) { m.Result = (IntPtr)hit; return; }
            if (pt.Y >= 0 && pt.Y < 36 && pt.X < Width - 40) { m.Result = (IntPtr)Win32Native.HTCAPTION; return; }
        }
        base.WndProc(ref m);
    }

    private int HitTestEdges(Point pt)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        bool L = pt.X < ResizeMargin, R = pt.X >= w - ResizeMargin;
        bool T = pt.Y < ResizeMargin, B = pt.Y >= h - ResizeMargin;
        if (T && L) return Win32Native.HTTOPLEFT;  if (T && R) return Win32Native.HTTOPRIGHT;
        if (B && L) return Win32Native.HTBOTTOMLEFT; if (B && R) return Win32Native.HTBOTTOMRIGHT;
        if (L) return Win32Native.HTLEFT;  if (R) return Win32Native.HTRIGHT;
        if (T) return Win32Native.HTTOP;   if (B) return Win32Native.HTBOTTOM;
        return Win32Native.HTCLIENT;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Custom dark dropdown (owner-drawn, no native ComboBox)
    // ═══════════════════════════════════════════════════════════════════

    private sealed class DarkDropdown : Control
    {
        public event Action<int>? SelectionChanged;

        private readonly List<string> _items;
        private int _selectedIndex;
        private bool _hover;
        private bool _open;
        private DropdownPopup? _popup;

        private static Color BgNormal   => AppSettings.Instance.Theme.HeaderColor;
        private static Color BgHover    => Color.FromArgb(
            Math.Min(255, AppSettings.Instance.Theme.HeaderColor.R + 14),
            Math.Min(255, AppSettings.Instance.Theme.HeaderColor.G + 14),
            Math.Min(255, AppSettings.Instance.Theme.HeaderColor.B + 14));
        private static Color Border     => AppSettings.Instance.Theme.BorderColor;
        private static Color FgNormal   => AppSettings.Instance.Theme.TextColor;
        private static Color Arrow      => AppSettings.Instance.Theme.TextDimColor;

        public DarkDropdown(List<string> items, int selectedIndex)
        {
            _items = items;
            _selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Count - 1));
            Height = 28;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex => _selectedIndex;
        public string? SelectedItem => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (_open) { ClosePopup(); return; }
            ShowPopup();
        }

        private void ShowPopup()
        {
            _open = true;
            _popup = new DropdownPopup(_items, _selectedIndex, Width);
            _popup.ItemSelected += idx =>
            {
                _selectedIndex = idx;
                Invalidate();
                SelectionChanged?.Invoke(idx);
            };
            _popup.Closed += (_, _) => { _open = false; Invalidate(); };

            var screenPt = PointToScreen(new Point(0, Height));
            _popup.Location = screenPt;
            _popup.Show();
            Invalidate();
        }

        private void ClosePopup()
        {
            _popup?.Close();
            _popup = null;
            _open = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            var bg = _open ? BgHover : _hover ? BgHover : BgNormal;
            using (var brush = new SolidBrush(bg))
            using (var path = RoundRect(0, 0, Width, Height, 6))
                g.FillPath(brush, path);

            using (var pen = new Pen(_open ? AppSettings.Instance.Theme.AccentColor : Border))
            using (var path = RoundRect(0, 0, Width, Height, 6))
                g.DrawPath(pen, path);

            // Text
            string text = SelectedItem ?? "";
            using var font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize);
            var textRect = new Rectangle(10, 0, Width - 30, Height);
            TextRenderer.DrawText(g, text, font, textRect, FgNormal,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // Arrow
            int ax = Width - 18, ay = Height / 2;
            using var arrowPen = new Pen(Arrow, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(arrowPen, ax - 3, ay - 2, ax, ay + 1);
            g.DrawLine(arrowPen, ax, ay + 1, ax + 3, ay - 2);
        }

        private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2 - 1, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2 - 1, y + h - r * 2 - 1, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2 - 1, r * 2, r * 2, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ─── Popup list for dropdown ─────────────────────────────────────

    private sealed class DropdownPopup : Form
    {
        public event Action<int>? ItemSelected;

        private readonly List<string> _items;
        private int _hoverIndex = -1;
        private int _selectedIndex;
        private const int ItemHeight = 26;
        private const int MaxVisible = 10;
        private int _scrollOffset;

        public DropdownPopup(List<string> items, int selectedIndex, int width)
        {
            _items = items;
            _selectedIndex = selectedIndex;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = AppSettings.Instance.Theme.BgColor;
            DoubleBuffered = true;

            int visibleCount = Math.Min(items.Count, MaxVisible);
            Size = new Size(Math.Max(width, 120), visibleCount * ItemHeight + 4);

            // Scroll to show selected item.
            if (selectedIndex > MaxVisible - 3)
                _scrollOffset = Math.Min(selectedIndex - 3, Math.Max(0, items.Count - MaxVisible));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000008 | 0x00000080; // TOPMOST | TOOLWINDOW
                cp.ClassStyle |= 0x0008; // CS_DBLCLKS
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = (e.Y - 2) / ItemHeight + _scrollOffset;
            if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int idx = (e.Y - 2) / ItemHeight + _scrollOffset;
            if (idx >= 0 && idx < _items.Count)
            {
                _selectedIndex = idx;
                ItemSelected?.Invoke(idx);
            }
            Close();
            base.OnMouseClick(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int maxOffset = Math.Max(0, _items.Count - MaxVisible);
            _scrollOffset = Math.Clamp(_scrollOffset - (e.Delta > 0 ? 2 : -2), 0, maxOffset);
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverIndex = -1; Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Border
            var _th = AppSettings.Instance.Theme;
            using (var pen = new Pen(_th.BorderColor))
            using (var path = RoundRect(0, 0, Width, Height, 6))
            {
                using var bgBrush = new SolidBrush(_th.BgColor);
                g.FillPath(bgBrush, path);
                g.DrawPath(pen, path);
            }

            using var font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize);
            int visibleCount = Math.Min(_items.Count - _scrollOffset, MaxVisible);

            for (int i = 0; i < visibleCount; i++)
            {
                int dataIdx = i + _scrollOffset;
                int iy = 2 + i * ItemHeight;
                var itemRect = new Rectangle(3, iy, Width - 6, ItemHeight);

                bool isHover = dataIdx == _hoverIndex;
                bool isSelected = dataIdx == _selectedIndex;

                if (isHover || isSelected)
                {
                    var hlColor = isHover ? _th.HeaderColor : Color.FromArgb(
                        Math.Min(255, _th.HeaderColor.R + 10),
                        Math.Min(255, _th.HeaderColor.G + 10),
                        Math.Min(255, _th.HeaderColor.B + 10));
                    using var hlBrush = new SolidBrush(hlColor);
                    using var hlPath = RoundRect(itemRect.X, itemRect.Y, itemRect.Width, itemRect.Height, 4);
                    g.FillPath(hlBrush, hlPath);
                }

                var textColor = isSelected ? _th.AccentColor : _th.TextColor;
                var textRect = new Rectangle(itemRect.X + 10, itemRect.Y, itemRect.Width - 14, itemRect.Height);
                TextRenderer.DrawText(g, _items[dataIdx], font, textRect, textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            // Scroll indicator
            if (_items.Count > MaxVisible)
            {
                int totalH = Height - 8;
                float thumbRatio = (float)MaxVisible / _items.Count;
                int thumbH = Math.Max(12, (int)(totalH * thumbRatio));
                float scrollRatio = (float)_scrollOffset / Math.Max(1, _items.Count - MaxVisible);
                int thumbY = 4 + (int)(scrollRatio * (totalH - thumbH));
                using var scrollBrush = new SolidBrush(_th.TextDimColor);
                g.FillRectangle(scrollBrush, Width - 6, thumbY, 3, thumbH);
            }
        }

        private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2 - 1, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2 - 1, y + h - r * 2 - 1, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2 - 1, r * 2, r * 2, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Close button
    // ═══════════════════════════════════════════════════════════════════

    private sealed class CloseButton : Control
    {
        private bool _hover, _pressed;
        public CloseButton()
        {
            Size = new Size(26, 26); DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_hover)
            {
                using var bg = new SolidBrush(Color.FromArgb(_pressed ? 110 : 70, 220, 70, 70));
                using var path = RoundRect(0, 0, Width, Height, 4);
                g.FillPath(bg, path);
            }
            var fg = _hover ? Color.FromArgb(235, 240, 250) : AppSettings.Instance.Theme.TextColor;
            using var pen = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int cx = Width / 2, cy = Height / 2;
            g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
            g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
        }
        private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
        {
            var p = new GraphicsPath();
            p.AddArc(x, y, r * 2, r * 2, 180, 90);
            p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
            p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
            p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
            p.CloseFigure(); return p;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Styled slider
    // ═══════════════════════════════════════════════════════════════════

    private sealed class StyledSlider : Control
    {
        public event Action<int>? ValueChanged;
        private int _min, _max, _value;
        private bool _dragging, _hover;
        private const int ThumbR = 7, TrackH = 4;

        public StyledSlider(int min, int max, int value)
        {
            _min = min; _max = max; _value = Math.Clamp(value, min, max);
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
        }

        private int TL => ThumbR;
        private int TR => Width - ThumbR - 44;
        private float Ratio => (_value - _min) / (float)Math.Max(1, _max - _min);
        private int ThumbX => TL + (int)(Ratio * (TR - TL));

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _dragging = true; Capture = true; Upd(e.X); } base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (_dragging) Upd(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; Capture = false; base.OnMouseUp(e); }

        private void Upd(int x)
        {
            float r = (x - TL) / (float)Math.Max(1, TR - TL);
            int v = _min + (int)Math.Round(r * (_max - _min));
            v = Math.Clamp(v, _min, _max);
            if (v != _value) { _value = v; Invalidate(); ValueChanged?.Invoke(v); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int cy = Height / 2, tx = ThumbX;

            // Track bg
            using (var tb = new SolidBrush(Color.FromArgb(30, 40, 60)))
            {
                var rect = new RectangleF(TL, cy - TrackH / 2f, TR - TL, TrackH);
                using var path = RoundRectF(rect, TrackH / 2f);
                g.FillPath(tb, path);
            }
            // Fill
            if (tx > TL)
            {
                var accent = AppSettings.Instance.Theme.AccentColor;
                var c = _hover || _dragging ? ControlPaint.Light(accent, 0.3f) : accent;
                using var fb = new SolidBrush(c);
                var rect = new RectangleF(TL, cy - TrackH / 2f, tx - TL, TrackH);
                using var path = RoundRectF(rect, TrackH / 2f);
                g.FillPath(fb, path);
            }
            // Thumb
            var tc = _dragging ? Color.FromArgb(220, 235, 255) : _hover ? Color.FromArgb(200, 220, 250) : AppSettings.Instance.Theme.TextColor;
            using (var tb2 = new SolidBrush(tc))
                g.FillEllipse(tb2, tx - ThumbR, cy - ThumbR, ThumbR * 2, ThumbR * 2);
            using (var rp = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                g.DrawEllipse(rp, tx - ThumbR, cy - ThumbR, ThumbR * 2, ThumbR * 2);
            // Label
            using var font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 1f);
            using var lb = new SolidBrush(Color.FromArgb(140, 165, 200));
            g.DrawString($"{_value}%", font, lb, TR + 10, cy - 7);
        }

        private static GraphicsPath RoundRectF(RectangleF rect, float r)
        {
            var p = new GraphicsPath();
            float d = r * 2;
            if (rect.Width < d) { p.AddEllipse(rect); return p; }
            p.AddArc(rect.X, rect.Y, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Color swatch (clickable circle that opens ColorDialog)
    // ═══════════════════════════════════════════════════════════════════

    private sealed class ColorSwatch : Control
    {
        public event Action<int, Color>? ColorPicked;
        private Color _color;
        private readonly int _index;
        private bool _hover;

        public ColorSwatch(Color color, int index)
        {
            _color = color; _index = index;
            Size = new Size(18, 18);
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public Color SwatchColor
        {
            get => _color;
            set { _color = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            using var dlg = new ColorDialog
            {
                Color = _color,
                FullOpen = true,
                AnyColor = true,
            };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _color = dlg.Color;
                Invalidate();
                ColorPicked?.Invoke(_index, _color);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int size = Math.Min(Width, Height) - 2;
            int x = (Width - size) / 2, y = (Height - size) / 2;

            using (var brush = new SolidBrush(_color))
                g.FillEllipse(brush, x, y, size, size);

            var borderColor = _hover ? Color.FromArgb(200, 220, 250) : Color.FromArgb(60, 80, 110);
            using (var pen = new Pen(borderColor, _hover ? 2f : 1.2f))
                g.DrawEllipse(pen, x, y, size, size);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Dark toggle switch (replaces ugly checkbox)
    // ═══════════════════════════════════════════════════════════════════

    private sealed class DarkToggle : Control
    {
        public event EventHandler? CheckedChanged;

        private bool _checked;
        private bool _hover;
        private const int TrackW = 36, TrackH = 18, ThumbR = 7;

        public DarkToggle(string text, bool isChecked)
        {
            _checked = isChecked;
            Text = text;
            Height = 22;
            Width = TrackW + 8 + TextRenderer.MeasureText(text, new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize)).Width;
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public bool Checked
        {
            get => _checked;
            set { if (_checked != value) { _checked = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Checked = !_checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var t = AppSettings.Instance.Theme;

            int cy = Height / 2;

            // Track
            var trackRect = new RectangleF(0, cy - TrackH / 2f, TrackW, TrackH);
            var trackColor = _checked ? t.AccentColor : (_hover ? Color.FromArgb(45, 55, 80) : Color.FromArgb(30, 40, 60));
            using (var brush = new SolidBrush(trackColor))
            using (var path = RoundRectF(trackRect, TrackH / 2f))
                g.FillPath(brush, path);

            // Thumb
            float thumbX = _checked ? TrackW - ThumbR - 3 : ThumbR + 3;
            using (var brush = new SolidBrush(Color.FromArgb(240, 245, 255)))
                g.FillEllipse(brush, thumbX - ThumbR, cy - ThumbR, ThumbR * 2, ThumbR * 2);

            // Label
            using var font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize);
            var textRect = new Rectangle(TrackW + 8, 0, Width - TrackW - 8, Height);
            TextRenderer.DrawText(g, Text, font, textRect, t.TextColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        private static GraphicsPath RoundRectF(RectangleF rect, float r)
        {
            var p = new GraphicsPath();
            float d = r * 2;
            p.AddArc(rect.X, rect.Y, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Dark scroll panel (custom-painted thin scrollbar)
    // ═══════════════════════════════════════════════════════════════════

    private sealed class DarkScrollPanel : Panel
    {
        private int _scrollOffset;
        private int _contentHeight;
        private bool _thumbDrag;
        private int _thumbDragStartY, _thumbDragStartOffset;
        private const int ScrollBarW = 6;
        private const int ThumbMinH = 20;

        public DarkScrollPanel()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.UserPaint, true);
        }

        public void SetContentHeight(int h) { _contentHeight = h; ClampScroll(); Invalidate(); }

        private int ViewH => ClientSize.Height;
        private bool NeedsScroll => _contentHeight > ViewH;
        private int MaxScroll => Math.Max(0, _contentHeight - ViewH);

        private void ClampScroll()
        {
            _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScroll);
            // Reposition child controls
            if (Controls.Count > 0 && Controls[0] is Control inner)
                inner.Top = -_scrollOffset;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!NeedsScroll) { base.OnMouseWheel(e); return; }
            _scrollOffset -= e.Delta / 4;
            ClampScroll();
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && NeedsScroll && e.X >= Width - ScrollBarW - 4)
            {
                var (thumbY, thumbH) = GetThumbRect();
                if (e.Y >= thumbY && e.Y <= thumbY + thumbH)
                {
                    _thumbDrag = true;
                    _thumbDragStartY = e.Y;
                    _thumbDragStartOffset = _scrollOffset;
                    Capture = true;
                }
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_thumbDrag)
            {
                int dy = e.Y - _thumbDragStartY;
                var (_, thumbH) = GetThumbRect();
                int trackH = ViewH - 8;
                float ratio = (float)dy / Math.Max(1, trackH - thumbH);
                _scrollOffset = _thumbDragStartOffset + (int)(ratio * MaxScroll);
                ClampScroll();
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _thumbDrag = false;
            Capture = false;
            base.OnMouseUp(e);
        }

        private (int y, int h) GetThumbRect()
        {
            int trackH = ViewH - 8;
            float visRatio = (float)ViewH / Math.Max(1, _contentHeight);
            int thumbH = Math.Max(ThumbMinH, (int)(trackH * visRatio));
            float scrollRatio = (float)_scrollOffset / Math.Max(1, MaxScroll);
            int thumbY = 4 + (int)(scrollRatio * (trackH - thumbH));
            return (thumbY, thumbH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!NeedsScroll) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var (thumbY, thumbH) = GetThumbRect();
            int x = Width - ScrollBarW - 2;

            using var brush = new SolidBrush(AppSettings.Instance.Theme.TextDimColor);
            var rect = new RectangleF(x, thumbY, ScrollBarW, thumbH);
            using var path = RoundRectF(rect, ScrollBarW / 2f);
            g.FillPath(brush, path);
        }

        private static GraphicsPath RoundRectF(RectangleF rect, float r)
        {
            var p = new GraphicsPath();
            float d = r * 2;
            if (rect.Width < d || rect.Height < d) { p.AddEllipse(rect); return p; }
            p.AddArc(rect.X, rect.Y, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
