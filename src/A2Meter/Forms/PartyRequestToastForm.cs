using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows.Forms;
using A2Meter.Api;
using A2Meter.Core;
using A2Meter.Dps;

namespace A2Meter.Forms;

/// Floating toast shown at the bottom of the overlay when a 07 97 party
/// request packet is observed.
internal sealed class PartyRequestToastForm : Form
{
    private const int ToastWidth = 420;
    private const int ToastHeight = 176;
    private const int ToastBottomMargin = 6;
    private const int ToastGap = 8;

    private static readonly object ActiveToastsLock = new();
    private static readonly Dictionary<Form, List<PartyRequestToastForm>> ActiveToasts = new();
    private static readonly HttpClient ImageHttp = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly Form _parent;
    private readonly PartyMember _member;

    private string _dungeonText = "";
    private string _dpStatusText = "DP 스킬 조회 중...";
    private List<CharacterDpSkill> _dpSkills = new();
    private readonly Dictionary<string, Image> _dpIcons = new(StringComparer.Ordinal);
    private System.Windows.Forms.Timer? _autoCloseTimer;

    public PartyRequestToastForm(Form parent, PartyMember member, int? currentDungeonId = null)
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

        var btnClose = new ToastCloseButton { Location = new Point(ToastWidth - 24, 4) };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        _autoCloseTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _autoCloseTimer.Tick += (_, _) => Close();
        _autoCloseTimer.Start();

        Paint += OnPaint;
        _dungeonText = BuildDungeonHint(currentDungeonId);

        RegisterToast();
        parent.Move += OnParentMoved;
        parent.Resize += OnParentMoved;

        FormClosed += (_, _) =>
        {
            parent.Move -= OnParentMoved;
            parent.Resize -= OnParentMoved;
            UnregisterToast();
            _autoCloseTimer?.Stop();
            _autoCloseTimer?.Dispose();
            foreach (var icon in _dpIcons.Values)
                icon.Dispose();
            _dpIcons.Clear();
        };

        _ = LoadDpSkillsAsync();
    }

    private void OnParentMoved(object? sender, EventArgs e) => RepositionActiveToasts(_parent);

    private void RegisterToast()
    {
        lock (ActiveToastsLock)
        {
            if (!ActiveToasts.TryGetValue(_parent, out var toasts))
            {
                toasts = new List<PartyRequestToastForm>();
                ActiveToasts[_parent] = toasts;
            }

            toasts.RemoveAll(t => t.IsDisposed);
            toasts.Add(this);
        }

        RepositionActiveToasts(_parent);
    }

    private void UnregisterToast()
    {
        lock (ActiveToastsLock)
        {
            if (ActiveToasts.TryGetValue(_parent, out var toasts))
            {
                toasts.Remove(this);
                toasts.RemoveAll(t => t.IsDisposed);
                if (toasts.Count == 0)
                    ActiveToasts.Remove(_parent);
            }
        }

        RepositionActiveToasts(_parent);
    }

    private static void RepositionActiveToasts(Form parent)
    {
        if (parent.IsDisposed) return;

        List<PartyRequestToastForm> toasts;
        lock (ActiveToastsLock)
        {
            if (!ActiveToasts.TryGetValue(parent, out var active)) return;
            active.RemoveAll(t => t.IsDisposed);
            toasts = active.ToList();
        }

        for (int i = 0; i < toasts.Count; i++)
            toasts[i].PlaceAtBottom(i);
    }

    private void PlaceAtBottom(int stackOrder)
    {
        if (_parent.IsDisposed || IsDisposed) return;
        int x = _parent.Left + (_parent.Width - Width) / 2;
        int y = _parent.Bottom - Height - ToastBottomMargin + stackOrder * (Height + ToastGap);
        Location = new Point(x, y);
    }

    private async System.Threading.Tasks.Task LoadDpSkillsAsync()
    {
        try
        {
            var data = await PlayncClient.FetchCharacterData(_member.Nickname, _member.ServerId).ConfigureAwait(false);
            var skills = (data?.DpSkills ?? new List<CharacterDpSkill>())
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .Take(4)
                .ToList();
            if (data != null)
                SkillLevelCache.Instance.Store(_member.Nickname, _member.ServerId, data);

            var icons = new Dictionary<string, Image>(StringComparer.Ordinal);
            foreach (var skill in skills)
            {
                if (string.IsNullOrWhiteSpace(skill.Icon) || icons.ContainsKey(skill.Icon)) continue;
                var image = await LoadImageAsync(skill.Icon).ConfigureAwait(false);
                if (image != null)
                    icons[skill.Icon] = image;
            }

            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                foreach (var icon in _dpIcons.Values)
                    icon.Dispose();
                _dpIcons.Clear();
                foreach (var (key, icon) in icons)
                    _dpIcons[key] = icon;
                _dpSkills = skills;
                _dpStatusText = skills.Count == 0 ? "장착 DP 없음" : "";
                Invalidate();
            });
        }
        catch
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                _dpSkills = new List<CharacterDpSkill>();
                _dpStatusText = "DP 조회 실패";
                Invalidate();
            });
        }
    }

    private static async System.Threading.Tasks.Task<Image?> LoadImageAsync(string url)
    {
        try
        {
            using var resp = await ImageHttp.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms).ConfigureAwait(false);
            ms.Position = 0;
            using var raw = Image.FromStream(ms);
            return new Bitmap(raw);
        }
        catch
        {
            return null;
        }
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

        using (var accent = new SolidBrush(theme.AccentColor))
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

        DrawDpSkills(g, fn, fs, theme, 94);
    }

    private void DrawDpSkills(Graphics g, string fontName, float fontSize, AppSettings.ThemeColors theme, float y)
    {
        using var titleFont = new Font(fontName, Math.Max(8f, fontSize - 2f), FontStyle.Bold);
        using var titleBrush = new SolidBrush(theme.TextDimColor);
        g.DrawString("장착 DP", titleFont, titleBrush, 12, y);

        if (_dpSkills.Count == 0)
        {
            using var statusFont = new Font(fontName, Math.Max(8f, fontSize - 1.5f));
            using var statusBrush = new SolidBrush(theme.TextDimColor);
            g.DrawString(_dpStatusText, statusFont, statusBrush, 68, y);
            return;
        }

        using var nameFont = new Font(fontName, Math.Max(7.5f, fontSize - 2.2f), FontStyle.Bold);
        using var lvFont = new Font(fontName, Math.Max(7.2f, fontSize - 2.5f));
        using var nameBrush = new SolidBrush(theme.TextColor);
        using var lvBrush = new SolidBrush(theme.TextDimColor);
        using var textFormat = new StringFormat
        {
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        const int iconSize = 28;
        const int cellWidth = 99;
        float x = 12;
        float rowY = y + 17;
        foreach (var skill in _dpSkills)
        {
            if (!string.IsNullOrWhiteSpace(skill.Icon) && _dpIcons.TryGetValue(skill.Icon, out var icon))
                g.DrawImage(icon, new RectangleF(x, rowY, iconSize, iconSize));
            else
            {
                using var border = new Pen(Color.FromArgb(80, 90, 110));
                g.DrawRectangle(border, x, rowY, iconSize, iconSize);
            }

            g.DrawString(skill.Name, nameFont, nameBrush, new RectangleF(x + iconSize + 5, rowY - 1, cellWidth - iconSize - 7, 15), textFormat);
            g.DrawString($"Lv{skill.SkillLevel}", lvFont, lvBrush, new RectangleF(x + iconSize + 5, rowY + 14, cellWidth - iconSize - 7, 14), textFormat);
            x += cellWidth;
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

    private static string BuildDungeonHint(int? dungeonId)
    {
        if (dungeonId is not int dgId) return "";
        try
        {
            var snapshot = GameDataClient.Snapshot;
            var info = snapshot.Dungeons.FirstOrDefault(d => d.Id == dgId);
            var bosses = snapshot.DungeonBosses
                .Where(b => b.DungeonId == dgId)
                .OrderBy(b => b.Ord)
                .ToList();
            if (info == null && bosses.Count == 0) return $"현재 던전 #{dgId}";

            var dungeonName = info == null
                ? $"#{dgId}"
                : $"{info.BaseName} {info.Tier}".Trim();
            if (bosses.Count == 0) return $"현재 던전: {dungeonName}";

            var bossText = string.Join(" / ", bosses.Select(b => $"{b.Ord}N {b.BossName}"));
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
