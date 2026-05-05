using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Direct2D;
using A2Meter.Dps;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2D = Vortice.Direct2D1;
using D3D11 = Vortice.Direct3D11;
using DW = Vortice.DirectWrite;
using D2DColor = Vortice.Mathematics.Color4;

namespace A2Meter.Forms;

/// Compact mode overlay: D2D offscreen render → UpdateLayeredWindow for per-pixel alpha.
/// Never calls SetLayeredWindowAttributes — only UpdateLayeredWindow.
internal sealed class CompactOverlayForm : Form
{
    private const int HandleHeight = 24;

    private IReadOnlyList<DpsCanvas.PlayerRow>? _rows;
    private long _totalDamage;
    private string _timer = "";
    private MobTarget? _target;

    // ── D2D offscreen resources ──
    private ID2D1Factory1? _d2dFactory;
    private IDWriteFactory? _dwFactory;
    private ID3D11Device? _d3dDevice;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _dc;
    private ID3D11Texture2D? _rtTexture;
    private ID3D11Texture2D? _stagingTexture;
    private ID2D1Bitmap1? _targetBitmap;
    private D2DFontProvider? _fonts;
    private int _texW, _texH;

    // ── D2D brushes ──
    private ID2D1SolidColorBrush? _brushBarBg;
    private ID2D1SolidColorBrush? _brushText;
    private ID2D1SolidColorBrush? _brushTextDim;
    private ID2D1SolidColorBrush? _brushGold;
    private ID2D1SolidColorBrush? _brushNameAsmo;
    private ID2D1SolidColorBrush? _brushNameElyos;
    private ID2D1SolidColorBrush? _brushHpBg;
    private ID2D1SolidColorBrush? _brushHpFill;
    private ID2D1SolidColorBrush? _brushHandle;
    private ID2D1SolidColorBrush? _brushAccent;

    // ── D2D fonts ──
    private IDWriteTextFormat? _fontName;
    private IDWriteTextFormat? _fontNumber;
    private IDWriteTextFormat? _fontSmall;

    public CompactOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
    }

    /// Rebuild colors/fonts from current AppSettings.
    public void ApplySettings()
    {
        RebuildFonts();
        ApplyThemeBrushes();
        RenderFrame();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32Native.WS_EX_LAYERED
                        | Win32Native.WS_EX_TOOLWINDOW
                        | Win32Native.WS_EX_NOACTIVATE
                        | Win32Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Win32Native.WM_NCHITTEST)
        {
            int lp = unchecked((int)(long)m.LParam);
            var screenPt = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
            var pt = PointToClient(screenPt);

            if (pt.Y >= 0 && pt.Y < HandleHeight)
            {
                m.Result = (IntPtr)Win32Native.HTCAPTION;
                return;
            }

            m.Result = (IntPtr)Win32Native.HTTRANSPARENT;
            return;
        }
        base.WndProc(ref m);
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

    public void PushData(IReadOnlyList<DpsCanvas.PlayerRow> rows, long totalDamage,
                         string timer, MobTarget? target, DpsCanvas.SessionSummary? _)
    {
        _rows = rows;
        _totalDamage = totalDamage;
        _timer = timer;
        _target = target;
        RenderFrame();
    }

    public void RenderFrame()
    {
        if (!Visible || Width <= 0 || Height <= 0) return;
        if (_dc == null) return;

        EnsureTextures(Width, Height);

        _dc.Target = _targetBitmap;
        _dc.BeginDraw();
        _dc.Clear(new D2DColor(0, 0, 0, 0)); // fully transparent

        float y = DrawHandle(HandleHeight);
        y = DrawHeader(y);
        y = DrawTargetBar(y);
        DrawRows(y);

        _dc.EndDraw();
        _dc.Target = null;

        PresentToLayeredWindow();
    }

    // ── D2D Init ──────────────────────────────────────────────────────────

    private void InitD2D()
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
        _brushBarBg     = _dc.CreateSolidColorBrush(new D2DColor(0.145f, 0.145f, 0.208f, 0.78f));
        _brushText      = _dc.CreateSolidColorBrush(new D2DColor(0.93f, 0.95f, 1f, 1f));
        _brushTextDim   = _dc.CreateSolidColorBrush(new D2DColor(0.63f, 0.69f, 0.78f, 0.7f));
        _brushGold      = _dc.CreateSolidColorBrush(new D2DColor(1f, 0.82f, 0.40f, 1f));
        _brushNameAsmo  = _dc.CreateSolidColorBrush(new D2DColor(0.55f, 0.82f, 1f, 1f));
        _brushNameElyos = _dc.CreateSolidColorBrush(new D2DColor(0.76f, 0.65f, 1f, 1f));
        _brushHpBg      = _dc.CreateSolidColorBrush(new D2DColor(0.20f, 0.07f, 0.10f, 1f));
        _brushHpFill    = _dc.CreateSolidColorBrush(new D2DColor(0.85f, 0.20f, 0.25f, 1f));
        _brushHandle    = _dc.CreateSolidColorBrush(new D2DColor(0.4f, 0.4f, 0.43f, 0.63f));
        _brushAccent    = _dc.CreateSolidColorBrush(new D2DColor(0.455f, 0.753f, 0.988f, 1f));

        ApplyThemeBrushes();

        _fonts = new D2DFontProvider(_dwFactory);
        RebuildFonts();
    }

    private void ApplyThemeBrushes()
    {
        if (_brushBarBg == null) return;
        var t = AppSettings.Instance.Theme;
        var hdr = t.HeaderColor;
        _brushBarBg.Color = new D2DColor(hdr.R / 255f, hdr.G / 255f, hdr.B / 255f, 0.78f);
        var txt = t.TextColor;
        _brushText!.Color = new D2DColor(txt.R / 255f, txt.G / 255f, txt.B / 255f, 1f);
        var dim = t.TextDimColor;
        _brushTextDim!.Color = new D2DColor(dim.R / 255f, dim.G / 255f, dim.B / 255f, 0.7f);
        var acc = t.AccentColor;
        _brushAccent!.Color = new D2DColor(acc.R / 255f, acc.G / 255f, acc.B / 255f, 1f);
        var elyos = t.ElyosColor;
        _brushNameElyos!.Color = new D2DColor(elyos.R / 255f, elyos.G / 255f, elyos.B / 255f, 1f);
        var asmo = t.AsmoColor;
        _brushNameAsmo!.Color = new D2DColor(asmo.R / 255f, asmo.G / 255f, asmo.B / 255f, 1f);
    }

    private void RebuildFonts()
    {
        if (_fonts == null) return;
        _fontName?.Dispose();
        _fontNumber?.Dispose();
        _fontSmall?.Dispose();

        var s = AppSettings.Instance;
        float baseSize = s.FontSize * s.FontScale / 100f;
        _fontName   = _fonts.CreateUi(baseSize + 4f);
        _fontNumber = _fonts.CreateUi(baseSize + 4f);
        _fontSmall  = _fonts.CreateUi(baseSize + 1f);
    }

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
            new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
            96f, 96f,
            BitmapOptions.Target | BitmapOptions.CannotDraw);
        _targetBitmap = _dc!.CreateBitmapFromDxgiSurface(surface, bmpProps);
    }

    // ── Drawing ───────────────────────────────────────────────────────────

    private const float PadX = 10f;
    private const float TargetBarH = 20f;
    private const float RowH = 30f;
    private const float RowGap = 3f;

    private const float HeaderH = 18f;

    private float DrawHandle(float h)
    {
        float w = _texW - PadX * 2;
        float barW = Math.Min(60f, w * 0.3f);
        float cx = _texW / 2f;
        float cy = h / 2f;
        _dc!.FillRectangle(new Rect(cx - barW / 2f, cy - 2f, barW, 4f), _brushHandle!);
        return h;
    }

    private float DrawHeader(float y)
    {
        if (_totalDamage <= 0 && string.IsNullOrEmpty(_timer)) return y;

        float w = _texW - PadX * 2;

        // Timer (left)
        _dc!.DrawText(_timer, _fontSmall!, new Rect(PadX, y, 60f, HeaderH), _brushText!,
            DrawTextOptions.None, MeasuringMode.Natural);

        // Total damage (right)
        string totalText = FormatDamage(_totalDamage);
        var totalLayout = _dwFactory!.CreateTextLayout(totalText, _fontSmall!, w, HeaderH);
        totalLayout.TextAlignment = TextAlignment.Trailing;
        _dc.DrawTextLayout(new Vector2(PadX, y), totalLayout, _brushAccent!);
        totalLayout.Dispose();

        return y + HeaderH + 2f;
    }

    private float DrawTargetBar(float y)
    {
        if (_target is not { IsBoss: true, MaxHp: > 0 }) return y;

        float w = _texW - PadX * 2;
        float pct = Math.Clamp((float)_target.CurrentHp / _target.MaxHp, 0, 1);

        _dc!.FillRectangle(new Rect(PadX, y, w, TargetBarH), _brushHpBg!);
        if (pct > 0)
            _dc.FillRectangle(new Rect(PadX, y, w * pct, TargetBarH), _brushHpFill!);

        // 1/3 and 2/3 markers
        using var markerBrush = _dc.CreateSolidColorBrush(new D2DColor(0.3f, 0.3f, 0.35f, 0.4f));
        _dc.DrawLine(new Vector2(PadX + w / 3f, y + 2), new Vector2(PadX + w / 3f, y + TargetBarH - 2), markerBrush, 1f);
        _dc.DrawLine(new Vector2(PadX + w * 2f / 3f, y + 2), new Vector2(PadX + w * 2f / 3f, y + TargetBarH - 2), markerBrush, 1f);

        string label = $"{_target.Name}   {FormatDamage(_target.CurrentHp)} / {FormatDamage(_target.MaxHp)}   {pct * 100:0.#}%";
        var layout = _dwFactory!.CreateTextLayout(label, _fontSmall!, w, TargetBarH);
        layout.TextAlignment = TextAlignment.Center;
        layout.ParagraphAlignment = ParagraphAlignment.Center;
        _dc.DrawTextLayout(new Vector2(PadX, y), layout, _brushText!);
        layout.Dispose();

        return y + TargetBarH + 4f;
    }

    private void DrawRows(float startY)
    {
        if (_rows == null || _rows.Count == 0) return;

        float y = startY;
        float w = _texW - PadX * 2;

        long maxDamage = _rows[0].Damage;
        if (maxDamage <= 0) maxDamage = 1;

        using var fillBrush = _dc!.CreateSolidColorBrush(new D2DColor(1, 1, 1, 0.35f));

        for (int i = 0; i < _rows.Count && y + RowH < _texH; i++)
        {
            var row = _rows[i];

            // Bar background
            _dc.FillRectangle(new Rect(PadX, y, w, RowH), _brushBarBg!);

            // Accent fill proportional to top player
            float fillW = (float)Math.Clamp((double)row.Damage / maxDamage, 0, 1) * w;
            if (fillW > 1)
            {
                fillBrush.Color = new D2DColor(row.AccentColor.R, row.AccentColor.G, row.AccentColor.B, 0.35f);
                _dc.FillRectangle(new Rect(PadX, y, fillW, RowH), fillBrush);
            }

            float textLeft = PadX + 8f;
            float numW = w - 16f;

            // Name (left)
            var nameBrush = row.ServerId switch
            {
                >= 1000 and < 2000 => _brushNameAsmo!,
                >= 2000 and < 3000 => _brushNameElyos!,
                _ => _brushText!,
            };
            var nameLayout = _dwFactory!.CreateTextLayout(row.Name, _fontName!, numW * 0.6f, 16);
            _dc.DrawTextLayout(new Vector2(textLeft, y + 5), nameLayout, nameBrush);
            nameLayout.Dispose();

            // Right-aligned stats: Damage (white) | DPS (gold) | Percent (white)
            string dmgText = FormatDamage(row.Damage);
            string dpsText = FormatDamage(row.DpsValue) + "/s";
            string pctText = $"{row.Percent * 100:0.#}%";
            const float statGap = 10f;

            // Measure each segment width
            var pctLayout = _dwFactory.CreateTextLayout(pctText, _fontSmall!, numW, 16);
            float pctW = pctLayout.Metrics.Width;

            var dpsLayout = _dwFactory.CreateTextLayout(dpsText, _fontNumber!, numW, 16);
            float dpsW = dpsLayout.Metrics.Width;

            var dmgLayout = _dwFactory.CreateTextLayout(dmgText, _fontSmall!, numW, 16);
            float dmgW = dmgLayout.Metrics.Width;

            // Position from right edge
            float rightEdge = textLeft + numW;
            float pctX = rightEdge - pctW;
            float dpsX = pctX - statGap - dpsW;
            float dmgX = dpsX - statGap - dmgW;

            _dc.DrawText(dmgText, _fontSmall!, new Rect(dmgX, y + 6, dmgW + 2, 14), _brushTextDim!,
                DrawTextOptions.None, MeasuringMode.Natural);
            _dc.DrawText(dpsText, _fontNumber!, new Rect(dpsX, y + 4, dpsW + 2, 16), _brushGold!,
                DrawTextOptions.None, MeasuringMode.Natural);
            _dc.DrawText(pctText, _fontSmall!, new Rect(pctX, y + 6, pctW + 2, 14), _brushTextDim!,
                DrawTextOptions.None, MeasuringMode.Natural);

            pctLayout.Dispose();
            dpsLayout.Dispose();
            dmgLayout.Dispose();

            y += RowH + RowGap;
        }
    }

    // ── Pixel readback → UpdateLayeredWindow ──────────────────────────────

    private void PresentToLayeredWindow()
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

            var ptDst = new Win32Native.POINT { X = Left, Y = Top };
            var size = new Win32Native.SIZE { CX = _texW, CY = _texH };
            var ptSrc = new Win32Native.POINT { X = 0, Y = 0 };
            var blend = new Win32Native.BLENDFUNCTION
            {
                BlendOp = Win32Native.AC_SRC_OVER,
                SourceConstantAlpha = (byte)Math.Clamp(AppSettings.Instance.Opacity * 255 / 100, 30, 255),
                AlphaFormat = Win32Native.AC_SRC_ALPHA,
            };

            Win32Native.UpdateLayeredWindow(
                Handle, IntPtr.Zero, ref ptDst, ref size,
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

        // Copy row by row (srcPitch may differ from w*4)
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

    // ── Cleanup ───────────────────────────────────────────────────────────

    private void DisposeD2D()
    {
        _fontSmall?.Dispose();
        _fontNumber?.Dispose();
        _fontName?.Dispose();
        _brushAccent?.Dispose();
        _brushNameElyos?.Dispose();
        _brushNameAsmo?.Dispose();
        _brushHandle?.Dispose();
        _brushHpFill?.Dispose();
        _brushHpBg?.Dispose();
        _brushNameElyos?.Dispose();
        _brushNameAsmo?.Dispose();
        _brushGold?.Dispose();
        _brushTextDim?.Dispose();
        _brushText?.Dispose();
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        DisposeD2D();
        base.OnFormClosed(e);
    }

    // ── Utility ───────────────────────────────────────────────────────────

    private static string FormatDamage(long v)
    {
        if (AppSettings.Instance.NumberFormat == "full")
            return v.ToString("N0");
        if (v >= 1_000_000_000) return (v / 1_000_000_000d).ToString("0.##") + "B";
        if (v >= 1_000_000)     return (v / 1_000_000d).ToString("0.##") + "M";
        if (v >= 1_000)         return (v / 1_000d).ToString("0.#") + "K";
        return v.ToString();
    }
}
