using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

/// Small toast notification shown at the bottom of the overlay when an update is available.
/// Styled to match the OverlayHeaderPanel (same theme colors, owner-drawn buttons).
internal sealed class UpdateToastForm : Form
{
    private readonly Version _version;
    private readonly Form _parent;

    private readonly ToastButton _btnUpdate;
    private readonly ToastButton _btnClose;

    public UpdateToastForm(Form parent, Version version)
    {
        _parent = parent;
        _version = version;

        var theme = AppSettings.Instance.Theme;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Owner = parent;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(260, 36);
        BackColor = theme.HeaderColor;
        Opacity = 0.96;
        DoubleBuffered = true;

        _btnUpdate = new ToastButton("업데이트", theme.AccentColor, isAccent: true)
        {
            Size = new Size(64, 22),
            Location = new Point(168, 7),
        };
        _btnUpdate.Click += OnUpdateClick;
        Controls.Add(_btnUpdate);

        _btnClose = new ToastButton("✕", theme.TextDimColor, isAccent: false)
        {
            Size = new Size(22, 22),
            Location = new Point(Width - 30, 7),
        };
        _btnClose.Click += (_, _) => Close();
        Controls.Add(_btnClose);

        Paint += OnPaint;

        PlaceAtBottom();
        parent.Move += (_, _) => PlaceAtBottom();
        parent.Resize += (_, _) => PlaceAtBottom();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var theme = AppSettings.Instance.Theme;

        // Border (1px rounded rectangle).
        using var borderPen = new Pen(theme.BorderColor);
        using var borderPath = RoundRect(0, 0, Width - 1, Height - 1, 6);
        g.DrawPath(borderPen, borderPath);

        // Subtle accent bar on the left edge.
        using var accentBrush = new SolidBrush(theme.AccentColor);
        g.FillRectangle(accentBrush, 0, 8, 3, Height - 16);

        // Text: version info.
        var text = $"v{_version} 업데이트 가능";
        var fontStyle = AppSettings.Instance.FontWeight >= 600 ? FontStyle.Bold : FontStyle.Regular;
        using var font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize, fontStyle);
        using var textBrush = new SolidBrush(theme.TextColor);
        var textRect = new RectangleF(12, 0, 150, Height);
        var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, textBrush, textRect, sf);
    }

    private void PlaceAtBottom()
    {
        if (_parent.IsDisposed) return;
        int x = _parent.Left + (_parent.Width - Width) / 2;
        int y = _parent.Bottom - Height - 6;
        Location = new Point(x, y);
    }

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
        using var bgBrush = new SolidBrush(BackColor);
        using var bgPath = RoundRect(0, 0, Width - 1, Height - 1, 6);
        g.FillPath(bgBrush, bgPath);
    }

    private void OnUpdateClick(object? sender, EventArgs e)
    {
        // The detailed update UI lives in A2Updater now; the toast just launches it.
        AppSettings.Instance.Save();
        AutoUpdater.LaunchUpdaterAndExit(msg => Console.Error.WriteLine(msg));
        Environment.Exit(0);
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

    // ─── Owner-drawn button matching OverlayHeaderPanel.IconButton style ───

    private sealed class ToastButton : Control
    {
        private readonly Color _fgColor;
        private readonly bool _isAccent;
        private bool _hover;
        private bool _pressed;

        public ToastButton(string text, Color fgColor, bool isAccent)
        {
            _fgColor = fgColor;
            _isAccent = isAccent;
            Text = text;
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; base.OnMouseUp(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var theme = AppSettings.Instance.Theme;

            // Background: accent button gets a filled rounded rect; close button gets hover only.
            if (_isAccent)
            {
                int alpha = _pressed ? 255 : _hover ? 220 : 180;
                using var bg = new SolidBrush(Color.FromArgb(alpha, _fgColor));
                using var path = RoundRectF(new RectangleF(0, 0, Width, Height), 4);
                g.FillPath(bg, path);
            }
            else if (_hover)
            {
                int alpha = _pressed ? 110 : 70;
                using var bg = new SolidBrush(Color.FromArgb(alpha, 60, 100, 160));
                using var path = RoundRectF(new RectangleF(0, 0, Width, Height), 4);
                g.FillPath(bg, path);
            }

            // Text.
            Color textColor;
            if (_isAccent)
                textColor = Color.FromArgb(20, 24, 36); // dark text on accent bg
            else
                textColor = _hover ? Color.FromArgb(235, 240, 250) : _fgColor;

            var baseStyle = AppSettings.Instance.FontWeight >= 600 ? FontStyle.Bold : FontStyle.Regular;
            var style = _isAccent ? FontStyle.Bold : baseStyle;
            using var font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f, style);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var brush = new SolidBrush(textColor);
            g.DrawString(Text, font, brush, new RectangleF(0, 0, Width, Height), sf);
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
}
