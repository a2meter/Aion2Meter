using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;

namespace A2Meter.Forms;

/// Popup showing per-hit damage log for a single skill.
internal sealed class SkillHitDetailForm : Form
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    private readonly VirtualListPanel _list;
    private readonly IReadOnlyList<long> _hits;
    private readonly long _maxHit;
    private readonly Color _accent;

    public SkillHitDetailForm(DpsCanvas.SkillBar skill, Color accent)
    {
        var hits = skill.HitLog ?? Array.Empty<long>();
        _hits = hits;
        _accent = accent;

        long max = 0;
        long min = long.MaxValue;
        long sum = 0;
        foreach (var d in hits)
        {
            if (d > max) max = d;
            if (d < min) min = d;
            sum += d;
        }
        if (hits.Count == 0) min = 0;
        _maxHit = max > 0 ? max : 1;

        var _s = AppSettings.Instance;
        var _t = _s.Theme;
        string _fn = _s.FontName;
        float _fs = _s.FontSize;

        Text = $"{skill.Name} - 피해 히스토리";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Size = new Size(340, 400);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = _t.BgColor;
        ForeColor = _t.TextColor;
        Font = new Font(_fn, _fs);
        DoubleBuffered = true;

        // ── Title bar ──
        int lineH = TextRenderer.MeasureText("Ag가", new Font(_fn, _fs)).Height;
        int hdrH = (int)(lineH * 2.25);
        var titleBar = new Panel { Dock = DockStyle.Top, Height = hdrH, BackColor = _t.HeaderColor };
        var lblTitle = new Label
        {
            Text = skill.Name,
            Font = new Font(_fn, _fs + 0.5f, FontStyle.Bold),
            ForeColor = _t.TextColor, AutoSize = true,
            Location = new Point(10, (hdrH - lineH) / 2), BackColor = Color.Transparent,
        };
        var btnClose = new Label
        {
            Text = "✕", Font = new Font(_fn, _fs),
            ForeColor = _t.TextDimColor, AutoSize = true,
            Cursor = Cursors.Hand, BackColor = Color.Transparent,
        };
        titleBar.Controls.Add(lblTitle);
        titleBar.Controls.Add(btnClose);
        titleBar.Resize += (_, _) => btnClose.Location = new Point(titleBar.Width - btnClose.Width - 10, (hdrH - lineH) / 2);
        btnClose.Click += (_, _) => Close();
        titleBar.MouseDown += (_, e) => Drag(e);
        lblTitle.MouseDown += (_, e) => Drag(e);

        // ── Summary badges ──
        int badgeH = (int)(lineH * 2.0);
        int badgePad = Math.Max(2, (badgeH - lineH) / 3);
        var summary = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = badgeH, BackColor = _t.BgColor,
            Padding = new Padding(8, badgePad, 8, badgePad), WrapContents = false,
        };
        long avg = hits.Count > 0 ? sum / hits.Count : 0;
        summary.Controls.Add(Badge($"타수 {hits.Count}"));
        summary.Controls.Add(Badge($"평균 {avg:N0}"));
        summary.Controls.Add(Badge($"최대 {max:N0}"));
        summary.Controls.Add(Badge($"최소 {min:N0}"));

        // ── Hit list ──
        _list = new VirtualListPanel
        {
            Dock = DockStyle.Fill,
            Font = new Font(_fn, _fs),
            RowHeight = (int)(lineH * 1.5),
            Columns = new[]
            {
                ("#",    30f, HorizontalAlignment.Right),
                ("피해량", 100f, HorizontalAlignment.Right),
            },
        };
        _list.RowCount = hits.Count;
        _list.PaintRow += OnPaintRow;

        Controls.Add(_list);
        Controls.Add(summary);
        Controls.Add(titleBar);
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

    private void OnPaintRow(Graphics g, Rectangle rowRect, Rectangle[] cells, int idx, bool sel)
    {
        if (idx < 0 || idx >= _hits.Count || cells.Length < 2) return;
        var theme = AppSettings.Instance.Theme;
        var fg = sel ? Color.White : theme.TextColor;
        long dmg = _hits[idx];

        // Damage bar in col 1 background.
        double rel = (double)dmg / _maxHit;
        if (rel > 0)
        {
            int barW = (int)(cells[1].Width * rel);
            if (barW > 0)
            {
                var barRect = new Rectangle(cells[1].X, cells[1].Y + 2, barW, cells[1].Height - 4);
                using var brush = new SolidBrush(Color.FromArgb(70, _accent));
                g.FillRectangle(brush, barRect);
            }
        }

        // Col 0: index
        var r0 = new Rectangle(cells[0].X + 3, cells[0].Y, cells[0].Width - 6, cells[0].Height);
        TextRenderer.DrawText(g, $"{idx + 1}", _list.Font, r0, theme.TextDimColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        // Col 1: damage
        var r1 = new Rectangle(cells[1].X + 3, cells[1].Y, cells[1].Width - 6, cells[1].Height);
        TextRenderer.DrawText(g, $"{dmg:N0}", _list.Font, r1, fg,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    private static Label Badge(string text) => new()
    {
        Text = text,
        Font = new Font(AppSettings.Instance.FontName, AppSettings.Instance.FontSize - 0.5f),
        ForeColor = AppSettings.Instance.Theme.TextColor,
        BackColor = Color.FromArgb(20, 35, 50),
        AutoSize = true,
        Padding = new Padding(4, 2, 4, 2),
        Margin = new Padding(2, 0, 2, 0),
    };

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

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }
}
