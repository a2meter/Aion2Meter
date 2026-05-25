using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using A2Meter.Core;
using A2Meter.Dps;
using A2Meter.Dps.Protocol;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using Vortice.WIC;
using WicPixelFormat = Vortice.WIC.PixelFormat;
using D2D = Vortice.Direct2D1;
using D3D11 = Vortice.Direct3D11;
using DW = Vortice.DirectWrite;
using D2DColor = Vortice.Mathematics.Color4;

namespace A2Meter.Direct2D;

/// Core D2D rendering engine for the overlay.
/// Renders to an offscreen bitmap, then blits to a layered window via UpdateLayeredWindow.
/// Does NOT own a window — receives hwnd + bounds for presentation.
internal sealed class OverlayRenderer : IDisposable
{
    // ── Layout (all scale with RowH so font/layout changes propagate) ──
    private const float PadX = 5f;
    private const float PadBottom = 6f;

    private float RowH => 30f * AppSettings.Instance.RowHeight / 90f;
    private float ToolbarHeight => RowH * 1.20f;
    private float HeaderHeight => RowH * 0.93f;
    private float TargetBarH => RowH * 0.67f;
    private float IconSize => RowH * 0.73f;
    private float RowGap => Math.Max(2f, RowH * 0.10f);

    // ── D3D11/D2D resources ──
    private ID2D1Factory1? _d2dFactory;
    private IDWriteFactory? _dwFactory;
    private ID3D11Device? _d3dDevice;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _dc;
    private ID3D11Texture2D? _rtTexture;
    private ID3D11Texture2D? _stagingTexture;
    private ID2D1Bitmap1? _targetBitmap;
    private D2DFontProvider? _fonts;
    private JobIconAtlas? _icons;
    private TierIconAtlas? _tierIcons;
    private ID2D1Bitmap1? _brandIcon;
    private int _texW, _texH;

    // ── D2D brushes ──
    private ID2D1SolidColorBrush? _brushBarBg;
    private ID2D1SolidColorBrush? _brushBarFill;
    private ID2D1SolidColorBrush? _brushBarBorder;
    private ID2D1SolidColorBrush? _brushText;
    private ID2D1SolidColorBrush? _brushTextBright;
    private ID2D1SolidColorBrush? _brushTextDim;
    private ID2D1SolidColorBrush? _brushGold;
    private ID2D1SolidColorBrush? _brushAccent;
    private ID2D1SolidColorBrush? _brushIconTint;
    private ID2D1SolidColorBrush? _brushHpBg;
    private ID2D1SolidColorBrush? _brushHpFill;
    private ID2D1SolidColorBrush? _brushNameAsmo;
    private ID2D1SolidColorBrush? _brushNameElyos;
    private ID2D1SolidColorBrush? _brushToolbarBg;

    // ── D2D fonts ──
    private IDWriteTextFormat? _fontName;
    private IDWriteTextFormat? _fontNumber;
    private IDWriteTextFormat? _fontSmall;
    private IDWriteTextFormat? _fontCpScore;
    private IDWriteTextFormat? _fontTotal;

    // ── Compact mode fonts (1pt smaller) ──
    private IDWriteTextFormat? _fontNameC;
    private IDWriteTextFormat? _fontNumberC;
    private IDWriteTextFormat? _fontSmallC;
    private IDWriteTextFormat? _fontCpScoreC;
    private IDWriteTextFormat? _fontTotalC;

    // ── Data state ──
    private readonly List<DpsCanvas.PlayerRow> _rows = new();
    private readonly List<(float Top, float Bottom, int Index)> _rowHitAreas = new();
    private string _timerText = "0:00";
    private long _totalDamage;
    private DpsCanvas.TargetInfo? _target;
    private DpsCanvas.SessionSummary? _summary;

    // ── Toolbar state ──
    private bool _locked;
    private bool _anonymous;
    public bool CompactMode { get; set; }
    public int PingMs { get; set; }
    public int CountdownSec { get; set; }
    public bool CountdownExpired { get; set; }

    // ── Tab state ──
    public enum TabId { Dps, Party }
    public TabId ActiveTab { get; set; } = TabId.Party;

    /// Party member rows for the Party tab.
    private readonly List<PartyRow> _partyRows = new();
    public sealed record PartyRow(
        string Name,
        string JobIconKey,
        int CombatPower,
        int CombatScore,
        int ServerId,
        string ServerName,
        bool IsSelf,
        int Level = 0,
        string Tier = "",
        bool IsPartyRequest = false,
        long PartyRequestOrder = 0);

    public void SetPartyData(IReadOnlyList<PartyRow> rows)
    {
        _partyRows.Clear();
        _partyRows.AddRange(rows);
    }

    // ── Hover/press tracking ──
    public enum ZoneId { None, Lock, Anon, History, Settings, Close, Slider, Countdown, CpToggle, ScoreToggle, TabDps, TabParty }
    private ZoneId _hoveredZone = ZoneId.None;

    // ── Toolbar button rects (updated each frame during rendering) ──
    private readonly Dictionary<ZoneId, RectangleF> _zones = new();

    // ── Slider state ──
    private int _sliderMin = 20, _sliderMax = 100;

    // ── Toast notifications ──
    private readonly Queue<(string Text, DateTime Expires)> _toasts = new();
    private const double ToastDurationSec = 3.0;

    // ── Cached icon geometries ──
    private ID2D1PathGeometry? _geoLockShackle;
    private ID2D1PathGeometry? _geoUnlockShackle;
    private ID2D1PathGeometry? _geoEyeTop;
    private ID2D1PathGeometry? _geoEyeBottom;

    // ──────────────────────────────────────────────────────────────────────
    // Init
    // ──────────────────────────────────────────────────────────────────────

    public void Init()
    {
        _d2dFactory = D2D.D2D1.D2D1CreateFactory<ID2D1Factory1>(D2D.FactoryType.SingleThreaded);
        _dwFactory = DW.DWrite.DWriteCreateFactory<IDWriteFactory>(DW.FactoryType.Shared);

        var creationFlags = DeviceCreationFlags.BgraSupport;
        var featureLevels = new[]
        {
            Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1, Vortice.Direct3D.FeatureLevel.Level_10_0,
        };

        D3D11.D3D11.D3D11CreateDevice(
            adapter: null, DriverType.Hardware, creationFlags, featureLevels, out var device);
        if (device == null)
        {
            D3D11.D3D11.D3D11CreateDevice(
                adapter: null, DriverType.Warp, creationFlags, featureLevels, out device);
        }
        _d3dDevice = device!;

        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice1>();
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _dc = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

        // Brushes
        _brushBarBg      = _dc.CreateSolidColorBrush(new D2DColor(0.078f, 0.098f, 0.157f, 0.60f));
        _brushBarFill    = _dc.CreateSolidColorBrush(new D2DColor(0.44f, 0.78f, 1.00f, 1.00f));
        _brushBarBorder  = _dc.CreateSolidColorBrush(new D2DColor(1f, 1f, 1f, 0.07f));
        _brushText       = _dc.CreateSolidColorBrush(new D2DColor(0.93f, 0.95f, 1.00f, 1.00f));
        _brushTextBright = _dc.CreateSolidColorBrush(new D2DColor(1f, 1f, 1f, 1f));
        _brushTextDim    = _dc.CreateSolidColorBrush(new D2DColor(0.635f, 0.694f, 0.784f, 1f));
        _brushGold       = _dc.CreateSolidColorBrush(new D2DColor(1f, 0.82f, 0.40f, 1f));
        _brushAccent     = _dc.CreateSolidColorBrush(new D2DColor(0.455f, 0.753f, 0.988f, 1f));
        _brushIconTint   = _dc.CreateSolidColorBrush(new D2DColor(1f, 1f, 1f, 1f));
        _brushHpBg       = _dc.CreateSolidColorBrush(new D2DColor(0.20f, 0.07f, 0.10f, 1.00f));
        _brushHpFill     = _dc.CreateSolidColorBrush(new D2DColor(0.85f, 0.20f, 0.25f, 1.00f));
        _brushNameElyos  = _dc.CreateSolidColorBrush(new D2DColor(0.55f, 0.82f, 1.00f, 1f));
        _brushNameAsmo   = _dc.CreateSolidColorBrush(new D2DColor(0.76f, 0.65f, 1.00f, 1f));
        _brushToolbarBg  = _dc.CreateSolidColorBrush(new D2DColor(0.145f, 0.145f, 0.208f, 0.85f));

        ApplyThemeBrushes();

        _fonts = new D2DFontProvider(_dwFactory);
        RebuildFonts();

        _icons = new JobIconAtlas(_dc);
        _tierIcons = new TierIconAtlas(_dc);
        _brandIcon = LoadBrandIcon(_dc);

        BuildIconGeometries();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Data
    // ──────────────────────────────────────────────────────────────────────

    public void SetData(IReadOnlyList<DpsCanvas.PlayerRow> rows, long totalDamage,
                        string timer, MobTarget? target, DpsCanvas.SessionSummary? summary)
    {
        _rows.Clear();
        _rows.AddRange(rows);
        _totalDamage = totalDamage;
        _timerText = timer;
        _target = target is { IsBoss: true, MaxHp: > 0 }
            ? new DpsCanvas.TargetInfo(target.Name, target.CurrentHp, target.MaxHp)
            : null;
        _summary = summary;
    }

    // ── State setters ──
    public void SetLocked(bool v) { _locked = v; }
    public void SetAnonymous(bool v) { _anonymous = v; }
    public void SetHoveredZone(ZoneId zone) { _hoveredZone = zone; }

    /// Show a brief toast notification on the overlay.
    public void ShowToast(string message)
    {
        lock (_toasts)
        {
            _toasts.Enqueue((message, DateTime.UtcNow.AddSeconds(ToastDurationSec)));
            while (_toasts.Count > 4) _toasts.Dequeue();
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Render
    // ──────────────────────────────────────────────────────────────────────

    public void RenderFrame(int width, int height)
    {
        if (_dc == null || width <= 0 || height <= 0) return;

        EnsureTextures(width, height);

        _dc.Target = _targetBitmap;
        _dc.BeginDraw();

        // Background clear
        var settings = AppSettings.Instance;
        if (CompactMode)
        {
            _dc.Clear(new D2DColor(0, 0, 0, 0)); // fully transparent
            _zones.Clear();

            float y = 0f;
            y = DrawCompactHeader(y, width);
            y = DrawTargetBar(y, width);
            DrawRows(y, width, height);
        }
        else
        {
            var bgc = settings.Theme.BgColor;
            float bgAlpha = Math.Max(0.12f, settings.Opacity / 100f);
            _dc.Clear(new D2DColor(bgc.R / 255f, bgc.G / 255f, bgc.B / 255f, bgAlpha));

            float y = 0f;
            y = DrawToolbar(y, width);
            y = DrawHeader(y, width);

            if (ActiveTab == TabId.Party)
            {
                DrawPartyRows(y, width, height);
            }
            else
            {
                y = DrawTargetBar(y, width);
                DrawRows(y, width, height);
            }
            DrawToasts(width, height);
        }

        _dc.EndDraw();
        _dc.Target = null;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Present
    // ──────────────────────────────────────────────────────────────────────

    public void PresentToLayeredWindow(IntPtr hwnd, int left, int top, int width, int height)
    {
        if (_d3dDevice == null || _rtTexture == null || _stagingTexture == null) return;

        var ctx = _d3dDevice.ImmediateContext;
        ctx.CopyResource(_stagingTexture, _rtTexture);

        var mapped = ctx.Map(_stagingTexture, 0, MapMode.Read);
        try
        {
            IntPtr hBmp = CreateHBitmapFromMapped(mapped.DataPointer, mapped.RowPitch, _texW, _texH);
            IntPtr hdcMem = Win32Native.CreateCompatibleDC(IntPtr.Zero);
            IntPtr oldBmp = Win32Native.SelectObject(hdcMem, hBmp);

            var ptDst = new Win32Native.POINT { X = left, Y = top };
            var size = new Win32Native.SIZE { CX = width, CY = height };
            var ptSrc = new Win32Native.POINT { X = 0, Y = 0 };

            var blend = new Win32Native.BLENDFUNCTION
            {
                BlendOp = Win32Native.AC_SRC_OVER,
                SourceConstantAlpha = 255,  // per-pixel alpha handles all opacity
                AlphaFormat = Win32Native.AC_SRC_ALPHA,
            };

            Win32Native.UpdateLayeredWindow(
                hwnd, IntPtr.Zero, ref ptDst, ref size,
                hdcMem, ref ptSrc, 0, ref blend, Win32Native.ULW_ALPHA);

            Win32Native.SelectObject(hdcMem, oldBmp);
            Win32Native.DeleteDC(hdcMem);
            Win32Native.DeleteObject(hBmp);
        }
        finally
        {
            ctx.Unmap(_stagingTexture, 0);
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Hit testing
    // ──────────────────────────────────────────────────────────────────────

    public ZoneId HitTest(Point pt)
    {
        foreach (var kvp in _zones)
            if (kvp.Value.Contains(pt.X, pt.Y))
                return kvp.Key;
        return ZoneId.None;
    }

    public int RowHitTest(float y)
    {
        foreach (var (top, bottom, idx) in _rowHitAreas)
            if (y >= top && y < bottom) return idx;
        return -1;
    }

    public float SliderValueFromX(int x)
    {
        if (!_zones.TryGetValue(ZoneId.Slider, out var rect)) return 0f;
        return Math.Clamp((x - rect.X) / rect.Width, 0f, 1f);
    }

    public IReadOnlyList<DpsCanvas.PlayerRow> GetRows() => _rows;

    public bool IsToolbarArea(float y) => !CompactMode && y < ToolbarHeight;

    public bool IsDragArea(Point pt)
    {
        if (CompactMode) return false;
        if (pt.Y >= ToolbarHeight) return false;
        // In toolbar area but not on any button/slider zone
        foreach (var kvp in _zones)
            if (kvp.Value.Contains(pt.X, pt.Y))
                return false;
        return true;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Settings
    // ──────────────────────────────────────────────────────────────────────

    public void ApplySettings()
    {
        RebuildFonts();
        ApplyThemeBrushes();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Dispose
    // ──────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _geoEyeBottom?.Dispose();
        _geoEyeTop?.Dispose();
        _geoUnlockShackle?.Dispose();
        _geoLockShackle?.Dispose();
        _brandIcon?.Dispose();
        _tierIcons?.Dispose();
        _icons?.Dispose();
        _fontTotalC?.Dispose();
        _fontCpScoreC?.Dispose();
        _fontSmallC?.Dispose();
        _fontNumberC?.Dispose();
        _fontNameC?.Dispose();
        _fontTotal?.Dispose();
        _fontCpScore?.Dispose();
        _fontSmall?.Dispose();
        _fontNumber?.Dispose();
        _fontName?.Dispose();
        _brushToolbarBg?.Dispose();
        _brushNameElyos?.Dispose();
        _brushNameAsmo?.Dispose();
        _brushHpFill?.Dispose();
        _brushHpBg?.Dispose();
        _brushAccent?.Dispose();
        _brushIconTint?.Dispose();
        _brushGold?.Dispose();
        _brushTextDim?.Dispose();
        _brushTextBright?.Dispose();
        _brushText?.Dispose();
        _brushBarBorder?.Dispose();
        _brushBarFill?.Dispose();
        _brushBarBg?.Dispose();
        _targetBitmap?.Dispose();
        _stagingTexture?.Dispose();
        _rtTexture?.Dispose();
        _dc?.Dispose();
        _d2dDevice?.Dispose();
        _d3dDevice?.Dispose();
        _dwFactory?.Dispose();
        _d2dFactory?.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Private — D2D infrastructure
    // ══════════════════════════════════════════════════════════════════════

    private void EnsureTextures(int w, int h)
    {
        if (w == _texW && h == _texH && _rtTexture != null) return;

        _targetBitmap?.Dispose();
        _rtTexture?.Dispose();
        _stagingTexture?.Dispose();

        _texW = w;
        _texH = h;

        var rtDesc = new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        };
        _rtTexture = _d3dDevice!.CreateTexture2D(rtDesc);

        var stageDesc = rtDesc;
        stageDesc.Usage = ResourceUsage.Staging;
        stageDesc.BindFlags = BindFlags.None;
        stageDesc.CPUAccessFlags = CpuAccessFlags.Read;
        _stagingTexture = _d3dDevice.CreateTexture2D(stageDesc);

        using var surface = _rtTexture.QueryInterface<IDXGISurface>();
        var bmpProps = new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96f, 96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        _targetBitmap = _dc!.CreateBitmapFromDxgiSurface(surface, bmpProps);
    }

    private static unsafe IntPtr CreateHBitmapFromMapped(IntPtr srcData, uint srcPitch, int w, int h)
    {
        var bmi = new Win32Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Win32Native.BITMAPINFOHEADER>(),
            biWidth = w,
            biHeight = -h, // top-down
            biPlanes = 1,
            biBitCount = 32,
        };

        IntPtr hBmp = Win32Native.CreateDIBSection(IntPtr.Zero, ref bmi, 0, out IntPtr bits, IntPtr.Zero, 0);
        if (bits == IntPtr.Zero) return hBmp;

        int rowBytes = w * 4;
        for (int row = 0; row < h; row++)
        {
            Buffer.MemoryCopy(
                (void*)(srcData + (int)(row * srcPitch)),
                (void*)(bits + row * rowBytes),
                rowBytes, rowBytes);
        }
        return hBmp;
    }

    private void ApplyThemeBrushes()
    {
        if (_brushBarBg == null) return;
        var t = AppSettings.Instance.Theme;
        _brushBarBg.Color    = ColorToD2D(t.HeaderColor);
        _brushText!.Color    = ColorToD2D(t.TextColor);
        _brushTextDim!.Color = ColorToD2D(t.TextDimColor);
        _brushAccent!.Color  = ColorToD2D(t.AccentColor);
        _brushNameElyos!.Color = ColorToD2D(t.ElyosColor);
        _brushNameAsmo!.Color  = ColorToD2D(t.AsmodianColor);

        var hdr = t.HeaderColor;
        _brushToolbarBg!.Color = new D2DColor(hdr.R / 255f, hdr.G / 255f, hdr.B / 255f, 0.85f);
    }

    private void RebuildFonts()
    {
        if (_fonts == null) return;
        _fontName?.Dispose();
        _fontNumber?.Dispose();
        _fontSmall?.Dispose();
        _fontCpScore?.Dispose();
        _fontTotal?.Dispose();
        _fontNameC?.Dispose();
        _fontNumberC?.Dispose();
        _fontSmallC?.Dispose();
        _fontCpScoreC?.Dispose();
        _fontTotalC?.Dispose();

        var s = AppSettings.Instance;
        float baseSize = s.FontSize * s.FontScale / 100f;
        _fontName    = CreateFont(baseSize + 4f);
        _fontNumber  = CreateFont(baseSize + 4f);
        _fontSmall   = CreateFont(baseSize + 1f);
        _fontCpScore = CreateFont(baseSize);
        _fontTotal   = CreateFont(baseSize + 6f);

        // Compact: 1pt smaller
        float cBase = baseSize - 1f;
        _fontNameC    = CreateFont(cBase + 4f);
        _fontNumberC  = CreateFont(cBase + 4f);
        _fontSmallC   = CreateFont(cBase + 1f);
        _fontCpScoreC = CreateFont(cBase);
        _fontTotalC   = CreateFont(cBase + 6f);

        IDWriteTextFormat CreateFont(float size)
        {
            var f = _fonts.CreateUi(size);
            f.WordWrapping = WordWrapping.NoWrap;
            return f;
        }
    }

    private static D2DColor ColorToD2D(System.Drawing.Color c)
        => new(c.R / 255f, c.G / 255f, c.B / 255f, 1f);

    // ── Font accessors (compact mode uses 1pt-smaller variants) ──
    private IDWriteTextFormat FName    => (CompactMode ? _fontNameC : _fontName)!;
    private IDWriteTextFormat FNumber  => (CompactMode ? _fontNumberC : _fontNumber)!;
    private IDWriteTextFormat FSmall   => (CompactMode ? _fontSmallC : _fontSmall)!;
    private IDWriteTextFormat FCpScore => (CompactMode ? _fontCpScoreC : _fontCpScore)!;
    private IDWriteTextFormat FTotal   => (CompactMode ? _fontTotalC : _fontTotal)!;

    // ══════════════════════════════════════════════════════════════════════
    // Private — Brand icon
    // ══════════════════════════════════════════════════════════════════════

    private static ID2D1Bitmap1? LoadBrandIcon(ID2D1DeviceContext dc)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath == null) return null;
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
            if (icon == null) return null;
            using var bmp = icon.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            using var wic = new IWICImagingFactory();
            using var stream = wic.CreateStream(ms);
            using var decoder = wic.CreateDecoderFromStream(stream, DecodeOptions.CacheOnLoad);
            using var frame = decoder.GetFrame(0);
            using var conv = wic.CreateFormatConverter();
            conv.Initialize(frame, WicPixelFormat.Format32bppPBGRA,
                            BitmapDitherType.None, null, 0.0, BitmapPaletteType.Custom);
            return dc.CreateBitmapFromWicBitmap(conv, null);
        }
        catch { return null; }
    }

    // Private — Icon geometry (cached, created once)
    // ══════════════════════════════════════════════════════════════════════

    private void BuildIconGeometries()
    {
        if (_d2dFactory == null) return;

        // Lock shackle (closed): arc from left to right over the body
        _geoLockShackle = _d2dFactory.CreatePathGeometry();
        using (var sink = _geoLockShackle.Open())
        {
            sink.BeginFigure(new Vector2(-4f, 0f), FigureBegin.Hollow);
            sink.AddLine(new Vector2(-4f, -3f));
            sink.AddArc(new ArcSegment
            {
                Point = new Vector2(4f, -3f),
                Size = new Vortice.Mathematics.Size(4f, 4f),
                SweepDirection = SweepDirection.Clockwise,
            });
            sink.AddLine(new Vector2(4f, 0f));
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }

        // Unlock shackle: shifted left, arc doesn't close on right
        _geoUnlockShackle = _d2dFactory.CreatePathGeometry();
        using (var sink = _geoUnlockShackle.Open())
        {
            sink.BeginFigure(new Vector2(-4f, 0f), FigureBegin.Hollow);
            sink.AddLine(new Vector2(-4f, -3f));
            sink.AddArc(new ArcSegment
            {
                Point = new Vector2(4f, -3f),
                Size = new Vortice.Mathematics.Size(4f, 4f),
                SweepDirection = SweepDirection.Clockwise,
            });
            sink.AddLine(new Vector2(4f, -1f));
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }

        // Eye shape top arc
        _geoEyeTop = _d2dFactory.CreatePathGeometry();
        using (var sink = _geoEyeTop.Open())
        {
            sink.BeginFigure(new Vector2(-7f, 0f), FigureBegin.Hollow);
            sink.AddArc(new ArcSegment
            {
                Point = new Vector2(7f, 0f),
                Size = new Vortice.Mathematics.Size(7f, 5f),
                SweepDirection = SweepDirection.Clockwise,
            });
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }

        // Eye shape bottom arc
        _geoEyeBottom = _d2dFactory.CreatePathGeometry();
        using (var sink = _geoEyeBottom.Open())
        {
            sink.BeginFigure(new Vector2(-7f, 0f), FigureBegin.Hollow);
            sink.AddArc(new ArcSegment
            {
                Point = new Vector2(7f, 0f),
                Size = new Vortice.Mathematics.Size(7f, 5f),
                SweepDirection = SweepDirection.CounterClockwise,
            });
            sink.EndFigure(FigureEnd.Open);
            sink.Close();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Private — Drawing: Toolbar
    // ══════════════════════════════════════════════════════════════════════

    private float DrawToolbar(float y, int width)
    {
        var dc = _dc!;
        _zones.Clear();

        // Background (alpha follows bg opacity)
        float bgOpacity = Math.Max(0.12f, AppSettings.Instance.Opacity / 100f);
        var hdr = AppSettings.Instance.Theme.HeaderColor;
        _brushToolbarBg!.Color = new D2DColor(hdr.R / 255f, hdr.G / 255f, hdr.B / 255f, 0.85f * bgOpacity);
        dc.FillRectangle(new Rect(0, y, width, ToolbarHeight), _brushToolbarBg);

        // Brand icon + version text (left, vertically centered)
        float brandW = 0f;
        float iconDrawSize = ToolbarHeight * 0.6f;
        if (_brandIcon != null)
        {
            float iconY = y + (ToolbarHeight - iconDrawSize) / 2f;
            float scale = iconDrawSize / _brandIcon.Size.Width;
            var save = dc.Transform;
            dc.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(PadX, iconY);
            dc.DrawBitmap(_brandIcon, 1f, D2D.InterpolationMode.Linear);
            dc.Transform = save;
            brandW += iconDrawSize + 4f;
        }
        string brand = $"v{AutoUpdater.CurrentVersion.ToString(3)}";
        var brandLayout = _dwFactory!.CreateTextLayout(brand, _fontSmall!, 400f, ToolbarHeight);
        brandLayout.ParagraphAlignment = ParagraphAlignment.Center;
        float textW = brandLayout.Metrics.WidthIncludingTrailingWhitespace;
        dc.DrawTextLayout(new Vector2(PadX + brandW, y), brandLayout, _brushTextDim!);
        brandLayout.Dispose();
        brandW += textW;

        // Tab buttons (right of brand)
        float tabX = PadX + brandW + 8f;
        {
            var dpsColor = ActiveTab == TabId.Dps
                ? new D2DColor(0.44f, 0.78f, 1f, 1f)
                : new D2DColor(0.45f, 0.48f, 0.55f, 1f);
            _brushBarFill!.Color = dpsColor;
            var dpsLayout = _dwFactory.CreateTextLayout("DPS", _fontSmall!, 120f, ToolbarHeight);
            dpsLayout.ParagraphAlignment = ParagraphAlignment.Center;
            float dpsW = dpsLayout.Metrics.WidthIncludingTrailingWhitespace;
            dc.DrawTextLayout(new Vector2(tabX, y), dpsLayout, _brushBarFill);
            dpsLayout.Dispose();
            _zones[ZoneId.TabDps] = new RectangleF(tabX, y, dpsW, ToolbarHeight);
            tabX += dpsW + 4f;

            var partyColor = ActiveTab == TabId.Party
                ? new D2DColor(0.40f, 0.85f, 0.55f, 1f)
                : new D2DColor(0.45f, 0.48f, 0.55f, 1f);
            _brushBarFill.Color = partyColor;
            var partyLayout = _dwFactory.CreateTextLayout("조회", _fontSmall!, 120f, ToolbarHeight);
            partyLayout.ParagraphAlignment = ParagraphAlignment.Center;
            float partyW = partyLayout.Metrics.WidthIncludingTrailingWhitespace;
            dc.DrawTextLayout(new Vector2(tabX, y), partyLayout, _brushBarFill);
            partyLayout.Dispose();
            _zones[ZoneId.TabParty] = new RectangleF(tabX, y, partyW, ToolbarHeight);
        }

        // Buttons (right-aligned, scale with toolbar height)
        int btnSize = (int)(ToolbarHeight * 0.72f);
        int gap = Math.Max(2, (int)(ToolbarHeight * 0.11f));
        float btnY = y + (ToolbarHeight - btnSize) / 2f;
        float bx = width - 8f - btnSize;

        DrawIconButton(dc, ZoneId.Close, bx, btnY, btnSize, DrawCloseIcon);
        bx -= btnSize + gap;
        DrawIconButton(dc, ZoneId.Settings, bx, btnY, btnSize, DrawGearIcon);
        bx -= btnSize + gap;
        DrawIconButton(dc, ZoneId.History, bx, btnY, btnSize, DrawHistoryIcon);
        bx -= btnSize + gap;
        DrawIconButton(dc, ZoneId.Anon, bx, btnY, btnSize, _anonymous ? DrawEyeOffIcon : DrawEyeIcon);
        bx -= btnSize + gap;
        DrawIconButton(dc, ZoneId.Lock, bx, btnY, btnSize, _locked ? DrawLockIcon : DrawUnlockIcon);

        // Opacity slider (left of the icon buttons)
        float sliderW = ToolbarHeight;
        float sliderH = ToolbarHeight * 0.56f;
        bx -= sliderW + gap;
        float sliderY = y + (ToolbarHeight - sliderH) / 2f;
        DrawSlider(dc, bx, sliderY, sliderW, sliderH);
        _zones[ZoneId.Slider] = new RectangleF(bx, sliderY, sliderW, sliderH);

        // Bottom border
        dc.DrawLine(new Vector2(0, y + ToolbarHeight - 1), new Vector2(width, y + ToolbarHeight - 1),
            _brushBarBorder!, 1f);

        return y + ToolbarHeight;
    }

    private void DrawIconButton(ID2D1DeviceContext dc, ZoneId zone, float x, float y, int size,
                                Action<ID2D1DeviceContext, float, float, ID2D1SolidColorBrush> drawIcon)
    {
        var rect = new RectangleF(x, y, size, size);
        _zones[zone] = rect;

        // Hover highlight
        if (_hoveredZone == zone)
        {
            var hoverColor = zone == ZoneId.Close
                ? new D2DColor(0.86f, 0.27f, 0.27f, 0.43f)
                : new D2DColor(0.24f, 0.39f, 0.63f, 0.27f);
            _brushBarFill!.Color = hoverColor;
            dc.FillRoundedRectangle(new RoundedRectangle
            {
                Rect = new Rect(x, y, size, size),
                RadiusX = 4f, RadiusY = 4f,
            }, _brushBarFill);
        }

        float cx = x + size / 2f;
        float cy = y + size / 2f;
        var fg = _hoveredZone == zone ? _brushTextBright! : _brushText!;
        drawIcon(dc, cx, cy, fg);
    }

    // ── Icon drawing methods ──

    private void DrawLockIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
    {
        // Body rectangle
        dc.DrawRectangle(new Rect(cx - 5f, cy - 1f, 10f, 8f), brush, 1.6f);
        // Closed shackle
        if (_geoLockShackle != null)
        {
            var save = dc.Transform;
            dc.Transform = Matrix3x2.CreateTranslation(cx, cy - 1f);
            dc.DrawGeometry(_geoLockShackle, brush, 1.6f);
            dc.Transform = save;
        }
    }

    private void DrawUnlockIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
    {
        dc.DrawRectangle(new Rect(cx - 5f, cy - 1f, 10f, 8f), brush, 1.6f);
        if (_geoUnlockShackle != null)
        {
            var save = dc.Transform;
            dc.Transform = Matrix3x2.CreateTranslation(cx - 2f, cy - 1f);
            dc.DrawGeometry(_geoUnlockShackle, brush, 1.6f);
            dc.Transform = save;
        }
    }

    private void DrawEyeIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
    {
        // Eye shape: two arcs
        if (_geoEyeTop != null && _geoEyeBottom != null)
        {
            var save = dc.Transform;
            dc.Transform = Matrix3x2.CreateTranslation(cx, cy);
            dc.DrawGeometry(_geoEyeTop, brush, 1.6f);
            dc.DrawGeometry(_geoEyeBottom, brush, 1.6f);
            dc.Transform = save;
        }
        // Pupil
        dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), 2f, 2f), brush, 1.6f);
    }

    private void DrawEyeOffIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
    {
        DrawEyeIcon(dc, cx, cy, brush);
        // Slash
        dc.DrawLine(new Vector2(cx - 6f, cy + 5f), new Vector2(cx + 6f, cy - 5f), brush, 1.6f);
    }

    private static void DrawHistoryIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
    {
        dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), 6f, 6f), brush, 1.6f);
        dc.DrawLine(new Vector2(cx, cy), new Vector2(cx, cy - 4f), brush, 1.6f);
        dc.DrawLine(new Vector2(cx, cy), new Vector2(cx + 3f, cy + 1f), brush, 1.6f);
    }

    private static void DrawGearIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
    {
        dc.DrawEllipse(new Ellipse(new Vector2(cx, cy), 4f, 4f), brush, 1.6f);
        for (int i = 0; i < 6; i++)
        {
            double angle = i * Math.PI / 3;
            float x1 = cx + (float)(4 * Math.Cos(angle));
            float y1 = cy + (float)(4 * Math.Sin(angle));
            float x2 = cx + (float)(6 * Math.Cos(angle));
            float y2 = cy + (float)(6 * Math.Sin(angle));
            dc.DrawLine(new Vector2(x1, y1), new Vector2(x2, y2), brush, 1.6f);
        }
    }

    private static void DrawCloseIcon(ID2D1DeviceContext dc, float cx, float cy, ID2D1SolidColorBrush brush)
    {
        dc.DrawLine(new Vector2(cx - 5f, cy - 5f), new Vector2(cx + 5f, cy + 5f), brush, 1.6f);
        dc.DrawLine(new Vector2(cx + 5f, cy - 5f), new Vector2(cx - 5f, cy + 5f), brush, 1.6f);
    }

    // ── Slider ──

    private void DrawSlider(ID2D1DeviceContext dc, float x, float y, float w, float h)
    {
        var settings = AppSettings.Instance;
        float ratio = Math.Clamp((settings.Opacity - _sliderMin) / (float)Math.Max(1, _sliderMax - _sliderMin), 0f, 1f);

        const float trackH = 4f;
        const float thumbR = 6f;
        float trackLeft = x + thumbR;
        float trackRight = x + w - thumbR;
        float cy = y + h / 2f;

        // Track background (dark groove)
        _brushBarFill!.Color = new D2DColor(0.12f, 0.16f, 0.24f, 1f);
        dc.FillRoundedRectangle(new RoundedRectangle
        {
            Rect = new Rect(trackLeft, cy - trackH / 2f, trackRight - trackLeft, trackH),
            RadiusX = trackH / 2f, RadiusY = trackH / 2f,
        }, _brushBarFill);

        // Filled portion (accent)
        float fillW = ratio * (trackRight - trackLeft);
        if (fillW > 1)
        {
            dc.FillRoundedRectangle(new RoundedRectangle
            {
                Rect = new Rect(trackLeft, cy - trackH / 2f, fillW, trackH),
                RadiusX = trackH / 2f, RadiusY = trackH / 2f,
            }, _brushAccent!);
        }

        // Thumb circle
        float thumbX = trackLeft + fillW;
        bool hovered = _hoveredZone == ZoneId.Slider;
        var thumbColor = hovered
            ? new D2DColor(0.86f, 0.92f, 1f, 1f)
            : new D2DColor(0.93f, 0.95f, 1f, 1f);
        _brushBarFill.Color = thumbColor;
        dc.FillEllipse(new Ellipse(new Vector2(thumbX, cy), thumbR, thumbR), _brushBarFill);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Private — Drawing: DPS Header
    // ══════════════════════════════════════════════════════════════════════

    private float DrawHeader(float y, int width)
    {
        var dc = _dc!;
        float w = width - PadX * 2;
        float rowH = RowH;
        float innerLeft = PadX + 8f;
        float innerW = w - 16f;

        // Timer (left, vertically centered)
        var timerLayout = _dwFactory!.CreateTextLayout(_timerText, _fontTotal!, 200f, rowH);
        timerLayout.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(innerLeft, y), timerLayout, _brushText!);
        float timerW = timerLayout.Metrics.WidthIncludingTrailingWhitespace;
        timerLayout.Dispose();

        // Countdown badge + CP toggle + Score toggle + Ping (flow left-to-right, 6f gap)
        float bx = innerLeft + timerW + 6f;
        {
            string badge = CountdownSec <= 0 ? "⏱off"
                         : CountdownExpired  ? $"⏱{CountdownSec}s ■"
                         :                     $"⏱{CountdownSec}s";
            var badgeBrush = CountdownExpired ? _brushGold! : _brushTextDim!;
            var badgeLayout = _dwFactory!.CreateTextLayout(badge, _fontSmall!, 200f, rowH);
            badgeLayout.ParagraphAlignment = ParagraphAlignment.Center;
            float badgeW = badgeLayout.Metrics.WidthIncludingTrailingWhitespace;
            dc.DrawTextLayout(new Vector2(bx, y), badgeLayout, badgeBrush);
            badgeLayout.Dispose();
            _zones[ZoneId.Countdown] = new RectangleF(bx, y, badgeW, rowH);
            bx += badgeW + 6f;
        }
        {
            var settings = AppSettings.Instance;

            var cpColor = settings.ShowCombatPower
                ? new D2DColor(0.39f, 0.71f, 1f, 1f)
                : new D2DColor(0.35f, 0.35f, 0.4f, 1f);
            _brushBarFill!.Color = cpColor;
            var cpToggleLayout = _dwFactory!.CreateTextLayout("전투력", _fontSmall!, 120f, rowH);
            cpToggleLayout.ParagraphAlignment = ParagraphAlignment.Center;
            float cpW = cpToggleLayout.Metrics.WidthIncludingTrailingWhitespace;
            dc.DrawTextLayout(new Vector2(bx, y), cpToggleLayout, _brushBarFill);
            cpToggleLayout.Dispose();
            _zones[ZoneId.CpToggle] = new RectangleF(bx, y, cpW, rowH);
            bx += cpW + 6f;

            var atColor = settings.ShowCombatScore
                ? new D2DColor(0.91f, 0.78f, 0.30f, 1f)
                : new D2DColor(0.35f, 0.35f, 0.4f, 1f);
            _brushBarFill.Color = atColor;
            var scoreToggleLayout = _dwFactory!.CreateTextLayout("아툴", _fontSmall!, 120f, rowH);
            scoreToggleLayout.ParagraphAlignment = ParagraphAlignment.Center;
            float atW = scoreToggleLayout.Metrics.WidthIncludingTrailingWhitespace;
            dc.DrawTextLayout(new Vector2(bx, y), scoreToggleLayout, _brushBarFill);
            scoreToggleLayout.Dispose();
            _zones[ZoneId.ScoreToggle] = new RectangleF(bx, y, atW, rowH);
            bx += atW + 6f;
        }

        // Ping indicator (flow)
        if (PingMs > 0)
        {
            string perfText = $"{PingMs}ms";
            var pingLayout = _dwFactory!.CreateTextLayout(perfText, _fontSmall!, 160f, rowH);
            pingLayout.ParagraphAlignment = ParagraphAlignment.Center;
            dc.DrawTextLayout(new Vector2(bx, y), pingLayout, _brushTextDim!);
            pingLayout.Dispose();
        }

        // Total damage (right-aligned)
        var totalLayout = _dwFactory!.CreateTextLayout(
            FormatDamage(_totalDamage), _fontTotal!, innerW * 0.5f, rowH);
        totalLayout.TextAlignment = TextAlignment.Trailing;
        totalLayout.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(innerLeft + innerW * 0.5f, y), totalLayout, _brushAccent!);
        totalLayout.Dispose();

        return y + rowH + RowGap;
    }

    /// Compact mode header: timer (left) + total damage (right), no badges/toolbar.
    private float DrawCompactHeader(float y, int width)
    {
        var dc = _dc!;
        float w = width - PadX * 2;
        float rowH = RowH;

        // Header background — same rect as DPS bars
        _brushBarFill!.Color = new D2DColor(0f, 0f, 0f, 0.1f);
        dc.FillRectangle(new Rect(PadX, y, w, rowH), _brushBarFill);

        // Timer (left)
        var timerLayout = _dwFactory!.CreateTextLayout(_timerText, FTotal, 200f, rowH);
        timerLayout.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX + 4f, y), timerLayout, _brushText!);
        timerLayout.Dispose();

        // Total damage (right-aligned)
        var totalLayout = _dwFactory.CreateTextLayout(
            FormatDamage(_totalDamage), FTotal, w * 0.6f, rowH);
        totalLayout.TextAlignment = TextAlignment.Trailing;
        totalLayout.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX + w * 0.4f - 4f, y), totalLayout, _brushAccent!);
        totalLayout.Dispose();

        return y + rowH + RowGap;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Private — Drawing: Target Bar
    // ══════════════════════════════════════════════════════════════════════

    private float DrawTargetBar(float y, int width)
    {
        if (_target is null) return y;

        var dc = _dc!;
        float w = width - PadX * 2;

        var bgRect = new Rect(PadX, y, w, TargetBarH);
        dc.FillRoundedRectangle(new RoundedRectangle { Rect = bgRect, RadiusX = 3f, RadiusY = 3f }, _brushHpBg!);

        double pct = _target.MaxHp > 0 ? (double)_target.CurrentHp / _target.MaxHp : 0;
        float fillW = (float)Math.Clamp(pct, 0, 1) * w;
        if (fillW > 1)
        {
            dc.FillRoundedRectangle(new RoundedRectangle
            {
                Rect = new Rect(PadX, y, fillW, TargetBarH),
                RadiusX = 3f, RadiusY = 3f,
            }, _brushHpFill!);
        }

        // 1/3 and 2/3 marker lines
        float x1 = PadX + w / 3f;
        float x2 = PadX + w * 2f / 3f;
        dc.DrawLine(new Vector2(x1, y + 2), new Vector2(x1, y + TargetBarH - 2), _brushBarBorder!, 1f);
        dc.DrawLine(new Vector2(x2, y + 2), new Vector2(x2, y + TargetBarH - 2), _brushBarBorder!, 1f);

        string label = $"{_target.Name}   {FormatDamage(_target.CurrentHp)} / {FormatDamage(_target.MaxHp)}   {pct * 100:0.#}%";
        var textLayout = _dwFactory!.CreateTextLayout(label, _fontSmall!, w, TargetBarH);
        textLayout.TextAlignment = TextAlignment.Center;
        textLayout.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX, y), textLayout, _brushText!);
        textLayout.Dispose();

        return y + TargetBarH + 6f;
    }

    // ══════════════════════════════════════════════════════════════════════
    // Private — Drawing: Rows
    // ══════════════════════════════════════════════════════════════════════

    private void DrawRows(float startY, int width, int height)
    {
        var dc = _dc!;
        float y = startY;
        float w = width - PadX * 2;

        float barLeft = PadX;
        float barW = w;
        float bottom = height - PadBottom;

        long maxDamage = _rows.Count > 0 ? _rows[0].Damage : 1;
        if (maxDamage <= 0) maxDamage = 1;

        _rowHitAreas.Clear();
        int idx = 0;
        float rowH = RowH;
        float barAlpha = Math.Clamp(AppSettings.Instance.BarOpacity / 100f, 0.05f, 1f);
        const float IconPad = 4f;  // icon padding inside bar

        foreach (var row in _rows)
        {
            if (y + rowH > bottom) break;

            // Background track (full width)
            _brushBarBg!.Color = new D2DColor(0.078f, 0.098f, 0.157f, 0.60f * barAlpha);
            var barRect = new Rect(barLeft, y, barW, rowH);
            dc.FillRectangle(barRect, _brushBarBg);

            // Accent fill proportional to top player
            float fillW = (float)Math.Clamp((double)row.Damage / maxDamage, 0, 1) * barW;
            if (fillW > 1)
            {
                _brushBarFill!.Color = new D2DColor(row.AccentColor.R, row.AccentColor.G, row.AccentColor.B, 0.35f * barAlpha);
                dc.FillRectangle(new Rect(barLeft, y, fillW, rowH), _brushBarFill);
            }

            // Subtle border
            _brushBarBorder!.Color = new D2DColor(1f, 1f, 1f, 0.07f * barAlpha);
            dc.DrawRectangle(barRect, _brushBarBorder);

            // Job icon (inside bar) — tinted with accent color
            float iconX = barLeft + IconPad;
            float iconY = y + (rowH - IconSize) / 2f;
            var icon = _icons?.Get(row.JobIconKey);
            if (icon != null)
            {
                DrawTintedIcon(dc, icon, iconX, iconY, row.IconColor);
            }
            else
            {
                _brushAccent!.Color = row.IconColor;
                dc.FillEllipse(new Ellipse(new Vector2(iconX + IconSize / 2, iconY + IconSize / 2),
                                           IconSize / 2 - 2, IconSize / 2 - 2), _brushAccent);
            }

            // Text content
            float textLeft = iconX + IconSize + 4f;
            float numW = barW - (textLeft - barLeft) - 8f;

            // Name — color based on server faction
            var nameBrush = row.ServerId switch
            {
                >= 1000 and < 2000 => _brushNameElyos!,
                >= 2000 and < 3000 => _brushNameAsmo!,
                _ => _brushTextBright!,
            };
            string displayName = _anonymous && !string.IsNullOrEmpty(row.JobIconKey)
                ? AnonName(row)
                : CompactMode ? CompactName(row) : row.Name;

            var settings = AppSettings.Instance;

            // Name (left, vertically centered)
            var nameLayout = _dwFactory!.CreateTextLayout(displayName, FName, numW, rowH);
            nameLayout.ParagraphAlignment = ParagraphAlignment.Center;
            dc.DrawTextLayout(new Vector2(textLeft, y), nameLayout, nameBrush);
            float nameWidth = nameLayout.Metrics.WidthIncludingTrailingWhitespace;
            nameLayout.Dispose();

            // CP / Score
            float cpScoreX = textLeft + nameWidth + 4f;

            if (settings.ShowCombatPower)
            {
                string cpText = row.CombatPower > 0 ? FormatAbbrev(row.CombatPower) : "—";
                _brushBarFill!.Color = new D2DColor(0.39f, 0.71f, 1f, 1f);
                var cpLayout = _dwFactory!.CreateTextLayout(cpText, FCpScore, 160f, rowH);
                cpLayout.ParagraphAlignment = ParagraphAlignment.Center;
                dc.DrawTextLayout(new Vector2(cpScoreX, y), cpLayout, _brushBarFill);
                cpScoreX += cpLayout.Metrics.WidthIncludingTrailingWhitespace + 3f;
                cpLayout.Dispose();
            }
            if (settings.ShowCombatScore)
            {
                string scoreText = row.CombatScore > 0 ? FormatAbbrev(row.CombatScore) : "—";
                _brushBarFill!.Color = new D2DColor(0.91f, 0.78f, 0.30f, 1f);
                var scoreLayout = _dwFactory!.CreateTextLayout(scoreText, FCpScore, 160f, rowH);
                scoreLayout.ParagraphAlignment = ParagraphAlignment.Center;
                dc.DrawTextLayout(new Vector2(cpScoreX, y), scoreLayout, _brushBarFill);
                scoreLayout.Dispose();
            }

            // Slot 3 (right-aligned, outermost)
            float slot3Width = 0f;
            var slot3Text = GetSlotText(settings.BarSlot3, row);
            if (slot3Text != null)
            {
                var slot3Font = GetSlotFont(settings.BarSlot3);
                var slot3Layout = _dwFactory.CreateTextLayout(slot3Text, slot3Font, numW, rowH);
                slot3Layout.TextAlignment = TextAlignment.Trailing;
                slot3Layout.ParagraphAlignment = ParagraphAlignment.Center;
                slot3Width = slot3Layout.Metrics.Width;
                using var slot3Brush = dc.CreateSolidColorBrush(ParseSlotColor(settings.BarSlot3.Color));
                dc.DrawTextLayout(new Vector2(textLeft, y), slot3Layout, slot3Brush);
                slot3Layout.Dispose();
            }

            // Slot 2 (right-aligned, left of slot3)
            float slot2Width = 0f;
            var slot2Text = GetSlotText(settings.BarSlot2, row);
            if (slot2Text != null)
            {
                var slot2Font = GetSlotFont(settings.BarSlot2);
                var slot2Layout = _dwFactory.CreateTextLayout(slot2Text, slot2Font, numW - slot3Width - 5, rowH);
                slot2Layout.TextAlignment = TextAlignment.Trailing;
                slot2Layout.ParagraphAlignment = ParagraphAlignment.Center;
                slot2Width = slot2Layout.Metrics.Width;
                using var slot2Brush = dc.CreateSolidColorBrush(ParseSlotColor(settings.BarSlot2.Color));
                dc.DrawTextLayout(new Vector2(textLeft, y), slot2Layout, slot2Brush);
                slot2Layout.Dispose();
            }

            // Slot 1 (right-aligned, left of slot2)
            var slot1Text = GetSlotText(settings.BarSlot1, row);
            if (slot1Text != null)
            {
                var slot1Font = GetSlotFont(settings.BarSlot1);
                var slot1Layout = _dwFactory.CreateTextLayout(slot1Text, slot1Font, numW - slot3Width - slot2Width - 10, rowH);
                slot1Layout.TextAlignment = TextAlignment.Trailing;
                slot1Layout.ParagraphAlignment = ParagraphAlignment.Center;
                using var slot1Brush = dc.CreateSolidColorBrush(ParseSlotColor(settings.BarSlot1.Color));
                dc.DrawTextLayout(new Vector2(textLeft, y), slot1Layout, slot1Brush);
                slot1Layout.Dispose();
            }

            _rowHitAreas.Add((y, y + rowH, idx));
            y += rowH + RowGap;
            idx++;
        }

        if (_rows.Count == 0)
        {
            if (_summary != null)
                DrawSummary(startY, width);
        }
    }

    private void DrawPartyRows(float startY, int width, int height)
    {
        var dc = _dc!;
        float y = startY;
        float w = width - PadX * 2;
        float bottom = height - PadBottom;
        float rowH = RowH;
        float barAlpha = Math.Clamp(AppSettings.Instance.BarOpacity / 100f, 0.05f, 1f);
        const float IconPad = 4f;

        _rowHitAreas.Clear();

        if (_partyRows.Count == 0)
        {
            var emptyLayout = _dwFactory!.CreateTextLayout("파티 정보 없음", FName, w, rowH);
            emptyLayout.ParagraphAlignment = ParagraphAlignment.Center;
            emptyLayout.TextAlignment = TextAlignment.Center;
            dc.DrawTextLayout(new Vector2(PadX, y), emptyLayout, _brushTextDim!);
            emptyLayout.Dispose();
            return;
        }

        foreach (var row in _partyRows)
        {
            if (y + rowH > bottom) break;

            // Background
            _brushBarBg!.Color = new D2DColor(0.078f, 0.098f, 0.157f, 0.60f * barAlpha);
            var barRect = new Rect(PadX, y, w, rowH);
            dc.FillRectangle(barRect, _brushBarBg);

            // Self highlight
            if (row.IsSelf)
            {
                _brushBarFill!.Color = new D2DColor(0.44f, 0.78f, 1f, 0.12f * barAlpha);
                dc.FillRectangle(barRect, _brushBarFill);
            }

            // Border
            _brushBarBorder!.Color = new D2DColor(1f, 1f, 1f, 0.07f * barAlpha);
            dc.DrawRectangle(barRect, _brushBarBorder);

            // Job icon — tinted with accent color
            float iconX = PadX + IconPad;
            float iconY = y + (rowH - IconSize) / 2f;
            var icon = _icons?.Get(row.JobIconKey);
            if (icon != null)
            {
                DrawTintedIcon(dc, icon, iconX, iconY, JobAccentFromName(row.JobIconKey));
            }

            // Name
            float textLeft = iconX + IconSize + 4f;
            float numW = w - (textLeft - PadX) - 8f;
            var nameBrush = row.ServerId switch
            {
                >= 1000 and < 2000 => _brushNameElyos!,
                >= 2000 and < 3000 => _brushNameAsmo!,
                _ => _brushTextBright!,
            };
            var nameLayout = _dwFactory!.CreateTextLayout(row.Name, FName, numW, rowH);
            nameLayout.ParagraphAlignment = ParagraphAlignment.Center;
            dc.DrawTextLayout(new Vector2(textLeft, y), nameLayout, nameBrush);
            float nameWidth = nameLayout.Metrics.WidthIncludingTrailingWhitespace;
            nameLayout.Dispose();

            // Level (right of name)
            float cpX = textLeft + nameWidth + 6f;
            if (row.IsPartyRequest)
            {
                string requestText = row.PartyRequestOrder > 0 ? $"신청 {row.PartyRequestOrder}" : "신청";
                _brushGold!.Color = new D2DColor(1f, 0.82f, 0.40f, 1f);
                var reqLayout = _dwFactory.CreateTextLayout(requestText, FCpScore, 120f, rowH);
                reqLayout.ParagraphAlignment = ParagraphAlignment.Center;
                dc.DrawTextLayout(new Vector2(cpX, y), reqLayout, _brushGold);
                cpX += reqLayout.Metrics.WidthIncludingTrailingWhitespace + 4f;
                reqLayout.Dispose();
            }
            if (row.Level > 0)
            {
                _brushTextDim!.Color = new D2DColor(0.70f, 0.70f, 0.70f, 1f);
                var lvLayout = _dwFactory.CreateTextLayout($"Lv.{row.Level}", FCpScore, 120f, rowH);
                lvLayout.ParagraphAlignment = ParagraphAlignment.Center;
                dc.DrawTextLayout(new Vector2(cpX, y), lvLayout, _brushTextDim);
                cpX += lvLayout.Metrics.WidthIncludingTrailingWhitespace + 4f;
                lvLayout.Dispose();
            }

            // CP + Score
            if (row.CombatPower > 0)
            {
                _brushBarFill!.Color = new D2DColor(0.39f, 0.71f, 1f, 1f);
                var cpLayout = _dwFactory.CreateTextLayout(FormatAbbrev(row.CombatPower), FCpScore, 160f, rowH);
                cpLayout.ParagraphAlignment = ParagraphAlignment.Center;
                dc.DrawTextLayout(new Vector2(cpX, y), cpLayout, _brushBarFill);
                cpX += cpLayout.Metrics.WidthIncludingTrailingWhitespace + 4f;
                cpLayout.Dispose();
            }
            if (row.CombatScore > 0)
            {
                _brushBarFill!.Color = new D2DColor(0.91f, 0.78f, 0.30f, 1f);
                var scoreLayout = _dwFactory.CreateTextLayout(FormatAbbrev(row.CombatScore), FCpScore, 160f, rowH);
                scoreLayout.ParagraphAlignment = ParagraphAlignment.Center;
                dc.DrawTextLayout(new Vector2(cpX, y), scoreLayout, _brushBarFill);
                cpX += scoreLayout.Metrics.WidthIncludingTrailingWhitespace + 4f;
                scoreLayout.Dispose();
            }

            if (!string.IsNullOrWhiteSpace(row.Tier))
            {
                float tierIconSize = Math.Min(20f, rowH * 0.68f);
                var tierIcon = _tierIcons?.Get(row.Tier);
                if (tierIcon != null)
                {
                    DrawBitmapScaled(dc, tierIcon, cpX, y + (rowH - tierIconSize) / 2f, tierIconSize);
                    cpX += tierIconSize + 3f;
                }

                _brushGold!.Color = TierTextColor(row.Tier);
                var tierLayout = _dwFactory.CreateTextLayout(TierShortKo(row.Tier), FCpScore, 120f, rowH);
                tierLayout.ParagraphAlignment = ParagraphAlignment.Center;
                dc.DrawTextLayout(new Vector2(cpX, y), tierLayout, _brushGold);
                tierLayout.Dispose();
            }

            // Server name (right-aligned)
            if (!string.IsNullOrEmpty(row.ServerName))
            {
                var svrLayout = _dwFactory.CreateTextLayout(row.ServerName, FSmall, numW, rowH);
                svrLayout.TextAlignment = TextAlignment.Trailing;
                svrLayout.ParagraphAlignment = ParagraphAlignment.Center;
                dc.DrawTextLayout(new Vector2(textLeft, y), svrLayout, _brushTextDim!);
                svrLayout.Dispose();
            }

            y += rowH + RowGap;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Private — Drawing: Summary / Empty
    // ══════════════════════════════════════════════════════════════════════

    private void DrawSummary(float startY, int width)
    {
        if (_summary is null) return;
        var dc = _dc!;
        float w = width - PadX * 2;
        const float CardH = 92f;

        dc.FillRoundedRectangle(new RoundedRectangle
        {
            Rect = new Rect(PadX, startY, w, CardH),
            RadiusX = 6f, RadiusY = 6f,
        }, _brushBarBg!);

        var titleLayout = _dwFactory!.CreateTextLayout("Last fight", _fontName!, w - 12, 26);
        titleLayout.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX + 8, startY), titleLayout, _brushText!);
        titleLayout.Dispose();

        var durLayout = _dwFactory.CreateTextLayout($"{_summary.DurationSec:0}s", _fontSmall!, w - 12, 26);
        durLayout.TextAlignment = TextAlignment.Trailing;
        durLayout.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX + 4, startY), durLayout, _brushTextDim!);
        durLayout.Dispose();

        string line1 = $"total {FormatDamage(_summary.TotalDamage)}   avg {FormatDamage(_summary.AverageDps)}/s   peak {FormatDamage(_summary.PeakDps)}/s";
        string line2 = !string.IsNullOrEmpty(_summary.TopActorName)
            ? $"{_summary.TopActorName}  {FormatDamage(_summary.TopActorDamage)}"
            : "";
        if (!string.IsNullOrEmpty(_summary.BossName))
            line2 = (line2.Length > 0 ? line2 + "   " : "") + "vs " + _summary.BossName;

        var l1 = _dwFactory.CreateTextLayout(line1, _fontSmall!, w - 12, 20);
        l1.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX + 8, startY + 30), l1, _brushText!);
        l1.Dispose();

        if (line2.Length > 0)
        {
            var l2 = _dwFactory.CreateTextLayout(line2, _fontSmall!, w - 12, 20);
            l2.ParagraphAlignment = ParagraphAlignment.Center;
            dc.DrawTextLayout(new Vector2(PadX + 8, startY + 50), l2, _brushTextDim!);
            l2.Dispose();
        }

        var hint = _dwFactory.CreateTextLayout("waiting for next fight...", _fontSmall!, w - 12, 20);
        hint.TextAlignment = TextAlignment.Center;
        hint.ParagraphAlignment = ParagraphAlignment.Center;
        dc.DrawTextLayout(new Vector2(PadX + 4, startY + 72), hint, _brushTextDim!);
        hint.Dispose();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Private — Drawing: Toasts
    // ══════════════════════════════════════════════════════════════════════

    private void DrawToasts(int width, int height)
    {
        List<(string Text, DateTime Expires)> active;
        lock (_toasts)
        {
            // Purge expired
            while (_toasts.Count > 0 && _toasts.Peek().Expires < DateTime.UtcNow)
                _toasts.Dequeue();
            if (_toasts.Count == 0) return;
            active = new List<(string, DateTime)>(_toasts);
        }

        var dc = _dc!;
        const float toastH = 24f;
        const float toastGap = 2f;
        float y = height - PadBottom - (toastH + toastGap) * active.Count;

        foreach (var (text, _) in active)
        {
            float tw = width - PadX * 2;
            _brushBarFill!.Color = new D2DColor(0.10f, 0.10f, 0.18f, 0.85f);
            dc.FillRoundedRectangle(new RoundedRectangle
            {
                Rect = new Rect(PadX, y, tw, toastH),
                RadiusX = 4f, RadiusY = 4f,
            }, _brushBarFill);

            var layout = _dwFactory!.CreateTextLayout(text, _fontSmall!, tw - 12, toastH);
            layout.TextAlignment = TextAlignment.Center;
            layout.ParagraphAlignment = ParagraphAlignment.Center;
            dc.DrawTextLayout(new Vector2(PadX + 6, y), layout, _brushText!);
            layout.Dispose();

            y += toastH + toastGap;
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Private — Slot helpers
    // ══════════════════════════════════════════════════════════════════════

    private string? GetSlotText(BarSlotConfig slot, DpsCanvas.PlayerRow row)
    {
        return slot.Content switch
        {
            "percent" => $" {row.Percent * 100:0.#}%",
            "damage"  => CompactMode ? FormatAbbrev(row.Damage) : FormatDamage(row.Damage),
            "dps"     => FormatDamage(row.DpsValue) + "/s",
            _ => null,
        };
    }

    private IDWriteTextFormat GetSlotFont(BarSlotConfig slot)
    {
        if (slot.FontSize >= 9f) return _fontNumber!;
        return _fontSmall!;
    }

    private static D2DColor ParseSlotColor(string hex)
    {
        try
        {
            var c = AppSettings.ThemeColors.ParseHex(hex);
            return new D2DColor(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
        }
        catch { return new D2DColor(0.43f, 0.43f, 0.5f, 1f); }
    }

    private static string FormatDamage(long v)
    {
        if (AppSettings.Instance.NumberFormat == "full")
            return v.ToString("N0");
        return FormatAbbrev(v);
    }

    /// Always-abbreviated number format (used for CP/Score regardless of settings).
    private static string FormatAbbrev(long v)
    {
        if (v >= 1_000_000_000) return (v / 1_000_000_000d).ToString("0.##") + "B";
        if (v >= 1_000_000)     return (v / 1_000_000d).ToString("0.##") + "M";
        if (v >= 1_000)         return (v / 1_000d).ToString("0.#") + "K";
        return v.ToString();
    }

    /// Anonymous display name: compact → job/server abbreviated, normal → full.
    private string AnonName(DpsCanvas.PlayerRow row)
    {
        if (CompactMode)
        {
            string job = row.JobIconKey.Length > 2 ? row.JobIconKey[..2] : row.JobIconKey;
            return row.ServerId <= 0 ? job : $"{job}[{ServerMap.GetShortName(row.ServerId)}]";
        }
        return row.ServerId <= 0 ? row.JobIconKey : $"{row.JobIconKey}[{row.ServerName}]";
    }

    /// Draw a job icon tinted with a solid accent color.
    /// Uses FillOpacityMask with the icon's alpha channel as the mask.
    private void DrawTintedIcon(ID2D1DeviceContext dc, ID2D1Bitmap1 icon, float x, float y, D2DColor accent)
    {
        var prevAA = dc.AntialiasMode;
        dc.AntialiasMode = AntialiasMode.Aliased;

        var save = dc.Transform;
        float scale = IconSize / icon.Size.Width;
        dc.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(x, y);

        _brushIconTint!.Color = accent;
        dc.FillOpacityMask(icon, _brushIconTint);

        dc.Transform = save;
        dc.AntialiasMode = prevAA;
    }

    private static void DrawBitmapScaled(ID2D1DeviceContext dc, ID2D1Bitmap1 icon, float x, float y, float size)
    {
        var save = dc.Transform;
        float scale = size / icon.Size.Width;
        dc.Transform = Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(x, y);
        dc.DrawBitmap(icon, 1f, D2D.InterpolationMode.Linear);
        dc.Transform = save;
    }

    private static string TierShortKo(string tier) => tier switch
    {
        "Challenger1" => "챌1",
        "Challenger2" => "챌2",
        "Challenger3" => "챌3",
        "Challenger"  => "챌",
        "Grandmaster" => "GM",
        "Master"      => "마",
        "Diamond"     => "다",
        "Platinum"    => "플",
        "Gold"        => "골",
        "Silver"      => "실",
        "Bronze"      => "브",
        "Iron"        => "아",
        "Unranked"    => "언",
        _             => "언",
    };

    private static D2DColor TierTextColor(string tier) => tier switch
    {
        "Challenger1" or "Challenger2" or "Challenger3" or "Challenger" => new D2DColor(1.00f, 0.48f, 0.32f, 1f),
        "Grandmaster" => new D2DColor(1.00f, 0.36f, 0.36f, 1f),
        "Master"      => new D2DColor(0.86f, 0.32f, 0.86f, 1f),
        "Diamond"     => new D2DColor(0.43f, 0.78f, 0.94f, 1f),
        "Platinum"    => new D2DColor(0.55f, 0.78f, 0.71f, 1f),
        "Gold"        => new D2DColor(0.90f, 0.78f, 0.39f, 1f),
        "Silver"      => new D2DColor(0.71f, 0.75f, 0.80f, 1f),
        "Bronze"      => new D2DColor(0.71f, 0.51f, 0.31f, 1f),
        "Iron"        => new D2DColor(0.52f, 0.56f, 0.62f, 1f),
        _             => new D2DColor(0.55f, 0.59f, 0.67f, 1f),
    };

    // ── Job accent palette (keyed by Korean name for Party tab icons) ──
    private static readonly Dictionary<string, D2DColor> _jobAccentByName = new()
    {
        ["검성"]  = new D2DColor(0.525f, 0.867f, 0.953f, 1f),
        ["궁성"]  = new D2DColor(0.384f, 0.694f, 0.561f, 1f),
        ["마도성"] = new D2DColor(0.718f, 0.549f, 0.949f, 1f),
        ["살성"]  = new D2DColor(0.643f, 0.906f, 0.608f, 1f),
        ["수호성"] = new D2DColor(0.490f, 0.627f, 0.976f, 1f),
        ["정령성"] = new D2DColor(0.812f, 0.420f, 0.816f, 1f),
        ["치유성"] = new D2DColor(0.906f, 0.812f, 0.490f, 1f),
        ["호법성"] = new D2DColor(0.894f, 0.647f, 0.357f, 1f),
    };

    private static D2DColor JobAccentFromName(string jobName)
        => _jobAccentByName.TryGetValue(jobName, out var c) ? c : new D2DColor(0.70f, 0.70f, 0.70f, 1f);

    /// Compact display name: replace [서버명] with abbreviated server name.
    private static string CompactName(DpsCanvas.PlayerRow row)
    {
        if (row.ServerId <= 0) return row.Name;
        string shortServer = ServerMap.GetShortName(row.ServerId);
        // Strip existing [서버명] suffix and append short version
        int open = row.Name.IndexOf('[');
        string baseName = open > 0 ? row.Name[..open] : row.Name;
        return string.IsNullOrEmpty(shortServer) ? baseName : $"{baseName}[{shortServer}]";
    }
}
