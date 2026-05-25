using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using A2Meter.Api;
using A2Meter.Core;
using A2Meter.Data;
using A2Meter.Dps;

namespace A2Meter.Forms;

/// Floating toast shown at the bottom of the overlay when a 07 97 party
/// request packet is observed.
internal sealed class PartyRequestToastForm : Form
{
    private const int ToastWidth = 340;
    private const int ToastHeight = 112;

    private readonly Form _parent;
    private readonly PartyMember _member;
    private readonly int? _currentDungeonId;

    private string _tierText = "티어 조회 중...";
    private string _tier = "";
    private Image? _tierIcon;
    private Color  _tierColor = Color.FromArgb(140, 150, 170);
    private string _dungeonText = "";
    private System.Windows.Forms.Timer? _autoCloseTimer;

    public PartyRequestToastForm(Form parent, PartyMember member, int? currentDungeonId = null)
    {
        _parent = parent;
        _member = member;
        _currentDungeonId = currentDungeonId;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        Owner = parent;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(ToastWidth, ToastHeight);
        BackColor = AppSettings.Instance.Theme.HeaderColor;
        Opacity = 0.97;
        DoubleBuffered = true;

        var btnClose = new ToastCloseButton { Location = new Point(ToastWidth - 24, 4) };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        _autoCloseTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();

        Paint += OnPaint;
        _dungeonText = BuildDungeonHint(currentDungeonId);

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
                    SetTierIcon("");
                    _tierColor = Color.FromArgb(120, 130, 150);
                    Invalidate();
                });
                return;
            }

            PlayerTierClient.DungeonTier pick = resp.Dungeons[0];
            string prefix = "최고";
            if (_currentDungeonId is int dgId)
            {
                var match = resp.Dungeons.Find(d => d.DungeonId == dgId);
                if (match != null) { pick = match; prefix = "현재 던전"; }
            }

            var chosen = pick;
            var label = prefix;
            BeginInvoke(() =>
            {
                string tier = chosen.Tier;
                _tierText = $"{label} · 표본 {chosen.SampleCount}";
                SetTierIcon(tier);
                _tierColor = TierColor(tier);
                Invalidate();
            });
        }
        catch
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                _tierText = "조회 실패";
                SetTierIcon("");
                _tierColor = Color.FromArgb(180, 90, 90);
                Invalidate();
            });
        }
    }

    private void SetTierIcon(string tier)
    {
        _tier = tier;
        _tierIcon = TierIconCache.Get(tier);
    }

    private static Color TierColor(string tier) => tier switch
    {
        "Challenger1" => Color.FromArgb(255, 120, 80),
        "Challenger2" => Color.FromArgb(255, 120, 80),
        "Challenger3" => Color.FromArgb(255, 120, 80),
        "Challenger"  => Color.FromArgb(255, 120, 80),
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

        using (var pen = new Pen(theme.BorderColor))
        using (var path = RoundRect(0, 0, Width - 1, Height - 1, 6))
            g.DrawPath(pen, path);

        using (var accent = new SolidBrush(_tierColor))
            g.FillRectangle(accent, 0, 10, 4, Height - 20);

        using (var headerFont = new Font(fn, fs - 1f, FontStyle.Bold))
        using (var headerBrush = new SolidBrush(theme.TextDimColor))
            g.DrawString("파티 요청", headerFont, headerBrush, 12, 8);

        using (var nameFont = new Font(fn, fs + 1.5f, FontStyle.Bold))
        using (var nameBrush = new SolidBrush(theme.TextColor))
            g.DrawString(_member.Nickname, nameFont, nameBrush, 12, 24);

        string sub = $"{_member.JobName} · Lv{_member.Level} · CP {_member.CombatPower:N0}";
        if (!string.IsNullOrEmpty(_member.ServerName))
            sub += $" · {_member.ServerName}";
        using (var subFont = new Font(fn, fs - 1f))
        using (var subBrush = new SolidBrush(theme.TextDimColor))
            g.DrawString(sub, subFont, subBrush, 12, 50);

        if (!string.IsNullOrEmpty(_dungeonText))
        {
            using var dgFont = new Font(fn, Math.Max(8f, fs - 1.5f));
            using var dgBrush = new SolidBrush(theme.TextDimColor);
            using var dgFormat = new StringFormat
            {
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap,
            };
            g.DrawString(_dungeonText, dgFont, dgBrush, new RectangleF(12, 68, Width - 24, 18), dgFormat);
        }

        using (var infoFont = new Font(fn, Math.Max(8f, fs - 2f)))
        using (var infoBrush = new SolidBrush(theme.TextDimColor))
        using (var infoFormat = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        })
            g.DrawString(_tierText, infoFont, infoBrush, new RectangleF(12, Height - 29, 88, 20), infoFormat);

        using var tierFont = new Font(fn, Math.Max(8f, fs - 1.5f), FontStyle.Bold);
        DrawTierBadge(g, tierFont, "종합", _tier, _tierIcon, 106, Height - 33);
    }

    private static void DrawTierBadge(Graphics g, Font font, string label, string tier, Image? icon, float x, float y)
    {
        using var labelBrush = new SolidBrush(Color.FromArgb(170, 180, 200));
        g.DrawString(label, font, labelBrush, x, y + 5);

        float iconX = x + 36;
        if (icon != null)
            g.DrawImage(icon, new RectangleF(iconX, y, 26, 26));
        else
        {
            using var border = new Pen(Color.FromArgb(90, 100, 120));
            g.DrawRectangle(border, iconX, y, 26, 26);
        }

        using var textBrush = new SolidBrush(TierColor(tier));
        g.DrawString(TierShortKo(tier), font, textBrush, iconX + 29, y + 5);
    }

    private static string TierShortKo(string tier) => tier switch
    {
        "Challenger1" => "챌린저 1위",
        "Challenger2" => "챌린저 2위",
        "Challenger3" => "챌린저 3위",
        "Challenger"  => "챌린저",
        "Grandmaster" => "그마",
        "Master"      => "마스터",
        "Diamond"     => "다이아",
        "Platinum"    => "플래티넘",
        "Gold"        => "골드",
        "Silver"      => "실버",
        "Bronze"      => "브론즈",
        "Iron"        => "아이언",
        "Unranked"    => "언랭크",
        _             => "언랭크",
    };

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

    private static string BuildDungeonHint(int? dungeonId)
    {
        if (dungeonId is not int dgId) return "";
        try
        {
            var db = GameDatabase.Instance;
            var info = db.GetDungeonInfo(dgId);
            var bosses = db.GetDungeonBosses(dgId);
            if (info == null && bosses.Count == 0) return $"현재 던전 #{dgId}";

            var dungeonName = info == null
                ? $"#{dgId}"
                : $"{info.BaseName} {info.Tier}".Trim();
            if (bosses.Count == 0) return $"현재 던전: {dungeonName}";

            var bossText = string.Join(" / ", bosses.OrderBy(b => b.Order).Select(b => $"{b.Order}N {b.Name}"));
            return $"현재 던전: {dungeonName} · {bossText}";
        }
        catch
        {
            return $"현재 던전 #{dgId}";
        }
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
