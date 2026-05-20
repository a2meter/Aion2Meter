using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

/// Update detail window: shows version, release notes, and a download button.
internal sealed class UpdateDetailForm : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private readonly Version _version;
    private readonly string _downloadUrl;
    private readonly Button _btnDownload;

    public UpdateDetailForm(Version version, string downloadUrl, string releaseNotes)
    {
        _version = version;
        _downloadUrl = downloadUrl;

        var theme = AppSettings.Instance.Theme;
        var fn = AppSettings.Instance.FontName;
        var fs = AppSettings.Instance.FontSize;

        Text = "업데이트";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(420, 360);
        BackColor = theme.BgColor;
        DoubleBuffered = true;

        // ── Title bar ──
        var titleBar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = theme.HeaderColor };
        titleBar.Paint += (_, e) =>
        {
            using var pen = new Pen(theme.BorderColor);
            e.Graphics.DrawLine(pen, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1);
        };
        var lblTitle = new Label
        {
            Text = $"v{version} 업데이트",
            ForeColor = theme.TextColor,
            Font = new Font(fn, fs + 0.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(12, 9),
            BackColor = Color.Transparent,
        };
        titleBar.Controls.Add(lblTitle);
        titleBar.MouseDown += (_, e) => Drag(e);
        lblTitle.MouseDown += (_, e) => Drag(e);

        var btnClose = new CloseButton { Location = new Point(titleBar.Width - 34, 5) };
        titleBar.Controls.Add(btnClose);
        titleBar.Resize += (_, _) => btnClose.Location = new Point(titleBar.Width - 34, 5);
        btnClose.Click += (_, _) => Close();

        // ── Version info ──
        var lblVersion = new Label
        {
            Text = $"현재: v{AutoUpdater.CurrentVersion}  →  새 버전: v{version}",
            ForeColor = theme.AccentColor,
            Font = new Font(fn, fs, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 48),
            BackColor = Color.Transparent,
        };

        // ── Release notes ──
        var lblNotesHeader = new Label
        {
            Text = "릴리즈 노트",
            ForeColor = theme.TextDimColor,
            Font = new Font(fn, fs - 0.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 76),
            BackColor = Color.Transparent,
        };

        var txtNotes = new RichTextBox
        {
            Text = string.IsNullOrWhiteSpace(releaseNotes) ? "(릴리즈 노트 없음)" : releaseNotes,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = theme.HeaderColor,
            ForeColor = theme.TextColor,
            Font = new Font(fn, fs),
            Location = new Point(16, 98),
            Size = new Size(388, 190),
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };

        // ── Download button ──
        _btnDownload = new Button
        {
            Text = "다운로드 및 업데이트",
            FlatStyle = FlatStyle.Flat,
            BackColor = theme.AccentColor,
            ForeColor = Color.FromArgb(20, 24, 36),
            Font = new Font(fn, fs + 0.5f, FontStyle.Bold),
            Size = new Size(200, 34),
            Location = new Point((420 - 200) / 2, 310),
            Cursor = Cursors.Hand,
        };
        _btnDownload.FlatAppearance.BorderSize = 0;
        _btnDownload.Click += OnDownloadClick;

        Controls.Add(_btnDownload);
        Controls.Add(txtNotes);
        Controls.Add(lblNotesHeader);
        Controls.Add(lblVersion);
        Controls.Add(titleBar);

        // Resize handler for notes box
        Resize += (_, _) =>
        {
            txtNotes.Size = new Size(ClientSize.Width - 32, ClientSize.Height - 170);
            _btnDownload.Location = new Point((ClientSize.Width - _btnDownload.Width) / 2, ClientSize.Height - 50);
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

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(AppSettings.Instance.Theme.BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }

    private void OnDownloadClick(object? sender, EventArgs e)
    {
        try
        {
            _btnDownload.Enabled = false;
            _btnDownload.Text = "업데이트 중...";

            AppSettings.Instance.Save();
            AutoUpdater.LaunchUpdaterAndExit(msg => Console.Error.WriteLine(msg));
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[updater] launch failed: {ex.Message}");
            _btnDownload.Text = "실패 — 다시 시도";
            _btnDownload.Enabled = true;
        }
    }

    // ─── Close button (same as SettingsPanelForm) ───

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
                g.FillEllipse(bg, 0, 0, Width, Height);
            }
            var fg = _hover ? Color.FromArgb(235, 240, 250) : AppSettings.Instance.Theme.TextColor;
            using var pen = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int cx = Width / 2, cy = Height / 2;
            g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
            g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
        }
    }
}
