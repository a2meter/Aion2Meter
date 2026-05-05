using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Dps;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DwFontStyle = Vortice.DirectWrite.FontStyle;
using D2DColor    = Vortice.Mathematics.Color4;
using DrawColor   = System.Drawing.Color;

namespace A2Meter.Direct2D;

/// High-frequency overlay layer: per-player DPS bars, numbers, debuff bars.
/// Renders only when SetData() is called or when the form is resized.
///
/// IMPORTANT: Vortice.Mathematics.Rect is (x, y, width, height), NOT (left, top, right, bottom).
internal sealed class DpsCanvas : Control
{
    public sealed record PlayerRow(
        string         Name,
        string         JobIconKey,
        long           Damage,
        double         Percent,    // 0..1 of party
        long           DpsValue,
        double         CritRate,   // 0..1
        long           HealTotal,
        D2DColor       AccentColor,
        IReadOnlyList<SkillBar>? Skills = null,
        int            CombatPower = 0,
        long           PeakDps = 0,
        long           AvgDps = 0,
        long           DotDamage = 0,
        int            ServerId = 0,
        string         ServerName = "");

    public sealed record SkillBar(
        string Name,
        long   Total,
        long   Hits,
        double CritRate,        // 0..1
        double PercentOfActor,  // 0..1
        double BackRate      = 0,
        double StrongRate    = 0,
        double PerfectRate   = 0,
        double MultiHitRate  = 0,
        double DodgeRate     = 0,
        double BlockRate     = 0,
        long   MaxHit        = 0,
        int[]? Specs         = null);

    public sealed record TargetInfo(string Name, long CurrentHp, long MaxHp);

    public sealed record SessionSummary(
        double  DurationSec,
        long    TotalDamage,
        long    AverageDps,
        long    PeakDps,
        string  TopActorName,
        long    TopActorDamage,
        string? BossName);

    private const float PadX         = 10f;
    private const float PadBottom    = 6f;
    private const float HeaderHeight = 28f;
    private const float TargetBarH   = 20f;
    private const float RowGap       = 3f;
    private const float BarRadius    = 5f;
    private const float IconSize     = 22f;

    private float RowH => 30f * Core.AppSettings.Instance.RowHeight / 90f;
    private D2DContext? _ctx;
    private D2DFontProvider? _fonts;
    private JobIconAtlas? _icons;
    private ID2D1SolidColorBrush? _brushBarBg;
    private ID2D1SolidColorBrush? _brushBarFill;
    private ID2D1SolidColorBrush? _brushBarBorder;
    private ID2D1SolidColorBrush? _brushText;
    private ID2D1SolidColorBrush? _brushTextBright;
    private ID2D1SolidColorBrush? _brushTextDim;
    private ID2D1SolidColorBrush? _brushGold;
    private ID2D1SolidColorBrush? _brushAccent;
    private IDWriteTextFormat? _fontName;
    private IDWriteTextFormat? _fontNumber;
    private IDWriteTextFormat? _fontSmall;
    private IDWriteTextFormat? _fontTotal;

    private readonly List<PlayerRow> _rows = new();
    private readonly List<(float Top, float Bottom, int Index)> _rowHitAreas = new();
    private string _timerText = "0:00";
    private long _totalDamage;
    private TargetInfo? _target;
    private SessionSummary? _summary;
    private ID2D1SolidColorBrush? _brushHpBg;
    private ID2D1SolidColorBrush? _brushHpFill;
    private ID2D1SolidColorBrush? _brushNameAsmo;   // 마족 (1xxx) 하늘색
    private ID2D1SolidColorBrush? _brushNameElyos;  // 천족 (2xxx) 연보라

    /// Fired when the user clicks a player row. Passes the clicked PlayerRow.
    public event Action<PlayerRow>? PlayerRowClicked;

    /// Fired when the user clicks the countdown timer button.
    public event Action? CountdownClicked;

    /// Countdown display: 0 = off, >0 = limit in seconds. Set by pipeline.
    public int CountdownSec { get; set; }
    /// Whether countdown has expired (shows frozen state).
    public bool CountdownExpired { get; set; }

    /// When true, show "직업명[서버명]" instead of nickname.
    public bool AnonymousMode { get; set; }

    /// Compact mode flag (unused in DpsCanvas rendering now — compact uses CompactOverlayForm).
    public bool CompactMode { get; set; }

    public DpsCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint
               | ControlStyles.Opaque
               | ControlStyles.UserPaint, true);
        DoubleBuffered = false;
        BackColor = Core.AppSettings.Instance.Theme.BgColor;

        MouseMove += OnEdgeMouseMove;
        MouseDown += OnEdgeMouseDown;
        MouseClick += OnPlayerRowClick;
    }

    private const int EdgeMargin = 6;

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
    private const int WM_NCLBUTTONDOWN = 0x00A1;

    private static int HitFromEdges(System.Drawing.Point pt, int w, int h)
    {
        bool L = pt.X < EdgeMargin;
        bool R = pt.X >= w - EdgeMargin;
        bool T = pt.Y < EdgeMargin;
        bool B = pt.Y >= h - EdgeMargin;
        if (T && L) return 13;
        if (T && R) return 14;
        if (B && L) return 16;
        if (B && R) return 17;
        if (L)      return 10;
        if (R)      return 11;
        if (T)      return 12;
        if (B)      return 15;
        return 0;
    }

    private void OnEdgeMouseMove(object? sender, MouseEventArgs e)
    {
        int code = HitFromEdges(e.Location, ClientSize.Width, ClientSize.Height);
        Cursor = code switch
        {
            10 or 11 => Cursors.SizeWE,
            12 or 15 => Cursors.SizeNS,
            13 or 17 => Cursors.SizeNWSE,
            14 or 16 => Cursors.SizeNESW,
            _        => Cursors.Default,
        };
    }

    private void OnEdgeMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int code = HitFromEdges(e.Location, ClientSize.Width, ClientSize.Height);
        if (code == 0) return;
        var top = FindForm();
        if (top is null) return;
        ReleaseCapture();
        SendMessage(top.Handle, WM_NCLBUTTONDOWN, (IntPtr)code, IntPtr.Zero);
    }

    public void SetData(IReadOnlyList<PlayerRow> rows, long totalDamage, string timerText,
                        MobTarget? target = null, SessionSummary? summary = null)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        _totalDamage = totalDamage;
        _timerText = timerText;
        _target = target is { IsBoss: true, MaxHp: > 0 }
            ? new TargetInfo(target.Name, target.CurrentHp, target.MaxHp)
            : null;
        _summary = summary;
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        InitD2D();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        DisposeD2D();
        base.OnHandleDestroyed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _ctx?.Resize(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height));
        Invalidate();
    }

    private void InitD2D()
    {
        if (DesignMode || Width <= 0 || Height <= 0) return;
        bool warp = string.Equals(Core.AppSettings.Instance.GpuMode, "off", StringComparison.OrdinalIgnoreCase);
        _ctx = new D2DContext(Handle, ClientSize.Width, ClientSize.Height, forceWarp: warp);

        var dc = _ctx.DC;
        _brushBarBg    = dc.CreateSolidColorBrush(new D2DColor(0.078f, 0.098f, 0.157f, 0.60f)); // #141928 @ 60%
        _brushBarFill  = dc.CreateSolidColorBrush(new D2DColor(0.44f, 0.78f, 1.00f, 1.00f));
        _brushBarBorder= dc.CreateSolidColorBrush(new D2DColor(1f, 1f, 1f, 0.07f));
        _brushText     = dc.CreateSolidColorBrush(new D2DColor(0.93f, 0.95f, 1.00f, 1.00f)); // --text-primary
        _brushTextBright = dc.CreateSolidColorBrush(new D2DColor(1f, 1f, 1f, 1f));            // --text-bright #fff
        _brushTextDim  = dc.CreateSolidColorBrush(new D2DColor(0.635f, 0.694f, 0.784f, 1f));  // --text-secondary #a2b1c8
        _brushGold     = dc.CreateSolidColorBrush(new D2DColor(1f, 0.82f, 0.40f, 1f));        // --gold #ffd166
        _brushAccent   = dc.CreateSolidColorBrush(new D2DColor(0.455f, 0.753f, 0.988f, 1f));  // --accent #74c0fc
        _brushHpBg     = dc.CreateSolidColorBrush(new D2DColor(0.20f, 0.07f, 0.10f, 1.00f));
        _brushHpFill   = dc.CreateSolidColorBrush(new D2DColor(0.85f, 0.20f, 0.25f, 1.00f));
        _brushNameElyos  = dc.CreateSolidColorBrush(new D2DColor(0.55f, 0.82f, 1.00f, 1f));  // 하늘색
        _brushNameAsmo = dc.CreateSolidColorBrush(new D2DColor(0.76f, 0.65f, 1.00f, 1f));  // 연보라

        ApplyThemeBrushes();

        _fonts = new D2DFontProvider(_ctx.DWriteFactory);
        RebuildFonts();

        _icons = new JobIconAtlas(dc);
    }

    private static D2DColor ColorToD2D(System.Drawing.Color c)
        => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);

    private void ApplyThemeBrushes()
    {
        var t = Core.AppSettings.Instance.Theme;
        if (_brushBarBg != null)   _brushBarBg.Color   = ColorToD2D(t.HeaderColor);
        if (_brushText != null)    _brushText.Color    = ColorToD2D(t.TextColor);
        if (_brushTextDim != null)  _brushTextDim.Color = ColorToD2D(t.TextDimColor);
        if (_brushAccent != null)   _brushAccent.Color  = ColorToD2D(t.AccentColor);
        if (_brushNameElyos != null) _brushNameElyos.Color = ColorToD2D(t.ElyosColor);
        if (_brushNameAsmo != null) _brushNameAsmo.Color = ColorToD2D(t.AsmoColor);
    }

    /// Rebuild fonts and theme colors from current AppSettings.
    public void ApplySettings()
    {
        if (_fonts == null) return;
        DisposeFonts();
        RebuildFonts();
        ApplyThemeBrushes();
        // Update the GDI BackColor (visible behind D2D if control isn't fully covered).
        BackColor = Core.AppSettings.Instance.Theme.BgColor;
        Invalidate();
    }

    private void RebuildFonts()
    {
        if (_fonts == null) return;
        var s = Core.AppSettings.Instance;
        float baseSize = s.FontSize * s.FontScale / 100f;
        _fontName   = _fonts.CreateUi(baseSize + 4f);
        _fontNumber = _fonts.CreateUi(baseSize + 4f);
        _fontSmall  = _fonts.CreateUi(baseSize + 1f);
        _fontTotal  = _fonts.CreateUi(baseSize + 6f);
    }

    private void DisposeFonts()
    {
        _fontTotal?.Dispose(); _fontTotal = null;
        _fontSmall?.Dispose(); _fontSmall = null;
        _fontNumber?.Dispose(); _fontNumber = null;
        _fontName?.Dispose(); _fontName = null;
    }

    private void DisposeD2D()
    {
        _icons?.Dispose();
        _fontTotal?.Dispose();
        _fontSmall?.Dispose();
        _fontNumber?.Dispose();
        _fontName?.Dispose();
        _brushNameElyos?.Dispose();
        _brushNameAsmo?.Dispose();
        _brushHpFill?.Dispose();
        _brushHpBg?.Dispose();
        _brushAccent?.Dispose();
        _brushNameElyos?.Dispose();
        _brushNameAsmo?.Dispose();
        _brushGold?.Dispose();
        _brushTextDim?.Dispose();
        _brushTextBright?.Dispose();
        _brushText?.Dispose();
        _brushBarBorder?.Dispose();
        _brushBarFill?.Dispose();
        _brushBarBg?.Dispose();
        _ctx?.Dispose();
        _ctx = null;
    }

    // ── Rendering ────────────────────────────────────────────────────

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_ctx is null) { base.OnPaint(e); return; }
        var dc = _ctx.DC;

        dc.BeginDraw();
        dc.Clear(ColorToD2D(Core.AppSettings.Instance.Theme.BgColor));

        float y = 6f;
        if (!CompactMode)
            y = DrawHeader(dc, y);
        y = DrawTargetBar(dc, y);
        DrawRows(dc, y);

        dc.EndDraw();
        _ctx.Present();
    }

    private float DrawHeader(ID2D1DeviceContext dc, float y)
    {
        float w = ClientSize.Width - PadX * 2;

        // Rect = (x, y, width, height)
        dc.DrawText(_timerText, _fontTotal!, new Rect(PadX, y, 60f, HeaderHeight), _brushText!,
            DrawTextOptions.None, MeasuringMode.Natural);

        // Countdown badge (clickable area: 70~150)
        {
            string badge = CountdownSec <= 0 ? "⏱off"
                         : CountdownExpired  ? $"⏱{CountdownSec}s ■"
                         :                     $"⏱{CountdownSec}s";
            var badgeBrush = CountdownExpired ? _brushGold! : _brushTextDim!;
            dc.DrawText(badge, _fontSmall!, new Rect(66f, y + 2f, 80f, HeaderHeight - 2f), badgeBrush,
                DrawTextOptions.None, MeasuringMode.Natural);
        }

        var totalLayout = _ctx!.DWriteFactory.CreateTextLayout(
            FormatDamage(_totalDamage), _fontTotal!, w, HeaderHeight);
        totalLayout.TextAlignment = TextAlignment.Trailing;
        dc.DrawTextLayout(new Vector2(PadX, y), totalLayout, _brushAccent!);
        totalLayout.Dispose();

        return y + HeaderHeight + 6f;
    }

    private float DrawTargetBar(ID2D1DeviceContext dc, float y)
    {
        if (_target is null) return y;

        float w = ClientSize.Width - PadX * 2;

        var bgRect = new Rect(PadX, y, w, TargetBarH);
        dc.FillRoundedRectangle(new RoundedRectangle { Rect = bgRect, RadiusX = 3f, RadiusY = 3f }, _brushHpBg!);

        double pct = _target.MaxHp > 0 ? (double)_target.CurrentHp / _target.MaxHp : 0;
        float fillW = (float)Math.Clamp(pct, 0, 1) * w;
        if (fillW > 1)
        {
            dc.FillRoundedRectangle(new RoundedRectangle
            {
                Rect = new Rect(PadX, y, fillW, TargetBarH),
                RadiusX = 3f, RadiusY = 3f
            }, _brushHpFill!);
        }

        // 1/3 and 2/3 marker lines.
        float x1 = PadX + w / 3f;
        float x2 = PadX + w * 2f / 3f;
        dc.DrawLine(new Vector2(x1, y + 2), new Vector2(x1, y + TargetBarH - 2), _brushBarBorder!, 1f);
        dc.DrawLine(new Vector2(x2, y + 2), new Vector2(x2, y + TargetBarH - 2), _brushBarBorder!, 1f);

        string label = $"{_target.Name}   {FormatDamage(_target.CurrentHp)} / {FormatDamage(_target.MaxHp)}   {pct * 100:0.#}%";
        var textLayout = _ctx!.DWriteFactory.CreateTextLayout(label, _fontSmall!, w, TargetBarH);
        textLayout.TextAlignment = TextAlignment.Center;
        textLayout.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX, y), textLayout, _brushText!);
        textLayout.Dispose();

        return y + TargetBarH + 6f;
    }

    private void DrawRows(ID2D1DeviceContext dc, float startY)
    {
        float y = startY;
        float w = ClientSize.Width - PadX * 2;

        const float IconLane = IconSize + 8f;
        float barLeft  = PadX + IconLane;
        float barW     = w - IconLane;
        float bottom   = ClientSize.Height - PadBottom;

        // Top player = 100% bar fill; others scale relative to top.
        long maxDamage = _rows.Count > 0 ? _rows[0].Damage : 1;
        if (maxDamage <= 0) maxDamage = 1;

        _rowHitAreas.Clear();
        int idx = 0;

        foreach (var row in _rows)
        {
            if (y + RowH > bottom) break;

            // Job icon.
            float iconY = y + (RowH - IconSize) / 2f;
            var icon = _icons?.Get(row.JobIconKey);
            if (icon != null)
            {
                var saveTransform = dc.Transform;
                float scale = IconSize / icon.Size.Width;
                dc.Transform = Matrix3x2.CreateScale(scale)
                             * Matrix3x2.CreateTranslation(PadX, iconY);
                dc.DrawImage(icon, null, null, InterpolationMode.Linear, CompositeMode.SourceOver);
                dc.Transform = saveTransform;
            }
            else
            {
                _brushAccent!.Color = row.AccentColor;
                dc.FillEllipse(new Ellipse(new Vector2(PadX + IconSize / 2, iconY + IconSize / 2),
                                           IconSize / 2 - 2, IconSize / 2 - 2), _brushAccent);
            }

            // Background track.  Rect(x, y, width, height)
            var barRect = new RoundedRectangle
            {
                Rect = new Rect(barLeft, y, barW, RowH),
                RadiusX = BarRadius, RadiusY = BarRadius
            };
            dc.FillRoundedRectangle(barRect, _brushBarBg!);

            // Accent fill — relative to top player (top = 100%).
            // Original uses opacity 0.35 for the bar fill to keep it subtle.
            float fillW = (float)Math.Clamp((double)row.Damage / maxDamage, 0, 1) * barW;
            if (fillW > 1)
            {
                _brushBarFill!.Color = new D2DColor(row.AccentColor.R, row.AccentColor.G, row.AccentColor.B, 0.35f);
                dc.FillRoundedRectangle(new RoundedRectangle
                {
                    Rect = new Rect(barLeft, y, fillW, RowH),
                    RadiusX = BarRadius, RadiusY = BarRadius
                }, _brushBarFill);
            }

            // Subtle border (rgba(255,255,255,.07))
            dc.DrawRoundedRectangle(barRect, _brushBarBorder!);

            // Text content.
            float textLeft = barLeft + 8f;
            float numW     = barW - 16f;

            // Name — color based on server faction.
            var nameBrush = row.ServerId switch
            {
                >= 1000 and < 2000 => _brushNameElyos!,  // 천족: 하늘색
                >= 2000 and < 3000 => _brushNameAsmo!,   // 마족: 연보라
                _ => _brushTextBright!,
            };
            string displayName = AnonymousMode && !string.IsNullOrEmpty(row.JobIconKey)
                ? (string.IsNullOrEmpty(row.ServerName) ? row.JobIconKey : $"{row.JobIconKey}[{row.ServerName}]")
                : row.Name;
            string nameText = row.CombatPower > 0 ? $"{displayName}  {row.CombatPower:N0}" : displayName;

            // Name (left)
            var nameLayout = _ctx!.DWriteFactory.CreateTextLayout(nameText, _fontName!, numW * 0.6f, 16);
            dc.DrawTextLayout(new Vector2(textLeft, y + 5), nameLayout, nameBrush);
            float nameWidth = nameLayout.Metrics.WidthIncludingTrailingWhitespace;
            nameLayout.Dispose();

            // Right-aligned stats: Damage (white) | DPS (gold) | Percent (white)
            string dmgText = FormatDamage(row.Damage);
            string dpsText = $"{FormatDamage(row.DpsValue)}/s";
            string pctText = $"{row.Percent * 100:0.#}%";
            const float statGap = 10f;

            // Measure each segment width
            using var pctLayout = _ctx.DWriteFactory.CreateTextLayout(pctText, _fontNumber!, numW, 16);
            float pctW = pctLayout.Metrics.Width;

            using var dpsLayout = _ctx.DWriteFactory.CreateTextLayout(dpsText, _fontNumber!, numW, 16);
            float dpsW = dpsLayout.Metrics.Width;

            using var dmgLayout = _ctx.DWriteFactory.CreateTextLayout(dmgText, _fontNumber!, numW, 16);
            float dmgW = dmgLayout.Metrics.Width;

            // Position from right edge
            float rightEdge = textLeft + numW;
            float pctX = rightEdge - pctW;
            float dpsX = pctX - statGap - dpsW;
            float dmgX = dpsX - statGap - dmgW;

            dc.DrawText(dmgText, _fontNumber!, new Rect(dmgX, y + 4, dmgW + 2, 16), _brushTextBright!,
                DrawTextOptions.None, MeasuringMode.Natural);
            dc.DrawText(dpsText, _fontNumber!, new Rect(dpsX, y + 4, dpsW + 2, 16), _brushGold!,
                DrawTextOptions.None, MeasuringMode.Natural);
            dc.DrawText(pctText, _fontNumber!, new Rect(pctX, y + 4, pctW + 2, 16), _brushTextBright!,
                DrawTextOptions.None, MeasuringMode.Natural);

            _rowHitAreas.Add((y, y + RowH, idx));
            y += RowH + RowGap;
            idx++;
        }

        if (_rows.Count == 0)
        {
            if (_summary != null) DrawSummary(dc, startY);
            else
            {
                float cy = ClientSize.Height / 2 - 12;
                float cw = ClientSize.Width - PadX * 2;
                var emptyLayout = _ctx!.DWriteFactory.CreateTextLayout(
                    "waiting for combat...", _fontSmall!, cw, 24);
                emptyLayout.TextAlignment = TextAlignment.Center;
                dc.DrawTextLayout(new Vector2(PadX, cy), emptyLayout, _brushTextDim!);
                emptyLayout.Dispose();
            }
        }
    }

    private void OnPlayerRowClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        float clickX = e.Location.X;
        float clickY = e.Location.Y;

        // Countdown button hit area: in the header row, right after timer text.
        if (clickY >= 6f && clickY < 6f + HeaderHeight && clickX >= 70f && clickX < 150f)
        {
            CountdownClicked?.Invoke();
            return;
        }

        foreach (var (top, bottom, i) in _rowHitAreas)
        {
            if (clickY >= top && clickY < bottom && i < _rows.Count)
            {
                PlayerRowClicked?.Invoke(_rows[i]);
                break;
            }
        }
    }

    private void DrawSummary(ID2D1DeviceContext dc, float startY)
    {
        if (_summary is null) return;
        float w = ClientSize.Width - PadX * 2;
        const float CardH = 92f;

        dc.FillRoundedRectangle(new RoundedRectangle
        {
            Rect = new Rect(PadX, startY, w, CardH),
            RadiusX = 6f, RadiusY = 6f
        }, _brushBarBg!);

        var titleLayout = _ctx!.DWriteFactory.CreateTextLayout("Last fight", _fontName!, w - 12, 20);
        dc.DrawTextLayout(new Vector2(PadX + 8, startY + 6), titleLayout, _brushText!);
        titleLayout.Dispose();
        var durLayout = _ctx.DWriteFactory.CreateTextLayout($"{_summary.DurationSec:0}s", _fontSmall!, w - 12, 20);
        durLayout.TextAlignment = TextAlignment.Trailing;
        dc.DrawTextLayout(new Vector2(PadX + 4, startY + 8), durLayout, _brushTextDim!);
        durLayout.Dispose();

        string line1 = $"total {FormatDamage(_summary.TotalDamage)}   avg {FormatDamage(_summary.AverageDps)}/s   peak {FormatDamage(_summary.PeakDps)}/s";
        string line2 = !string.IsNullOrEmpty(_summary.TopActorName)
            ? $"{_summary.TopActorName}  {FormatDamage(_summary.TopActorDamage)}"
            : "";
        if (!string.IsNullOrEmpty(_summary.BossName)) line2 = (line2.Length > 0 ? line2 + "   " : "") + "vs " + _summary.BossName;

        var l1 = _ctx.DWriteFactory.CreateTextLayout(line1, _fontSmall!, w - 12, 18);
        dc.DrawTextLayout(new Vector2(PadX + 8, startY + 32), l1, _brushText!);
        l1.Dispose();

        if (line2.Length > 0)
        {
            var l2 = _ctx.DWriteFactory.CreateTextLayout(line2, _fontSmall!, w - 12, 18);
            dc.DrawTextLayout(new Vector2(PadX + 8, startY + 52), l2, _brushTextDim!);
            l2.Dispose();
        }

        var hint = _ctx.DWriteFactory.CreateTextLayout("waiting for next fight...", _fontSmall!, w - 12, 18);
        hint.TextAlignment = TextAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX + 4, startY + 72), hint, _brushTextDim!);
        hint.Dispose();
    }

    private static string FormatDamage(long v)
    {
        if (Core.AppSettings.Instance.NumberFormat == "full")
            return v.ToString("N0");
        if (v >= 1_000_000_000) return (v / 1_000_000_000d).ToString("0.##") + "B";
        if (v >= 1_000_000)     return (v / 1_000_000d).ToString("0.##") + "M";
        if (v >= 1_000)         return (v / 1_000d).ToString("0.#") + "K";
        return v.ToString();
    }
}
