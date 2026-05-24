using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using A2Meter.Api;
using A2Meter.Core;
using A2Meter.Dps;

namespace A2Meter.Forms;

/// Floating toast shown at the bottom of the overlay when a 07 97 party
/// request packet is observed. Renders the requester's display info
/// (name / job / Lv / CP / server) and a tier badge that updates
/// asynchronously once the web-side tier lookup returns.
internal sealed class PartyRequestToastForm : Form
{
    private const int ToastWidth = 340;
    private const int ToastHeight = 96;

    private readonly Form _parent;
    private readonly PartyMember _member;

    private string _tierText = "티어 조회 중...";
    private Color  _tierColor = Color.FromArgb(140, 150, 170);
    private System.Windows.Forms.Timer? _autoCloseTimer;

    public PartyRequestToastForm(Form parent, PartyMember member)
    {
        _parent = parent;
        _member = member;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Owner = parent;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(ToastWidth, ToastHeight);
        BackColor = AppSettings.Instance.Theme.HeaderColor;
        Opacity = 0.97;
        DoubleBuffered = true;

        // Close X (top-right corner, lightweight)
        var btnClose = new ToastCloseButton { Location = new Point(ToastWidth - 24, 4) };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        // Auto-dismiss after 15 seconds
        _autoCloseTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();

        Paint += OnPaint;

        PlaceAtBottom();
        parent.Move += OnParentMoved;
        parent.Resize += OnParentMoved;

        FormClosed += (_, _) =>
        {
            parent.Move -= OnParentMoved;
            parent.Resize -= OnParentMoved;
            _autoCloseTimer?.Stop();
            _autoCloseTimer?.Dispose();
        };

        // Kick off the async tier fetch.
        _ = LoadTierAsync();
    }

    private void OnParentMoved(object? sender, EventArgs e) => PlaceAtBottom();

    private void PlaceAtBottom()
    {
        if (_parent.IsDisposed) return;
        int x = _parent.Left + (_parent.Width - Width) / 2;
        int y = _parent.Bottom - Height - 6;
        Location = new Point(x, y);
    }

    private async System.Threading.Tasks.Task LoadTierAsync()
    {
        try
        {
            var resp = await PlayerTierClient.FetchAsync(_member.Nickname, _member.ServerId).ConfigureAwait(false);
            if (IsDisposed) return;

            if (resp == null || resp.Dungeons.Count == 0)
            {
                BeginInvoke(() =>
                {
                    _tierText = "기록 없음";
                    _tierColor = Color.FromArgb(120, 130, 150);
                    Invalidate();
                });
                return;
            }

            // Pick the top-ranked dungeon (sorted by zScore desc on the server).
            var top = resp.Dungeons[0];
            BeginInvoke(() =>
            {
                _tierText = $"{TierKo(top.Tier)} (n={top.SampleCount})";
                _tierColor = TierColor(top.Tier);
                Invalidate();
            });
        }
        catch
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                _tierText = "조회 실패";
                _tierColor = Color.FromArgb(180, 90, 90);
                Invalidate();
            });
        }
    }

    private static string TierKo(string tier) => tier switch
    {
        "Grandmaster" => "그랜드마스터",
        "Master"      => "마스터",
        "Diamond"     => "다이아",
        "Platinum"    => "플레티넘",
        "Gold"        => "골드",
        "Silver"      => "실버",
        "Bronze"      => "브론즈",
        _             => tier,
    };

    private static Color TierColor(string tier) => tier switch
    {
        "Grandmaster" => Color.FromArgb(255, 90, 90),
        "Master"      => Color.FromArgb(220, 80, 220),
        "Diamond"     => Color.FromArgb(110, 200, 240),
        "Platinum"    => Color.FromArgb(140, 200, 180),
        "Gold"        => Color.FromArgb(230, 200, 100),
        "Silver"      => Color.FromArgb(180, 190, 200),
        "Bronze"      => Color.FromArgb(180, 130, 80),
        _             => Color.FromArgb(140, 150, 170),
    };

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080 /* WS_EX_TOOLWINDOW */ | 0x00000008 /* WS_EX_TOPMOST */;
            return cp;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var bg = new SolidBrush(BackColor);
        using var path = RoundRect(0, 0, Width - 1, Height - 1, 6);
        g.FillPath(bg, path);
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var theme = AppSettings.Instance.Theme;
        var fn = AppSettings.Instance.FontName;
        var fs = AppSettings.Instance.FontSize;

        // Border
        using (var pen = new Pen(theme.BorderColor))
        using (var path = RoundRect(0, 0, Width - 1, Height - 1, 6))
            g.DrawPath(pen, path);

        // Left accent bar (uses tier color)
        using (var accent = new SolidBrush(_tierColor))
            g.FillRectangle(accent, 0, 10, 4, Height - 20);

        // Header: "파티 신청"
        using (var headerFont = new Font(fn, fs - 1f, FontStyle.Bold))
        using (var headerBrush = new SolidBrush(theme.TextDimColor))
            g.DrawString("파티 신청", headerFont, headerBrush, 12, 8);

        // Requester name (big)
        using (var nameFont = new Font(fn, fs + 1.5f, FontStyle.Bold))
        using (var nameBrush = new SolidBrush(theme.TextColor))
            g.DrawString(_member.Nickname, nameFont, nameBrush, 12, 24);

        // Sub-line: job / Lv / CP / server
        string sub = $"{_member.JobName} · Lv{_member.Level} · CP {_member.CombatPower:N0}";
        if (!string.IsNullOrEmpty(_member.ServerName))
            sub += $" · {_member.ServerName}";
        using (var subFont = new Font(fn, fs - 1f))
        using (var subBrush = new SolidBrush(theme.TextDimColor))
            g.DrawString(sub, subFont, subBrush, 12, 50);

        // Tier badge (bottom-right)
        using (var tierFont = new Font(fn, fs + 0.5f, FontStyle.Bold))
        {
            var size = g.MeasureString(_tierText, tierFont);
            float bx = Width - size.Width - 16;
            float by = Height - size.Height - 10;
            using var tierBrush = new SolidBrush(_tierColor);
            g.DrawString(_tierText, tierFont, tierBrush, bx, by);
        }
    }

    private static GraphicsPath RoundRect(int x, int y, int w, int h, int r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    private sealed class ToastCloseButton : Control
    {
        private bool _hover;
        public ToastCloseButton()
        {
            Size = new Size(20, 20);
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var fg = _hover ? Color.FromArgb(235, 140, 140) : AppSettings.Instance.Theme.TextDimColor;
            using var pen = new Pen(fg, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int cx = Width / 2, cy = Height / 2;
            g.DrawLine(pen, cx - 4, cy - 4, cx + 4, cy + 4);
            g.DrawLine(pen, cx + 4, cy - 4, cx - 4, cy + 4);
        }
    }
}
