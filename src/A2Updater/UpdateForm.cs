using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace A2Updater;

/// 업데이트 확인 다이얼로그. 닫기 = DialogResult.Cancel (아니오), 다운로드 클릭 = DialogResult.OK (예).
/// 본체의 UpdateDetailForm을 standalone 형태로 옮긴 것. AppSettings 의존성 없이 색상은 하드코딩.
internal sealed class UpdateForm : Form
{
    // Theme constants (matches A2Meter default theme).
    private static readonly Color BgColor       = Color.FromArgb(0x1E, 0x1E, 0x2A);
    private static readonly Color HeaderColor   = Color.FromArgb(0x25, 0x25, 0x35);
    private static readonly Color BorderColor   = Color.FromArgb(0x3A, 0x3A, 0x4A);
    private static readonly Color TextColor     = Color.FromArgb(0xD5, 0xD5, 0xDB);
    private static readonly Color TextDimColor  = Color.FromArgb(0xC2, 0xC2, 0xCD);
    private static readonly Color AccentColor   = Color.FromArgb(0x4D, 0xE8, 0xE0);

    private const string FontName = "Malgun Gothic";
    private const float  FontSize = 10.5f;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private readonly Button _btnDownload;

    public UpdateForm(Version currentVersion, Version newVersion, string releaseNotes)
    {
        Text = "업데이트";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(420, 360);
        BackColor = BgColor;
        DoubleBuffered = true;

        // ── Title bar ──
        var titleBar = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = HeaderColor };
        titleBar.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 0, titleBar.Height - 1, titleBar.Width, titleBar.Height - 1);
        };
        var lblTitle = new Label
        {
            Text = $"v{newVersion} 업데이트",
            ForeColor = TextColor,
            Font = new Font(FontName, FontSize + 0.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(12, 9),
            BackColor = Color.Transparent,
        };
        titleBar.Controls.Add(lblTitle);
        titleBar.MouseDown += Drag;
        lblTitle.MouseDown += Drag;

        var btnClose = new CloseButton { Location = new Point(titleBar.Width - 34, 5) };
        titleBar.Controls.Add(btnClose);
        titleBar.Resize += (_, _) => btnClose.Location = new Point(titleBar.Width - 34, 5);
        btnClose.Click += (_, _) => Close();

        // ── Version info ──
        var lblVersion = new Label
        {
            Text = $"현재: v{currentVersion}  →  새 버전: v{newVersion}",
            ForeColor = AccentColor,
            Font = new Font(FontName, FontSize, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 48),
            BackColor = Color.Transparent,
        };

        // ── Release notes ──
        var lblNotesHeader = new Label
        {
            Text = "릴리즈 노트",
            ForeColor = TextDimColor,
            Font = new Font(FontName, FontSize - 0.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 76),
            BackColor = Color.Transparent,
        };

        var txtNotes = new RichTextBox
        {
            Text = string.IsNullOrWhiteSpace(releaseNotes) ? "(릴리즈 노트 없음)" : releaseNotes,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = HeaderColor,
            ForeColor = TextColor,
            Font = new Font(FontName, FontSize),
            Location = new Point(16, 98),
            Size = new Size(388, 190),
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };

        // ── Download button ──
        _btnDownload = new Button
        {
            Text = "다운로드 및 업데이트",
            FlatStyle = FlatStyle.Flat,
            BackColor = AccentColor,
            ForeColor = Color.FromArgb(20, 24, 36),
            Font = new Font(FontName, FontSize + 0.5f, FontStyle.Bold),
            Size = new Size(200, 34),
            Location = new Point((420 - 200) / 2, 310),
            Cursor = Cursors.Hand,
        };
        _btnDownload.FlatAppearance.BorderSize = 0;
        _btnDownload.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.Add(_btnDownload);
        Controls.Add(txtNotes);
        Controls.Add(lblNotesHeader);
        Controls.Add(lblVersion);
        Controls.Add(titleBar);

        Resize += (_, _) =>
        {
            txtNotes.Size = new Size(ClientSize.Width - 32, ClientSize.Height - 170);
            _btnDownload.Location = new Point((ClientSize.Width - _btnDownload.Width) / 2, ClientSize.Height - 50);
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    private void Drag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }

    // ─── Close button ───

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
            var fg = _hover ? Color.FromArgb(235, 240, 250) : TextColor;
            using var pen = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int cx = Width / 2, cy = Height / 2;
            g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
            g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
        }
    }
}
