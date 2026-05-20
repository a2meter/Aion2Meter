using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Dps;

namespace A2Meter.Forms;

internal sealed class CombatHistoryForm : Form
{
    private const int ResizeMargin = 14;

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private readonly VirtualListPanel _list;
    private readonly Panel _headerPanel;
    private CombatHistory? _history;
    private Action<CombatRecord>? _onRecordSelected;

    public CombatHistoryForm()
    {
        Text = "전투 기록";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        MinimumSize = new Size(500, 300);
        var _s = AppSettings.Instance;
        var _t = _s.Theme;
        BackColor = _t.BgColor;
        Padding = new Padding(3);
        DoubleBuffered = true;
        Size = new Size(
            Math.Max(MinimumSize.Width, _s.CombatRecordsPanelWidth),
            Math.Max(MinimumSize.Height, _s.CombatRecordsPanelHeight));
        if (_s.CombatRecordsPanelX >= 0 && _s.CombatRecordsPanelY >= 0)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(_s.CombatRecordsPanelX, _s.CombatRecordsPanelY);
        }
        else StartPosition = FormStartPosition.CenterScreen;

        int lineH = TextRenderer.MeasureText("Ag가", new Font(_s.FontName, _s.FontSize)).Height;
        int hdrH = (int)(lineH * 2.25);
        _headerPanel = new Panel { Dock = DockStyle.Top, Height = hdrH, BackColor = _t.HeaderColor };
        _headerPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(_t.BorderColor);
            e.Graphics.DrawLine(pen, 0, _headerPanel.Height - 1, _headerPanel.Width, _headerPanel.Height - 1);
        };

        var lblTitle = new Label
        {
            Text = "전투 기록",
            ForeColor = _t.TextColor,
            Font = new Font(_s.FontName, _s.FontSize + 0.5f, FontStyle.Bold),
            AutoSize = true, Location = new Point(10, (hdrH - lineH) / 2), BackColor = Color.Transparent,
        };
        _headerPanel.Controls.Add(lblTitle);

        var btnClose = new HeaderCloseButton();
        _headerPanel.Controls.Add(btnClose);
        _headerPanel.Resize += (_, _) => btnClose.Location = new Point(_headerPanel.Width - btnClose.Width - 8, (_headerPanel.Height - btnClose.Height) / 2);
        btnClose.Click += (_, _) => Close();
        _headerPanel.MouseDown += (_, e) => Drag(e);
        lblTitle.MouseDown += (_, e) => Drag(e);

        _list = new VirtualListPanel
        {
            Dock = DockStyle.Fill,
            Font = new Font(_s.FontName, _s.FontSize),
            RowHeight = (int)(lineH * 1.6),
            Columns = new[]
            {
                ("시간", 130f, HorizontalAlignment.Left),
                ("보스", 140f, HorizontalAlignment.Left),
                ("시간(초)", 70f, HorizontalAlignment.Right),
                ("총 대미지", 110f, HorizontalAlignment.Right),
                ("평균 DPS", 100f, HorizontalAlignment.Right),
                ("최대 DPS", 100f, HorizontalAlignment.Right),
            },
        };
        _list.PaintRow += OnPaintRow;
        _list.RowDoubleClicked += idx =>
        {
            if (_history != null && idx >= 0 && idx < _history.Records.Count)
                _onRecordSelected?.Invoke(_history.Records[idx]);
        };

        Controls.Add(_list);
        Controls.Add(_headerPanel);
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

    public void SetData(CombatHistory history, Action<CombatRecord> onSelect)
    {
        _history = history;
        _onRecordSelected = onSelect;
        _list.RowCount = history.Records.Count;
    }

    private void OnPaintRow(Graphics g, Rectangle rowRect, Rectangle[] cells, int idx, bool sel)
    {
        if (_history == null || idx < 0 || idx >= _history.Records.Count) return;
        var rec = _history.Records[idx];
        var fg = sel ? Color.White : AppSettings.Instance.Theme.TextColor;
        string[] texts =
        {
            rec.Timestamp.ToString("MM/dd HH:mm:ss"),
            rec.BossName ?? "-",
            $"{rec.DurationSec:0.0}",
            $"{rec.TotalDamage:N0}",
            $"{rec.AverageDps:N0}",
            $"{rec.PeakDps:N0}",
        };
        for (int i = 0; i < cells.Length && i < texts.Length; i++)
        {
            var cr = cells[i];
            var align = _list.Columns[i].Align;
            var flags = VirtualListPanel_AlignFlag(align) | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            var tr = new Rectangle(cr.X + 4, cr.Y, cr.Width - 8, cr.Height);
            TextRenderer.DrawText(g, texts[i], _list.Font, tr, fg, flags);
        }
    }

    private static TextFormatFlags VirtualListPanel_AlignFlag(HorizontalAlignment a) => a switch
    {
        HorizontalAlignment.Right => TextFormatFlags.Right,
        HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
        _ => TextFormatFlags.Left,
    };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        var s = AppSettings.Instance;
        s.CombatRecordsPanelWidth = Width;
        s.CombatRecordsPanelHeight = Height;
        s.SaveDebounced();
        base.OnFormClosing(e);
    }

    private void Drag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, (IntPtr)2, IntPtr.Zero);
    }

    private void PersistBounds()
    {
        if (WindowState != FormWindowState.Normal) return;
        var s = AppSettings.Instance;
        s.CombatRecordsPanelX = Location.X;
        s.CombatRecordsPanelY = Location.Y;
        s.CombatRecordsPanelWidth = Size.Width;
        s.CombatRecordsPanelHeight = Size.Height;
        s.SaveDebounced();
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
            if (pt.Y < 36 && pt.X < Width - 40) { m.Result = (IntPtr)Win32Native.HTCAPTION; return; }
        }
        base.WndProc(ref m);
    }

    private int HitTestEdges(Point pt)
    {
        int w = ClientSize.Width, h = ClientSize.Height;
        bool L = pt.X < ResizeMargin, R = pt.X >= w - ResizeMargin;
        bool T = pt.Y < ResizeMargin, B = pt.Y >= h - ResizeMargin;
        if (T && L) return Win32Native.HTTOPLEFT;
        if (T && R) return Win32Native.HTTOPRIGHT;
        if (B && L) return Win32Native.HTBOTTOMLEFT;
        if (B && R) return Win32Native.HTBOTTOMRIGHT;
        if (L) return Win32Native.HTLEFT;
        if (R) return Win32Native.HTRIGHT;
        if (T) return Win32Native.HTTOP;
        if (B) return Win32Native.HTBOTTOM;
        return Win32Native.HTCLIENT;
    }

    private sealed class HeaderCloseButton : Control
    {
        private bool _hover, _pressed;
        public HeaderCloseButton()
        {
            Size = new Size(26, 26); DoubleBuffered = true;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent; Cursor = Cursors.Hand;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); }
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
}
