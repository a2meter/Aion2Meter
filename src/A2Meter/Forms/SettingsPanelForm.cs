using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

/// Settings panel with tabbed layout: 시스템 / 테마 / DPS / 단축키.
internal sealed class SettingsPanelForm : Form
{
    private const int ResizeMargin = 14;
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public event Action? SettingsChanged;

    // Layout constants
    private const int PX = 16;          // horizontal padding
    private int _lineH;                 // measured text height — drives all row spacing
    private static float FontScale => Math.Max(1f, AppSettings.Instance.FontSize / 9f);

    public SettingsPanelForm()
    {
        Text = "설정";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Padding = new Padding(3);
        DoubleBuffered = true;

        var settings = AppSettings.Instance;

        // ── Scale form size with font ──
        float _wScale = Math.Max(1f, settings.FontSize / 9f);
        int minW = (int)(380 * _wScale);
        MinimumSize = new Size(minW, 380);
        Size = new Size(
            Math.Max(minW, (int)(settings.SettingsPanelWidth * _wScale)),
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
        string _fn = settings.FontName;
        float _fs = settings.FontSize;

        // ── Row spacing derived from measured text height ──
        using (var mf = new Font(_fn, _fs))
            _lineH = TextRenderer.MeasureText("Ag가", mf).Height;
        int sH   = (int)(_lineH * 1.50);  // section header row     (24 @ 9pt)
        int rH   = (int)(_lineH * 1.85);  // standard field row     (30 @ 9pt)
        int srH  = (int)(_lineH * 2.25);  // slider / dropdown row  (36 @ 9pt)
        int gH   = (int)(_lineH * 2.00);  // color grid cell        (32 @ 9pt)
        int brH  = (int)(_lineH * 2.50);  // button bar row         (40 @ 9pt)
        int hdrH = (int)(_lineH * 2.25);  // title bar              (36 @ 9pt)
        int tabH = (int)(_lineH * 2.00);  // tab bar                (32 @ 9pt)

        int cw = ClientSize.Width;

        // ══════════════════════════════════════════════════════════
        // Title bar
        // ══════════════════════════════════════════════════════════
        var titleBar = new Panel { Dock = DockStyle.Top, Height = hdrH, BackColor = theme.HeaderColor };
        titleBar.Paint += (_, e) =>
        {
            using var pen = new Pen(theme.BorderColor);
            e.Graphics.DrawLine(pen, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1);
        };
        var lblTitle = new Label
        {
            Text = "설정", ForeColor = theme.TextColor,
            Font = new Font(_fn, _fs + 0.5f, FontStyle.Bold),
            AutoSize = true, Location = new Point(10, (hdrH - _lineH) / 2), BackColor = Color.Transparent,
        };
        titleBar.Controls.Add(lblTitle);
        titleBar.MouseDown += (_, e) => Drag(e);
        lblTitle.MouseDown += (_, e) => Drag(e);
        var btnClose = new CloseButton();
        titleBar.Controls.Add(btnClose);
        titleBar.Resize += (_, _) => btnClose.Location = new Point(titleBar.Width - btnClose.Width - 8, (titleBar.Height - btnClose.Height) / 2);
        btnClose.Click += (_, _) => Close();

        // ══════════════════════════════════════════════════════════
        // Tab bar
        // ══════════════════════════════════════════════════════════
        string[] tabNames = { "시스템", "테마", "DPS", "단축키" };
        int activeTab = 0, hoverTab = -1;
        var tabBar = new Panel { Dock = DockStyle.Top, Height = tabH, BackColor = theme.HeaderColor, Cursor = Cursors.Hand };
        tabBar.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var font = new Font(_fn, _fs, FontStyle.Regular);
            int tabW = tabBar.Width / tabNames.Length;
            for (int i = 0; i < tabNames.Length; i++)
            {
                var rect = new Rectangle(i * tabW, 0, tabW, tabBar.Height);
                var textColor = i == activeTab ? theme.AccentColor
                    : i == hoverTab ? theme.TextColor
                    : theme.TextDimColor;
                TextRenderer.DrawText(g, tabNames[i], font, rect, textColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (i == activeTab)
                {
                    using var accentPen = new Pen(theme.AccentColor, 2f);
                    int cx = rect.Left + rect.Width / 2;
                    int hw = Math.Min(rect.Width / 3, 24);
                    g.DrawLine(accentPen, cx - hw, rect.Bottom - 2, cx + hw, rect.Bottom - 2);
                }
            }
            using var borderPen = new Pen(theme.BorderColor);
            g.DrawLine(borderPen, 0, tabBar.Height - 1, tabBar.Width, tabBar.Height - 1);
        };
        tabBar.Resize += (_, _) => tabBar.Invalidate();

        var tabPanels = new DarkScrollPanel[4];
        for (int t = 0; t < 4; t++)
            tabPanels[t] = new DarkScrollPanel { Dock = DockStyle.Fill, BackColor = theme.BgColor, Visible = t == 0 };

        tabBar.MouseMove += (_, e) =>
        {
            int tabW = tabBar.Width / tabNames.Length;
            int nh = Math.Clamp(e.X / Math.Max(1, tabW), 0, tabNames.Length - 1);
            if (nh != hoverTab) { hoverTab = nh; tabBar.Invalidate(); }
        };
        tabBar.MouseLeave += (_, _) => { hoverTab = -1; tabBar.Invalidate(); };
        tabBar.MouseClick += (_, e) =>
        {
            int tabW = tabBar.Width / tabNames.Length;
            int nt = Math.Clamp(e.X / Math.Max(1, tabW), 0, tabNames.Length - 1);
            if (nt != activeTab)
            {
                tabPanels[activeTab].Visible = false;
                activeTab = nt;
                tabPanels[activeTab].Visible = true;
                tabBar.Invalidate();
            }
        };

        // ══════════════════════════════════════════════════════════
        // Tab 0 — 시스템
        // ══════════════════════════════════════════════════════════
        {
            var tab = new VLayout { BackColor = theme.BgColor };

            tab.Add(SectionLabel("설정 저장"), sH);

            {
                var row = new HLayout { Spread = false, FillWidth = true };
                var btnExport = StyledButton("내보내기");
                btnExport.Click += (_, _) => ExportSettings(settings);
                row.Add(btnExport);
                var btnImport = StyledButton("불러오기");
                btnImport.Click += (_, _) =>
                {
                    if (ImportSettings(settings)) { SettingsChanged?.Invoke(); Close(); }
                };
                row.Add(btnImport);
                var btnReset = StyledButton("초기화");
                btnReset.Click += (_, _) =>
                {
                    if (MessageBox.Show("모든 설정을 초기화하시겠습니까?", "설정 초기화",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    { ResetAllSettings(settings); SettingsChanged?.Invoke(); Close(); }
                };
                row.Add(btnReset);
                tab.Add(row, brH);
            }

            tab.Add(SectionLabel("오버레이"), sH);

            {
                var row = new HLayout();
                row.Add(FieldLabel("아이온2 활성화 시에만 표시"));
                var chk = StyledCheckBox(settings.OverlayOnlyWhenAion);
                chk.CheckedChanged += (_, _) =>
                {
                    settings.OverlayOnlyWhenAion = chk.Checked;
                    settings.SaveDebounced();
                    if (Owner is OverlayForm overlay) overlay.SetOverlayOnlyWhenAion(chk.Checked);
                };
                row.Add(chk);
                tab.Add(row, rH);
            }

            {
                var row = new HLayout();
                row.Add(FieldLabel("GPU 가속 사용"));
                var chk = StyledCheckBox(string.Equals(settings.GpuMode, "on", StringComparison.OrdinalIgnoreCase));
                chk.CheckedChanged += (_, _) =>
                {
                    settings.GpuMode = chk.Checked ? "on" : "off";
                    settings.GpuModeUserOverride = true;
                    settings.SaveDebounced();
                    SettingsChanged?.Invoke();
                };
                row.Add(chk);
                tab.Add(row, rH - 4);
            }

            tab.Add(new Label
            {
                Text = "※ GPU 가속은 재시작 후 적용됩니다.",
                ForeColor = Color.FromArgb(80, 100, 130),
                Font = new Font(_fn, _fs - 1f),
                AutoSize = true, BackColor = Color.Transparent,
            }, rH, 16);

            tab.Add(SectionLabel("화면 스냅"), sH);

            {
                var row = new HLayout();
                row.Add(FieldLabel("화면 가장자리 스냅"));
                var chk = StyledCheckBox(settings.SnapEnabled);
                chk.CheckedChanged += (_, _) =>
                {
                    settings.SnapEnabled = chk.Checked;
                    settings.SaveDebounced();
                };
                row.Add(chk);
                tab.Add(row, rH);
            }

            {
                var row = new HLayout();
                row.Add(FieldLabel("범위 (px)"));
                var slider = new StyledSlider(1, 8, settings.SnapDistance) { Suffix = "px", Height = 26 };
                slider.ValueChanged += v => { settings.SnapDistance = v; settings.SaveDebounced(); };
                row.Add(slider, 0.5f);
                tab.Add(row, srH);
            }

            tab.Add(SectionLabel("통계 웹"), sH);

            {
                var row = new HLayout();
                row.Add(FieldLabel("파티 신청 조회 토스트"));
                var chk = StyledCheckBox(settings.LookupToastEnabled);
                chk.CheckedChanged += (_, _) =>
                {
                    settings.LookupToastEnabled = chk.Checked;
                    settings.SaveDebounced();
                };
                row.Add(chk);
                tab.Add(row, rH);
            }

            {
                var row = new HLayout { Spread = false, FillWidth = true };
                var btnMyRecords = StyledButton("내 기록 보기");
                btnMyRecords.Click += (_, _) => OpenMyRecordsInBrowser();
                row.Add(btnMyRecords);
                tab.Add(row, brH);
            }

            tab.Add(new Label
            {
                Text = "※ 기본 브라우저로 aion2meter.com에서 내 업로드 기록을 봅니다.",
                ForeColor = Color.FromArgb(80, 100, 130),
                Font = new Font(_fn, _fs - 1f),
                AutoSize = true, BackColor = Color.Transparent,
            }, rH, 16);

            tab.Add(SectionLabel("진단"), sH);

            {
                var row = new HLayout();
                row.Add(FieldLabel("익명 크래시 리포트 보내기"));
                var chk = StyledCheckBox(settings.CrashReportingEnabled);
                chk.CheckedChanged += (_, _) =>
                {
                    settings.CrashReportingEnabled = chk.Checked;
                    settings.SaveDebounced();
                };
                row.Add(chk);
                tab.Add(row, rH);
            }

            tab.Add(new Label
            {
                Text = "※ 크래시가 발생하면 Github Issue로 자동 등록됩니다.\n   경로·캐릭터명·IP는 전송 전 마스킹됩니다.",
                ForeColor = Color.FromArgb(80, 100, 130),
                Font = new Font(_fn, _fs - 1f),
                AutoSize = true, BackColor = Color.Transparent,
            }, rH + 8, 16);

            tabPanels[0].Controls.Add(tab);
            tab.Width = cw;
            tab.DoLayout();
            tabPanels[0].SetContentHeight(tab.Height);
        }

        // ══════════════════════════════════════════════════════════
        // Tab 1 — 테마
        // ══════════════════════════════════════════════════════════
        {
            var tab = new VLayout { BackColor = theme.BgColor };

            tab.Add(SectionLabel("테마 색상"), sH);

            {
                string[] themeLabels = { "배경", "헤더", "보더", "텍스트", "보조 텍스트", "강조", "천족", "마족" };
                Func<string>[] themeGetters = {
                    () => theme.Background, () => theme.Header, () => theme.Border,
                    () => theme.TextPrimary, () => theme.TextSecondary, () => theme.Accent,
                    () => theme.Elyos, () => theme.Asmodian };
                Action<string>[] themeSetters = {
                    v => theme.Background = v, v => theme.Header = v, v => theme.Border = v,
                    v => theme.TextPrimary = v, v => theme.TextSecondary = v, v => theme.Accent = v,
                    v => theme.Elyos = v, v => theme.Asmodian = v };

                int numRows = (int)Math.Ceiling(themeLabels.Length / 3.0);
                int gridH = numRows * gH;
                var gridPanel = new Panel { BackColor = Color.Transparent };
                var cells = new (ColorSwatch sw, Label lb, int c, int r)[themeLabels.Length];

                for (int i = 0; i < themeLabels.Length; i++)
                {
                    Color c;
                    try { c = ColorTranslator.FromHtml(themeGetters[i]()); } catch { c = Color.Gray; }
                    var swatch = new ColorSwatch(c, i);
                    int idx = i;
                    swatch.ColorPicked += (_, newColor) =>
                    {
                        themeSetters[idx](ColorTranslator.ToHtml(newColor));
                        settings.SaveDebounced();
                        SettingsChanged?.Invoke();
                    };
                    gridPanel.Controls.Add(swatch);

                    var lbl = new Label
                    {
                        Text = themeLabels[i],
                        ForeColor = Color.FromArgb(140, 160, 190),
                        Font = new Font(_fn, _fs - 1.5f),
                        AutoSize = true, BackColor = Color.Transparent,
                    };
                    gridPanel.Controls.Add(lbl);
                    cells[i] = (swatch, lbl, i % 3, i / 3);
                }

                gridPanel.Resize += (_, _) =>
                {
                    int colW = Math.Max(1, gridPanel.Width / 3);
                    foreach (var (sw, lb, col, row) in cells)
                    {
                        sw.Location = new Point(col * colW, row * gH + 1);
                        lb.Location = new Point(col * colW + (int)(22 * FontScale), row * gH + 3);
                    }
                };

                tab.Add(gridPanel, gridH + 12);
            }

            tab.Add(SectionLabel("폰트"), sH);

            {
                var row = new HLayout();
                row.Add(FieldLabel("폰트"));
                var fontItems = GetFontList();
                var dd = new DarkDropdown(fontItems, FindIndex(fontItems, settings.FontName));
                dd.SelectionChanged += idx =>
                {
                    if (idx >= 0 && idx < fontItems.Count)
                    { settings.FontName = fontItems[idx]; settings.SaveDebounced(); SettingsChanged?.Invoke(); }
                };
                row.Add(dd, 0.5f);
                tab.Add(row, srH);
            }

            {
                var row = new HLayout();
                row.Add(FieldLabel("굵기"));
                var weightItems = new List<string> { "Thin (100)", "Light (300)", "Regular (400)", "Medium (500)", "SemiBold (600)", "Bold (700)", "Black (900)" };
                int[] weightValues = { 100, 300, 400, 500, 600, 700, 900 };
                int curWeightIdx = 2;
                for (int i = 0; i < weightValues.Length; i++)
                    if (weightValues[i] == settings.FontWeight) { curWeightIdx = i; break; }
                var dd = new DarkDropdown(weightItems, curWeightIdx);
                dd.SelectionChanged += idx =>
                {
                    if (idx >= 0 && idx < weightValues.Length)
                    { settings.FontWeight = weightValues[idx]; settings.SaveDebounced(); SettingsChanged?.Invoke(); }
                };
                row.Add(dd, 0.4f);
                tab.Add(row, srH);
            }

            {
                var row = new HLayout();
                row.Add(FieldLabel("크기"));
                var sizeItems = new List<string> { "7", "7.5", "8", "8.5", "9", "9.5", "10", "10.5", "11", "12", "13", "14", "14.5", "15", "16", "17", "18", "19", "20", "21", "22", "23", "24" };
                var dd = new DarkDropdown(sizeItems, FindIndex(sizeItems, settings.FontSize.ToString("0.#")));
                dd.SelectionChanged += idx =>
                {
                    if (idx >= 0 && idx < sizeItems.Count && float.TryParse(sizeItems[idx], out float v))
                    { settings.FontSize = v; settings.SaveDebounced(); SettingsChanged?.Invoke(); }
                };
                row.Add(dd, 0.25f);
                tab.Add(row, brH);
            }

            tab.Add(SectionLabel("레이아웃"), sH);

            {
                var row = new HLayout();
                row.Add(FieldLabel("크기"));
                var slider = new StyledSlider(50, 250, settings.RowHeight) { Suffix = "", Height = 26 };
                slider.ValueChanged += v =>
                { settings.RowHeight = v; settings.SaveDebounced(); SettingsChanged?.Invoke(); };
                row.Add(slider, 0.5f);
                tab.Add(row, srH);
            }

            tabPanels[1].Controls.Add(tab);
            tab.Width = cw;
            tab.DoLayout();
            tabPanels[1].SetContentHeight(tab.Height);
        }

        // ══════════════════════════════════════════════════════════
        // Tab 2 — DPS
        // ══════════════════════════════════════════════════════════
        {
            var tab = new VLayout { BackColor = theme.BgColor };

            tab.Add(SectionLabel("DPS바"), sH);

            {
                var row = new HLayout();
                row.Add(FieldLabel("투명도"));
                var slider = new StyledSlider(5, 100, settings.BarOpacity) { Height = 26 };
                slider.ValueChanged += v =>
                { settings.BarOpacity = v; settings.SaveDebounced(); SettingsChanged?.Invoke(); };
                row.Add(slider, 0.5f);
                tab.Add(row, srH + 2);
            }

            tab.Add(SectionLabel("직업별 색상"), sH);

            {
                string[] jobNames = { "검성", "궁성", "마도성", "살성", "수호성", "정령성", "치유성", "호법성" };
                int numRows = (int)Math.Ceiling(jobNames.Length / 4.0);
                int gridH = numRows * gH;
                var gridPanel = new Panel { BackColor = Color.Transparent };
                var cells = new (ColorSwatch sw, Label lb, int c, int r)[jobNames.Length];

                for (int i = 0; i < jobNames.Length; i++)
                {
                    string jn = jobNames[i];
                    Color jc;
                    try { jc = ColorTranslator.FromHtml(settings.JobBarColors.GetHex(jn)); } catch { jc = Color.Gray; }
                    var swatch = new ColorSwatch(jc, i);
                    swatch.ColorPicked += (_, newColor) =>
                    {
                        settings.JobBarColors.SetHex(jn, ColorTranslator.ToHtml(newColor));
                        settings.SaveDebounced();
                        SettingsChanged?.Invoke();
                    };
                    gridPanel.Controls.Add(swatch);

                    var lbl = new Label
                    {
                        Text = jn,
                        ForeColor = Color.FromArgb(140, 160, 190),
                        Font = new Font(_fn, _fs - 1.5f),
                        AutoSize = true, BackColor = Color.Transparent,
                    };
                    gridPanel.Controls.Add(lbl);
                    cells[i] = (swatch, lbl, i % 4, i / 4);
                }

                gridPanel.Resize += (_, _) =>
                {
                    int colW = Math.Max(1, gridPanel.Width / 4);
                    foreach (var (sw, lb, col, row) in cells)
                    {
                        sw.Location = new Point(col * colW, row * gH + 1);
                        lb.Location = new Point(col * colW + (int)(22 * FontScale), row * gH + 3);
                    }
                };

                tab.Add(gridPanel, gridH + 12);
            }

            tab.Add(SectionLabel("표기"), sH);

            {
                var row = new HLayout();
                row.Add(FieldLabel("숫자"));
                var numFmtItems = new List<string> { "축약 (1.5M)", "그대로 (1,500,000)" };
                int numFmtIdx = settings.NumberFormat == "full" ? 1 : 0;
                var dd = new DarkDropdown(numFmtItems, numFmtIdx);
                dd.SelectionChanged += idx =>
                {
                    settings.NumberFormat = idx == 1 ? "full" : "abbreviated";
                    settings.SaveDebounced();
                    SettingsChanged?.Invoke();
                };
                row.Add(dd, 0.4f);
                tab.Add(row, srH);
            }

            {
                var row = new HLayout();
                row.Add(FieldLabel("기여도"));
                var pctModeItems = new List<string> { "총 딜량 대비", "보스 최대체력 대비" };
                int pctModeIdx = string.Equals(settings.DpsPercentMode, "boss", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                var dd = new DarkDropdown(pctModeItems, pctModeIdx);
                dd.SelectionChanged += idx =>
                {
                    settings.DpsPercentMode = idx == 1 ? "boss" : "party";
                    settings.SaveDebounced();
                    SettingsChanged?.Invoke();
                };
                row.Add(dd, 0.4f);
                tab.Add(row, brH);
            }

            tab.Add(SectionLabel("레이아웃"), sH);

            tab.Add(MakeBarSlotRow("슬롯 1 (이름 옆)", settings.BarSlot1, settings), srH);
            tab.Add(MakeBarSlotRow("슬롯 2 (오른쪽)", settings.BarSlot2, settings), srH);
            tab.Add(MakeBarSlotRow("슬롯 3 (맨 오른쪽)", settings.BarSlot3, settings), srH);

            tabPanels[2].Controls.Add(tab);
            tab.Width = cw;
            tab.DoLayout();
            tabPanels[2].SetContentHeight(tab.Height);
        }

        // ══════════════════════════════════════════════════════════
        // Tab 3 — 단축키
        // ══════════════════════════════════════════════════════════
        {
            var tab = new VLayout { BackColor = theme.BgColor };

            tab.Add(SectionLabel("단축키"), sH);

            var shortcuts = settings.Shortcuts;
            tab.Add(MakeShortcutRow("리셋", shortcuts.Reset, v => { shortcuts.Reset = v; settings.SaveDebounced(); }), srH);
            tab.Add(MakeShortcutRow("프로그램 재시작", shortcuts.Restart, v => { shortcuts.Restart = v; settings.SaveDebounced(); }), srH);
            tab.Add(MakeShortcutRow("익명 모드", shortcuts.Anonymous, v => { shortcuts.Anonymous = v; settings.SaveDebounced(); }), srH);
            tab.Add(MakeShortcutRow("컴팩트 모드", shortcuts.Compact, v => { shortcuts.Compact = v; settings.SaveDebounced(); }), srH);
            tab.Add(MakeShortcutRow("숨기기", shortcuts.Hide, v => { shortcuts.Hide = v; settings.SaveDebounced(); }), srH);
            tab.Add(MakeShortcutRow("탭 전환", shortcuts.SwitchTab, v => { shortcuts.SwitchTab = v; settings.SaveDebounced(); }), srH);

            tabPanels[3].Controls.Add(tab);
            tab.Width = cw;
            tab.DoLayout();
            tabPanels[3].SetContentHeight(tab.Height);
        }

        // ══════════════════════════════════════════════════════════
        // Assemble form
        // ══════════════════════════════════════════════════════════
        for (int t = 3; t >= 0; t--)
            Controls.Add(tabPanels[t]);
        Controls.Add(tabBar);
        Controls.Add(titleBar);
    }

    // ══════════════════════════════════════════════════════════════
    // Overrides
    // ══════════════════════════════════════════════════════════════

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32Native.WS_EX_TOOLWINDOW | Win32Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override void OnMove(EventArgs e) { base.OnMove(e); PersistBounds(); }
    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); PersistBounds(); }

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

    // ══════════════════════════════════════════════════════════════
    // Helpers
    // ══════════════════════════════════════════════════════════════

    private static int FindIndex(List<string> items, string value)
    {
        for (int i = 0; i < items.Count; i++)
            if (items[i].Equals(value, StringComparison.OrdinalIgnoreCase)) return i;
        return 0;
    }

    private static List<string> GetFontList()
    {
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
            var list = new List<string> { "Malgun Gothic", "Segoe UI" };
            using var ifc = new InstalledFontCollection();
            foreach (var f in ifc.Families) list.Add(f.Name);
            return list;
        }
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        ForeColor = AppSettings.Instance.Theme.TextDimColor,
        Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f, FontStyle.Bold),
        AutoSize = true, BackColor = Color.Transparent,
    };

    private static Label FieldLabel(string text) => new()
    {
        Text = text,
        ForeColor = AppSettings.Instance.Theme.TextColor,
        Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f),
        AutoSize = true, BackColor = Color.Transparent,
    };

    private static DarkToggle StyledCheckBox(bool isChecked)
        => new DarkToggle("", isChecked);

    private static Button StyledButton(string text, int width = 80)
    {
        var t = AppSettings.Instance.Theme;
        var btn = new Button
        {
            Text = text,
            Width = width, Height = (int)(26 * FontScale),
            FlatStyle = FlatStyle.Flat,
            BackColor = t.HeaderColor, ForeColor = t.TextColor,
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

    private HLayout MakeShortcutRow(string label, string currentValue, Action<string> onChanged)
    {
        var t = AppSettings.Instance.Theme;
        var row = new HLayout();
        row.Add(FieldLabel(label));

        var txt = new TextBox
        {
            Text = currentValue,
            Height = (int)(24 * FontScale),
            BackColor = t.HeaderColor, ForeColor = t.TextColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize),
        };
        txt.GotFocus += (_, _) => (Owner as OverlayForm)?.Hotkeys?.Suspend();
        txt.LostFocus += (_, _) =>
        {
            var hotkeys = (Owner as OverlayForm)?.Hotkeys;
            hotkeys?.Resume(AppSettings.Instance.Shortcuts);
        };
        txt.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) { txt.Text = currentValue; row.Focus(); return; }
            if (e.KeyCode == Keys.Back) { currentValue = ""; txt.Text = ""; onChanged(""); row.Focus(); return; }
            var parts = new List<string>();
            if (e.Alt) parts.Add("Alt");
            if (e.Control) parts.Add("Ctrl");
            if (e.Shift) parts.Add("Shift");
            var key = e.KeyCode;
            if (key != Keys.Menu && key != Keys.ControlKey && key != Keys.ShiftKey)
            {
                string keyName = key switch
                {
                    Keys.Oemtilde => "`", Keys.OemMinus => "-", Keys.OemQuestion => "/",
                    _ => key.ToString(),
                };
                parts.Add(keyName);
                txt.Text = string.Join("+", parts);
                currentValue = txt.Text;
                onChanged(txt.Text);
            }
        };
        row.Add(txt, 0.33f);
        return row;
    }

    private HLayout MakeBarSlotRow(string label, BarSlotConfig slot, AppSettings settings)
    {
        var row = new HLayout();
        row.Add(FieldLabel(label));

        var contentItems = new List<string> { "없음", "기여도", "대미지", "DPS" };
        int curIdx = slot.Content switch { "percent" => 1, "damage" => 2, "dps" => 3, _ => 0 };
        var dd = new DarkDropdown(contentItems, curIdx);
        dd.SelectionChanged += idx =>
        {
            slot.Content = idx switch { 1 => "percent", 2 => "damage", 3 => "dps", _ => "none" };
            settings.SaveDebounced();
            SettingsChanged?.Invoke();
        };
        row.Add(dd, 0.25f);

        var sizeItems = new List<string> { "7", "7.5", "8", "8.5", "9", "9.5", "10", "11" };
        var ddSize = new DarkDropdown(sizeItems, FindIndex(sizeItems, slot.FontSize.ToString("0.#")));
        ddSize.SelectionChanged += idx =>
        {
            if (idx >= 0 && idx < sizeItems.Count && float.TryParse(sizeItems[idx], out float v))
            { slot.FontSize = v; settings.SaveDebounced(); SettingsChanged?.Invoke(); }
        };
        row.Add(ddSize, 0.15f);

        Color c;
        try { c = ColorTranslator.FromHtml(slot.Color); } catch { c = Color.Gray; }
        var swatch = new ColorSwatch(c, 0);
        swatch.ColorPicked += (_, newColor) =>
        {
            slot.Color = ColorTranslator.ToHtml(newColor);
            settings.SaveDebounced();
            SettingsChanged?.Invoke();
        };
        row.Add(swatch);

        return row;
    }

    private static void OpenMyRecordsInBrowser()
    {
        string clientIdPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "A2Meter", "client_id");

        string clientId = "";
        try
        {
            if (System.IO.File.Exists(clientIdPath))
                clientId = System.IO.File.ReadAllText(clientIdPath).Trim();
        }
        catch { /* fall through */ }

        if (clientId.Length != 36)
        {
            MessageBox.Show(
                "기기 ID가 아직 생성되지 않았습니다.\n전투를 한 번 업로드한 후 다시 시도하세요.",
                "내 기록 보기", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string url = $"https://www.aion2meter.com/records/{clientId}";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"브라우저를 열지 못했습니다: {ex.Message}",
                "내 기록 보기", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ExportSettings(AppSettings settings)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "설정 내보내기", Filter = "JSON 파일|*.json", FileName = "a2meter_settings.json",
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
        System.IO.File.WriteAllText(dlg.FileName, json);
    }

    private static bool ImportSettings(AppSettings settings)
    {
        using var dlg = new OpenFileDialog { Title = "설정 불러오기", Filter = "JSON 파일|*.json" };
        if (dlg.ShowDialog() != DialogResult.OK) return false;
        try
        {
            var json = System.IO.File.ReadAllText(dlg.FileName);
            var imported = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, new System.Text.Json.JsonSerializerOptions
            { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            if (imported == null) return false;

            settings.OverlayOnlyWhenAion = imported.OverlayOnlyWhenAion;
            settings.GpuMode = imported.GpuMode;
            settings.GpuModeUserOverride = imported.GpuModeUserOverride;
            settings.Opacity = imported.Opacity;
            settings.BarOpacity = imported.BarOpacity;
            settings.FontName = imported.FontName;
            settings.FontWeight = imported.FontWeight;
            settings.FontSize = imported.FontSize;
            settings.Theme = imported.Theme;
            settings.FontScale = imported.FontScale;
            settings.RowHeight = imported.RowHeight;
            settings.Shortcuts = imported.Shortcuts ?? new ShortcutSettings();
            settings.DpsPercentMode = imported.DpsPercentMode;
            settings.NumberFormat = imported.NumberFormat;
            settings.ShowCombatPower = imported.ShowCombatPower;
            settings.ShowCombatScore = imported.ShowCombatScore;
            settings.LookupToastEnabled = imported.LookupToastEnabled;
            settings.SnapEnabled = imported.SnapEnabled;
            settings.SnapDistance = imported.SnapDistance;
            settings.JobBarColors = imported.JobBarColors ?? new AppSettings.JobBarColorSettings();
            settings.BarSlot1 = imported.BarSlot1;
            settings.BarSlot2 = imported.BarSlot2;
            settings.BarSlot3 = imported.BarSlot3;
            settings.Save();
            return true;
        }
        catch { }
        return false;
    }

    private static void ResetAllSettings(AppSettings settings)
    {
        var def = new AppSettings();
        settings.OverlayOnlyWhenAion = def.OverlayOnlyWhenAion;
        settings.GpuMode = def.GpuMode;
        settings.GpuModeUserOverride = def.GpuModeUserOverride;
        settings.Opacity = def.Opacity;
        settings.BarOpacity = def.BarOpacity;
        settings.FontName = def.FontName;
        settings.FontWeight = def.FontWeight;
        settings.FontSize = def.FontSize;
        settings.Theme = def.Theme;
        settings.FontScale = def.FontScale;
        settings.RowHeight = def.RowHeight;
        settings.Shortcuts = def.Shortcuts;
        settings.DpsPercentMode = def.DpsPercentMode;
        settings.NumberFormat = def.NumberFormat;
        settings.ShowCombatPower = def.ShowCombatPower;
        settings.ShowCombatScore = def.ShowCombatScore;
        settings.LookupToastEnabled = def.LookupToastEnabled;
        settings.SnapEnabled = def.SnapEnabled;
        settings.SnapDistance = def.SnapDistance;
        settings.JobBarColors = def.JobBarColors;
        settings.BarSlot1 = def.BarSlot1;
        settings.BarSlot2 = def.BarSlot2;
        settings.BarSlot3 = def.BarSlot3;
        settings.Save();
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

    private void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
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

    // ══════════════════════════════════════════════════════════════
    // Custom dark dropdown (owner-drawn, no native ComboBox)
    // ══════════════════════════════════════════════════════════════

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
            Height = (int)(28 * FontScale);
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
            _popup?.Close(); _popup = null; _open = false; Invalidate();
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
            string text = SelectedItem ?? "";
            using var font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize);
            var textRect = new Rectangle(10, 0, Width - 30, Height);
            TextRenderer.DrawText(g, text, font, textRect, FgNormal,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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

    // ── Popup list for dropdown ──

    private sealed class DropdownPopup : Form
    {
        public event Action<int>? ItemSelected;

        private readonly List<string> _items;
        private int _hoverIndex = -1;
        private int _selectedIndex;
        private readonly int ItemHeight;
        private const int MaxVisible = 10;
        private int _scrollOffset;

        public DropdownPopup(List<string> items, int selectedIndex, int width)
        {
            _items = items;
            _selectedIndex = selectedIndex;
            ItemHeight = (int)(26 * FontScale);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = AppSettings.Instance.Theme.BgColor;
            DoubleBuffered = true;
            int visibleCount = Math.Min(items.Count, MaxVisible);
            Size = new Size(Math.Max(width, (int)(120 * FontScale)), visibleCount * ItemHeight + 4);
            if (selectedIndex > MaxVisible - 3)
                _scrollOffset = Math.Min(selectedIndex - 3, Math.Max(0, items.Count - MaxVisible));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000008 | 0x00000080;
                cp.ClassStyle |= 0x0008;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); Close(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = (e.Y - 2) / ItemHeight + _scrollOffset;
            if (idx != _hoverIndex) { _hoverIndex = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            int idx = (e.Y - 2) / ItemHeight + _scrollOffset;
            if (idx >= 0 && idx < _items.Count) { _selectedIndex = idx; ItemSelected?.Invoke(idx); }
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

        protected override void OnMouseLeave(EventArgs e) { _hoverIndex = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
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

    // ══════════════════════════════════════════════════════════════
    // Close button
    // ══════════════════════════════════════════════════════════════

    private sealed class CloseButton : Control
    {
        private bool _hover, _pressed;
        public CloseButton()
        {
            int sz = (int)(26 * FontScale);
            Size = new Size(sz, sz); DoubleBuffered = true;
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

    // ══════════════════════════════════════════════════════════════
    // Styled slider
    // ══════════════════════════════════════════════════════════════

    private sealed class StyledSlider : Control
    {
        public event Action<int>? ValueChanged;
        public string Suffix { get; set; } = "%";
        private int _min, _max, _value;
        private bool _dragging, _hover;
        private readonly int ThumbR, TrackH;

        public StyledSlider(int min, int max, int value)
        {
            float s = FontScale;
            ThumbR = (int)(7 * s);
            TrackH = Math.Max(3, (int)(4 * s));
            _min = min; _max = max; _value = Math.Clamp(value, min, max);
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
        }

        private int TL => ThumbR;
        private int TR => Width - ThumbR - (int)(44 * FontScale);
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
            using (var tb = new SolidBrush(Color.FromArgb(30, 40, 60)))
            {
                var rect = new RectangleF(TL, cy - TrackH / 2f, TR - TL, TrackH);
                using var path = RoundRectF(rect, TrackH / 2f);
                g.FillPath(tb, path);
            }
            if (tx > TL)
            {
                var accent = AppSettings.Instance.Theme.AccentColor;
                var c = _hover || _dragging ? ControlPaint.Light(accent, 0.3f) : accent;
                using var fb = new SolidBrush(c);
                var rect = new RectangleF(TL, cy - TrackH / 2f, tx - TL, TrackH);
                using var path = RoundRectF(rect, TrackH / 2f);
                g.FillPath(fb, path);
            }
            var tc = _dragging ? Color.FromArgb(220, 235, 255) : _hover ? Color.FromArgb(200, 220, 250) : AppSettings.Instance.Theme.TextColor;
            using (var tb2 = new SolidBrush(tc))
                g.FillEllipse(tb2, tx - ThumbR, cy - ThumbR, ThumbR * 2, ThumbR * 2);
            using (var rp = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                g.DrawEllipse(rp, tx - ThumbR, cy - ThumbR, ThumbR * 2, ThumbR * 2);
            using var font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 1f);
            using var lb = new SolidBrush(Color.FromArgb(140, 165, 200));
            var valSz = g.MeasureString($"{_value}{Suffix}", font);
            g.DrawString($"{_value}{Suffix}", font, lb, TR + 10, cy - valSz.Height / 2);
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

    // ══════════════════════════════════════════════════════════════
    // Color swatch (clickable circle that opens ColorDialog)
    // ══════════════════════════════════════════════════════════════

    private sealed class ColorSwatch : Control
    {
        public event Action<int, Color>? ColorPicked;
        private Color _color;
        private readonly int _index;
        private bool _hover;

        public ColorSwatch(Color color, int index)
        {
            _color = color; _index = index;
            int sz = (int)(18 * FontScale);
            Size = new Size(sz, sz);
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public Color SwatchColor { get => _color; set { _color = value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            using var dlg = new ColorDialog { Color = _color, FullOpen = true, AnyColor = true };
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

    // ══════════════════════════════════════════════════════════════
    // Dark toggle switch
    // ══════════════════════════════════════════════════════════════

    private sealed class DarkToggle : Control
    {
        public event EventHandler? CheckedChanged;
        private bool _checked, _hover;
        private readonly int TrackW, TrackH, ThumbR;
        private const int Gap = 8;
        private int _textW;

        public DarkToggle(string text, bool isChecked)
        {
            float s = FontScale;
            TrackW = (int)(36 * s);
            TrackH = (int)(18 * s);
            ThumbR = (int)(7 * s);
            _checked = isChecked;
            Text = text;
            Height = (int)(22 * s);
            _textW = string.IsNullOrEmpty(text) ? 0
                : TextRenderer.MeasureText(text, new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize)).Width;
            Width = _textW > 0 ? _textW + Gap + TrackW : TrackW;
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

            // Text first (left side)
            if (_textW > 0)
            {
                using var font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize);
                var textRect = new Rectangle(0, 0, _textW, Height);
                TextRenderer.DrawText(g, Text, font, textRect, t.TextColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }

            // Toggle track (right side, or x=0 if no text)
            int trackX = _textW > 0 ? _textW + Gap : 0;
            var trackRect = new RectangleF(trackX, cy - TrackH / 2f, TrackW, TrackH);
            var trackColor = _checked ? t.AccentColor : (_hover ? Color.FromArgb(45, 55, 80) : Color.FromArgb(30, 40, 60));
            using (var brush = new SolidBrush(trackColor))
            using (var path = RoundRectF(trackRect, TrackH / 2f))
                g.FillPath(brush, path);
            float thumbX = _checked ? trackX + TrackW - ThumbR - 3 : trackX + ThumbR + 3;
            using (var brush = new SolidBrush(Color.FromArgb(240, 245, 255)))
                g.FillEllipse(brush, thumbX - ThumbR, cy - ThumbR, ThumbR * 2, ThumbR * 2);
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

    // ══════════════════════════════════════════════════════════════
    // VLayout / HLayout (Android LinearLayout-style containers)
    // ══════════════════════════════════════════════════════════════

    private sealed class VLayout : Panel
    {
        private readonly List<(Control ctrl, int rowH, int indent)> _rows = new();
        private bool _inLayout;
        public int PadX = PX;
        public int PadTop = 12;
        public int PadBottom = 8;

        public VLayout() { DoubleBuffered = true; }

        public void Add(Control c, int rowHeight, int indent = 0)
        {
            _rows.Add((c, rowHeight, indent));
            Controls.Add(c);
        }

        public void DoLayout()
        {
            if (_inLayout || ClientSize.Width <= 0) return;
            _inLayout = true;
            try
            {
                int y = PadTop;
                int w = ClientSize.Width;
                foreach (var (ctrl, rowH, indent) in _rows)
                {
                    int x = PadX + indent;
                    int itemW = w - PadX - x;
                    int h = rowH;
                    if (ctrl is HLayout hl)
                    {
                        h = Math.Max(rowH, hl.MinHeight);
                        hl.SetBounds(x, y, itemW, h);
                        hl.DoLayout();
                    }
                    else if (ctrl.AutoSize)
                    {
                        ctrl.Location = new Point(x, y);
                        h = Math.Max(rowH, ctrl.Height);
                    }
                    else
                    {
                        h = Math.Max(rowH, ctrl.Height);
                        ctrl.SetBounds(x, y, itemW, h);
                    }
                    y += h;
                }
                int totalH = y + PadBottom;
                if (Height != totalH) Height = totalH;
            }
            finally { _inLayout = false; }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (ClientSize.Width > 0) DoLayout();
        }
    }

    private sealed class HLayout : Panel
    {
        private readonly List<(Control ctrl, float ratio)> _items = new();
        public int Gap = 6;
        public bool Spread = true;
        public bool FillWidth = false;

        /// Minimum height needed so that no child control is clipped.
        public int MinHeight
        {
            get
            {
                int max = 0;
                foreach (var (c, _) in _items)
                    if (c.Height > max) max = c.Height;
                return max + 4;
            }
        }

        public HLayout()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Color.Transparent;
        }

        public void Add(Control c, float widthRatio = 0)
        {
            _items.Add((c, widthRatio));
            Controls.Add(c);
        }

        public void DoLayout()
        {
            if (_items.Count == 0) return;
            int h = Height, w = Width;
            if (Spread && _items.Count >= 2)
            {
                // Apply dynamic widths from ratio
                for (int i = 1; i < _items.Count; i++)
                {
                    var (c, r) = _items[i];
                    if (r > 0) c.Width = Math.Max(1, (int)(w * r));
                }

                var first = _items[0].ctrl;
                first.Location = new Point(0, Math.Max(0, (h - first.Height) / 2));
                int rx = w;
                for (int i = _items.Count - 1; i >= 1; i--)
                {
                    rx -= _items[i].ctrl.Width;
                    _items[i].ctrl.Location = new Point(rx, Math.Max(0, (h - _items[i].ctrl.Height) / 2));
                    rx -= Gap;
                }
            }
            else if (FillWidth && _items.Count > 0)
            {
                int totalGap = (_items.Count - 1) * Gap;
                int itemW = (w - totalGap) / _items.Count;
                int x = 0;
                foreach (var (c, _) in _items)
                {
                    c.Width = itemW;
                    c.Location = new Point(x, Math.Max(0, (h - c.Height) / 2));
                    x += itemW + Gap;
                }
            }
            else
            {
                int x = 0;
                foreach (var (c, _) in _items)
                {
                    c.Location = new Point(x, Math.Max(0, (h - c.Height) / 2));
                    x += c.Width + Gap;
                }
            }
        }
    }

    // ══════════════════════════════════════════════════════════════
    // Dark scroll panel (custom-painted thin scrollbar)
    // ══════════════════════════════════════════════════════════════

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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Controls.Count > 0 && Controls[0] is Control inner)
            {
                inner.Width = ClientSize.Width;
                _contentHeight = inner.Height;
            }
            ClampScroll();
            Invalidate();
        }

        private int ViewH => ClientSize.Height;
        private bool NeedsScroll => _contentHeight > ViewH;
        private int MaxScroll => Math.Max(0, _contentHeight - ViewH);

        private void ClampScroll()
        {
            _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScroll);
            if (Controls.Count > 0 && Controls[0] is Control inner)
                inner.Top = -_scrollOffset;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!NeedsScroll) { base.OnMouseWheel(e); return; }
            _scrollOffset -= e.Delta / 4;
            ClampScroll(); Invalidate();
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
                ClampScroll(); Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _thumbDrag = false; Capture = false;
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
