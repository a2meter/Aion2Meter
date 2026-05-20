using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;

namespace A2Meter.Forms;

/// Native WinForms replacement for the WebView2 header strip.
/// Three icon buttons on the right (lock / settings / close), a brand label on
/// the left. Owner-drawn buttons keep the look consistent with the dark D2D
/// canvas below and avoid the OS theme bleeding through.
internal sealed class OverlayHeaderPanel : Panel
{
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION        = 2;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    public event Action<bool>? LockToggled;
    public event Action<bool>? AnonymousToggled;
    public event Action?       HistoryClicked;
    public event Action?       SettingsClicked;
    public event Action?       CloseClicked;
    public event Action<int>?  OpacityChanged;

    private readonly IconButton _btnLock;
    private readonly IconButton _btnAnon;
    private readonly IconButton _btnHistory;
    private readonly IconButton _btnSettings;
    private readonly IconButton _btnClose;
    private readonly SlimSlider _sliderOpacity;
    private readonly Label      _brand;

    private bool _locked;
    private bool _anonymous;

    public OverlayHeaderPanel()
    {
        Dock      = DockStyle.Top;
        Height    = 36;
        BackColor = AppSettings.Instance.Theme.HeaderColor;
        DoubleBuffered = true;

        _brand = new Label
        {
            Text      = $"A2Meter v{AutoUpdater.CurrentVersion.ToString(3)}",
            ForeColor = AppSettings.Instance.Theme.TextDimColor,
            Font      = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize, FontStyle.Regular),
            AutoSize  = true,
            Location  = new Point(8, 10),
            BackColor = Color.Transparent,
        };
        _brand.MouseDown += (_, e) => BeginParentDrag(e);
        this.MouseDown   += (_, e) => OnHeaderMouseDown(e);
        this.MouseMove   += (_, e) => OnHeaderMouseMove(e);

        _btnLock     = new IconButton(IconKind.Unlock)   { TabStop = false };
        _btnAnon     = new IconButton(IconKind.Eye)      { TabStop = false };
        _btnHistory  = new IconButton(IconKind.History)  { TabStop = false };
        _btnSettings = new IconButton(IconKind.Settings) { TabStop = false };
        _btnClose    = new IconButton(IconKind.Close)    { TabStop = false, HoverColor = Color.FromArgb(220, 70, 70) };

        _sliderOpacity = new SlimSlider(20, 100, Math.Clamp(Core.AppSettings.Instance.Opacity, 20, 100))
        {
            Width = 72,
            Height = 20,
            TabStop = false,
        };
        _sliderOpacity.ValueChanged += v => OpacityChanged?.Invoke(v);

        _btnLock.Click     += (_, _) => { _locked = !_locked; _btnLock.Kind = _locked ? IconKind.Lock : IconKind.Unlock; _btnLock.Invalidate(); LockToggled?.Invoke(_locked); };
        _btnAnon.Click     += (_, _) => { _anonymous = !_anonymous; _btnAnon.Kind = _anonymous ? IconKind.EyeOff : IconKind.Eye; _btnAnon.Invalidate(); AnonymousToggled?.Invoke(_anonymous); };
        _btnHistory.Click  += (_, _) => HistoryClicked?.Invoke();
        _btnSettings.Click += (_, _) => SettingsClicked?.Invoke();
        _btnClose.Click    += (_, _) => CloseClicked?.Invoke();

        Controls.Add(_btnLock);
        Controls.Add(_btnAnon);
        Controls.Add(_btnHistory);
        Controls.Add(_btnSettings);
        Controls.Add(_sliderOpacity);
        Controls.Add(_btnClose);
        Controls.Add(_brand);

        Resize += (_, _) => LayoutButtons();
        LayoutButtons();
    }

    /// Externally force unlock state (e.g. from tray menu).
    public void ForceUnlock()
    {
        _locked = false;
        _btnLock.Kind = IconKind.Unlock;
        _btnLock.Invalidate();
    }

    public void SetAnonymous(bool anon)
    {
        _anonymous = anon;
        _btnAnon.Kind = anon ? IconKind.EyeOff : IconKind.Eye;
        _btnAnon.Invalidate();
    }

    /// Returns true if the client point hits the lock button.
    public bool IsLockButtonArea(Point clientPt)
        => _btnLock.Bounds.Contains(clientPt);

    /// Returns true if a hit at the given client point should be treated as a
    /// title-bar drag region (so the OverlayForm can move on click-drag).
    public bool IsDragArea(Point clientPt)
    {
        if (_locked) return false;
        var hit = GetChildAtPoint(clientPt);
        return hit is null || hit == _brand;
    }

    private void LayoutButtons()
    {
        const int btnSize = 26;
        const int gap     = 4;
        int y  = (Height - btnSize) / 2;
        int x  = Width - 8 - btnSize;
        _btnClose.SetBounds(x, y, btnSize, btnSize);
        x -= btnSize + gap;
        _btnSettings.SetBounds(x, y, btnSize, btnSize);
        x -= btnSize + gap;
        _btnHistory.SetBounds(x, y, btnSize, btnSize);
        x -= btnSize + gap;
        _btnAnon.SetBounds(x, y, btnSize, btnSize);
        x -= btnSize + gap;
        _btnLock.SetBounds(x, y, btnSize, btnSize);
        // Opacity slider sits left of the icon buttons.
        x -= _sliderOpacity.Width + gap;
        _sliderOpacity.SetBounds(x, (Height - _sliderOpacity.Height) / 2, _sliderOpacity.Width, _sliderOpacity.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // 1px subtle bottom border.
        using var pen = new Pen(AppSettings.Instance.Theme.BorderColor);
        e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }

    /// Standard WinForms trick to make a child control drag its top-level form:
    /// release the local mouse capture and forward an NCLBUTTONDOWN/CAPTION to
    /// the parent so Windows runs its built-in window-move loop.
    private void BeginParentDrag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var top = FindForm();
        if (top is null) return;
        ReleaseCapture();
        SendMessage(top.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    private const int EdgeMargin = 10;

    /// Header sits at the top of the form, so it owns the top edge + the top
    /// 6px of the left/right edges. Anywhere else on the panel acts as drag.
    private int EdgeHitOnHeader(System.Drawing.Point pt)
    {
        int w = Width, h = Height;
        bool L = pt.X < EdgeMargin;
        bool R = pt.X >= w - EdgeMargin;
        bool T = pt.Y < EdgeMargin;
        if (T && L) return 13; // HTTOPLEFT
        if (T && R) return 14; // HTTOPRIGHT
        if (T)      return 12; // HTTOP
        if (L)      return 10; // HTLEFT
        if (R)      return 11; // HTRIGHT
        return 0;
    }

    private void OnHeaderMouseMove(MouseEventArgs e)
    {
        int code = EdgeHitOnHeader(e.Location);
        Cursor = code switch
        {
            10 or 11 => Cursors.SizeWE,
            12       => Cursors.SizeNS,
            13       => Cursors.SizeNWSE,
            14       => Cursors.SizeNESW,
            _        => Cursors.Default,
        };
    }

    private void OnHeaderMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int code = EdgeHitOnHeader(e.Location);
        if (code != 0)
        {
            var top = FindForm();
            if (top is null) return;
            ReleaseCapture();
            SendMessage(top.Handle, WM_NCLBUTTONDOWN, (IntPtr)code, IntPtr.Zero);
            return;
        }
        BeginParentDrag(e);
    }

    // ─── Owner-drawn icon button ─────────────────────────────────────

    public enum IconKind { Lock, Unlock, Eye, EyeOff, History, Settings, Close, CpOn, CpOff, ScoreOn, ScoreOff }

    private sealed class IconButton : Control
    {
        public IconKind Kind { get; set; }
        public Color HoverColor { get; set; } = Color.FromArgb(60, 100, 160);

        private bool _hover;
        private bool _pressed;

        public IconButton(IconKind kind)
        {
            Kind = kind;
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true;  base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true;  base.OnMouseDown(e); Invalidate(); }
        protected override void OnMouseUp  (MouseEventArgs e) { _pressed = false; base.OnMouseUp(e);   Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Hover / press background.
            if (_hover)
            {
                int alpha = _pressed ? 110 : 70;
                using var bg = new SolidBrush(Color.FromArgb(alpha, HoverColor));
                using var path = RoundRect(0, 0, Width, Height, 4);
                g.FillPath(bg, path);
            }

            // Foreground glyph.
            var fg = _hover ? Color.FromArgb(235, 240, 250) : AppSettings.Instance.Theme.TextColor;
            using var pen = new Pen(fg, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            int cx = Width / 2, cy = Height / 2;

            switch (Kind)
            {
                case IconKind.Lock:     DrawLock(g, pen, cx, cy, closed: true);  break;
                case IconKind.Unlock:   DrawLock(g, pen, cx, cy, closed: false); break;
                case IconKind.Eye:      DrawEye(g, pen, cx, cy, off: false); break;
                case IconKind.EyeOff:   DrawEye(g, pen, cx, cy, off: true);  break;
                case IconKind.History:  DrawHistory(g, pen, cx, cy); break;
                case IconKind.Settings: DrawGear(g, pen, cx, cy);   break;
                case IconKind.Close:    DrawCross(g, pen, cx, cy);  break;
                case IconKind.CpOn:     DrawCpLabel(g, cx, cy, on: true);  break;
                case IconKind.CpOff:    DrawCpLabel(g, cx, cy, on: false); break;
                case IconKind.ScoreOn:  DrawScoreLabel(g, cx, cy, on: true);  break;
                case IconKind.ScoreOff: DrawScoreLabel(g, cx, cy, on: false); break;
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

        private static void DrawLock(Graphics g, Pen pen, int cx, int cy, bool closed)
        {
            var body = new Rectangle(cx - 5, cy - 1, 10, 8);
            g.DrawRectangle(pen, body);
            // Shackle: circle on top, rotated open if unlocked.
            if (closed)
                g.DrawArc(pen, cx - 4, cy - 8, 8, 8, 180, 180);
            else
                g.DrawArc(pen, cx - 6, cy - 8, 8, 8, 180, 180);
        }


        private static void DrawEye(Graphics g, Pen pen, int cx, int cy, bool off)
        {
            // Eye shape: two arcs forming an almond shape.
            g.DrawArc(pen, cx - 7, cy - 3, 14, 10, 200, 140);
            g.DrawArc(pen, cx - 7, cy - 7, 14, 10, 20, 140);
            // Pupil.
            g.DrawEllipse(pen, cx - 2, cy - 2, 4, 4);
            // Slash for "off" state.
            if (off) g.DrawLine(pen, cx - 6, cy + 5, cx + 6, cy - 5);
        }

        private static void DrawHistory(Graphics g, Pen pen, int cx, int cy)
        {
            // Clock icon: circle + two hands.
            g.DrawEllipse(pen, cx - 6, cy - 6, 12, 12);
            g.DrawLine(pen, cx, cy, cx, cy - 4);
            g.DrawLine(pen, cx, cy, cx + 3, cy + 1);
        }

        private static void DrawGear(Graphics g, Pen pen, int cx, int cy)
        {
            // Simple gear: outer circle with notches + inner circle.
            g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
            // 6 spokes radiating outward.
            for (int i = 0; i < 6; i++)
            {
                double angle = i * Math.PI / 3;
                int x1 = cx + (int)(4 * Math.Cos(angle));
                int y1 = cy + (int)(4 * Math.Sin(angle));
                int x2 = cx + (int)(6 * Math.Cos(angle));
                int y2 = cy + (int)(6 * Math.Sin(angle));
                g.DrawLine(pen, x1, y1, x2, y2);
            }
        }

        private static void DrawCross(Graphics g, Pen pen, int cx, int cy)
        {
            g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
            g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
        }

        private static void DrawCpLabel(Graphics g, int cx, int cy, bool on)
        {
            var color = on ? Color.FromArgb(100, 180, 255) : Color.FromArgb(90, 90, 100);
            using var font = new Font("Malgun Gothic", 6.5f, FontStyle.Bold);
            using var brush = new SolidBrush(color);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("전투력", font, brush, cx, cy, sf);
        }

        private static void DrawScoreLabel(Graphics g, int cx, int cy, bool on)
        {
            var color = on ? Color.FromArgb(232, 200, 77) : Color.FromArgb(90, 90, 100);
            using var font = new Font("Malgun Gothic", 6.5f, FontStyle.Bold);
            using var brush = new SolidBrush(color);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("아툴", font, brush, cx, cy, sf);
        }
    }

    // ─── Custom slim slider ─────────────────────────────────────────

    private sealed class SlimSlider : Control
    {
        public event Action<int>? ValueChanged;

        private int _min, _max, _value;
        private bool _dragging;
        private bool _hover;

        private const int TrackHeight = 4;
        private const int ThumbRadius = 6;

        public SlimSlider(int min, int max, int value)
        {
            _min = min; _max = max; _value = Math.Clamp(value, min, max);
            DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        public int Value
        {
            get => _value;
            set { _value = Math.Clamp(value, _min, _max); Invalidate(); }
        }

        private int TrackLeft => ThumbRadius;
        private int TrackRight => Width - ThumbRadius;
        private float Ratio => (_value - _min) / (float)Math.Max(1, _max - _min);

        private int ThumbX => TrackLeft + (int)(Ratio * (TrackRight - TrackLeft));

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                Capture = true;
                UpdateValue(e.X);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_dragging) UpdateValue(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            Capture = false;
            base.OnMouseUp(e);
        }

        private void UpdateValue(int x)
        {
            float ratio = (x - TrackLeft) / (float)Math.Max(1, TrackRight - TrackLeft);
            int newVal = _min + (int)Math.Round(ratio * (_max - _min));
            newVal = Math.Clamp(newVal, _min, _max);
            if (newVal != _value)
            {
                _value = newVal;
                Invalidate();
                ValueChanged?.Invoke(_value);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int cy = Height / 2;
            int tl = TrackLeft, tr = TrackRight;
            int tx = ThumbX;

            // Track background (dark groove).
            using (var trackBrush = new SolidBrush(Color.FromArgb(30, 40, 60)))
            {
                var trackRect = new RectangleF(tl, cy - TrackHeight / 2f, tr - tl, TrackHeight);
                using var trackPath = RoundRectF(trackRect, TrackHeight / 2f);
                g.FillPath(trackBrush, trackPath);
            }

            // Filled portion (accent).
            if (tx > tl)
            {
                var accent = AppSettings.Instance.Theme.AccentColor;
                var accentColor = _hover || _dragging ? ControlPaint.Light(accent, 0.3f) : accent;
                using var fillBrush = new SolidBrush(accentColor);
                var fillRect = new RectangleF(tl, cy - TrackHeight / 2f, tx - tl, TrackHeight);
                using var fillPath = RoundRectF(fillRect, TrackHeight / 2f);
                g.FillPath(fillBrush, fillPath);
            }

            // Thumb circle.
            var thumbColor = _dragging ? Color.FromArgb(220, 235, 255)
                           : _hover   ? Color.FromArgb(200, 220, 250)
                                      : AppSettings.Instance.Theme.TextColor;
            using (var thumbBrush = new SolidBrush(thumbColor))
            {
                int r = _dragging ? ThumbRadius + 1 : ThumbRadius;
                g.FillEllipse(thumbBrush, tx - r, cy - r, r * 2, r * 2);
            }

            // Subtle shadow ring on thumb.
            using (var ringPen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                g.DrawEllipse(ringPen, tx - ThumbRadius, cy - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
        }

        private static GraphicsPath RoundRectF(RectangleF rect, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            if (rect.Width < d) { p.AddEllipse(rect); return p; }
            p.AddArc(rect.X, rect.Y, d, d, 180, 90);
            p.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            p.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
