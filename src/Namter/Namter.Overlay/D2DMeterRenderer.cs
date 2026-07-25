using System.Globalization;
using System.Numerics;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using D2D = Vortice.Direct2D1;
using DW = Vortice.DirectWrite;
using DCommon = Vortice.DCommon;

namespace Namter.Overlay;

/// Independent Direct2D renderer for the meter. Renders into an offscreen
/// D3D11 texture, copies it to a CPU-readable staging texture, and presents it
/// to a per-pixel-alpha layered window via UpdateLayeredWindow. No dependency
/// on A2Meter — only the public Vortice.Windows D2D bindings.
internal sealed class D2DMeterRenderer : IDisposable
{
    private const float HeaderH = 34f;
    private const float SummaryH = 26f;
    private const float RowH = 34f;

    private ID2D1Factory1 _d2dFactory = null!;
    private IDWriteFactory _dwFactory = null!;
    private ID3D11Device _d3dDevice = null!;
    private ID2D1Device _d2dDevice = null!;
    private ID2D1DeviceContext _dc = null!;

    private ID2D1Bitmap1? _target;
    private ID3D11Texture2D? _rtTexture;
    private ID3D11Texture2D? _stagingTexture;
    private int _texW, _texH;

    private ID2D1SolidColorBrush _brPanel = null!;
    private ID2D1SolidColorBrush _brText = null!;
    private ID2D1SolidColorBrush _brDim = null!;
    private ID2D1SolidColorBrush _brSelf = null!;
    private ID2D1SolidColorBrush _brGreen = null!;
    private ID2D1SolidColorBrush _brSep = null!;
    private ID2D1SolidColorBrush _brClose = null!;
    private ID2D1SolidColorBrush[] _brRanks = null!;

    private IDWriteTextFormat _fmtBrand = null!;
    private IDWriteTextFormat _fmtSub = null!;
    private IDWriteTextFormat _fmtName = null!;
    private IDWriteTextFormat _fmtNum = null!;
    private IDWriteTextFormat _fmtSmall = null!;
    private IDWriteTextFormat _fmtSummaryL = null!;
    private IDWriteTextFormat _fmtSummaryR = null!;
    private IDWriteTextFormat _fmtCenter = null!;

    public bool CloseHovered { get; set; }

    public void Init()
    {
        _d2dFactory = D2D.D2D1.D2D1CreateFactory<ID2D1Factory1>(D2D.FactoryType.SingleThreaded);
        _dwFactory = DW.DWrite.DWriteCreateFactory<IDWriteFactory>(DW.FactoryType.Shared);

        var flags = DeviceCreationFlags.BgraSupport;
        var levels = new[]
        {
            Vortice.Direct3D.FeatureLevel.Level_11_1, Vortice.Direct3D.FeatureLevel.Level_11_0,
            Vortice.Direct3D.FeatureLevel.Level_10_1, Vortice.Direct3D.FeatureLevel.Level_10_0,
        };
        Vortice.Direct3D11.D3D11.D3D11CreateDevice(null, DriverType.Hardware, flags, levels, out var device);
        if (device is null)
            Vortice.Direct3D11.D3D11.D3D11CreateDevice(null, DriverType.Warp, flags, levels, out device);
        _d3dDevice = device!;

        using var dxgiDevice = _d3dDevice.QueryInterface<IDXGIDevice1>();
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _dc = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

        _brPanel = _dc.CreateSolidColorBrush(new Color4(0.094f, 0.106f, 0.145f, 0.94f));
        _brText = _dc.CreateSolidColorBrush(new Color4(0.886f, 0.910f, 0.941f, 1f));
        _brDim = _dc.CreateSolidColorBrush(new Color4(0.580f, 0.639f, 0.722f, 1f));
        _brSelf = _dc.CreateSolidColorBrush(new Color4(0.910f, 0.784f, 0.302f, 1f));
        _brGreen = _dc.CreateSolidColorBrush(new Color4(0.470f, 0.784f, 0.549f, 1f));
        _brSep = _dc.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 0.08f));
        _brClose = _dc.CreateSolidColorBrush(new Color4(0.86f, 0.30f, 0.30f, 1f));
        _brRanks = new[]
        {
            _dc.CreateSolidColorBrush(new Color4(0.220f, 0.518f, 0.871f, 1f)),
            _dc.CreateSolidColorBrush(new Color4(0.298f, 0.659f, 0.510f, 1f)),
            _dc.CreateSolidColorBrush(new Color4(0.706f, 0.471f, 0.839f, 1f)),
            _dc.CreateSolidColorBrush(new Color4(0.839f, 0.549f, 0.275f, 1f)),
            _dc.CreateSolidColorBrush(new Color4(0.353f, 0.667f, 0.784f, 1f)),
            _dc.CreateSolidColorBrush(new Color4(0.588f, 0.588f, 0.667f, 1f)),
        };

        _fmtBrand = Fmt(13f, FontWeight.Bold, TextAlignment.Leading);
        _fmtSub = Fmt(10.5f, FontWeight.Normal, TextAlignment.Leading);
        _fmtName = Fmt(12.5f, FontWeight.Bold, TextAlignment.Leading);
        _fmtNum = Fmt(13f, FontWeight.Normal, TextAlignment.Trailing);
        _fmtSmall = Fmt(10.5f, FontWeight.Normal, TextAlignment.Trailing);
        _fmtSummaryL = Fmt(10.5f, FontWeight.Normal, TextAlignment.Leading);
        _fmtSummaryR = Fmt(10.5f, FontWeight.Normal, TextAlignment.Trailing);
        _fmtCenter = Fmt(12f, FontWeight.Normal, TextAlignment.Center);
        _fmtCenter.WordWrapping = WordWrapping.Wrap;
    }

    private IDWriteTextFormat Fmt(float size, FontWeight weight, TextAlignment align)
    {
        IDWriteTextFormat f = _dwFactory.CreateTextFormat("Malgun Gothic", weight, DW.FontStyle.Normal, size);
        f.TextAlignment = align;
        f.ParagraphAlignment = ParagraphAlignment.Center;
        f.WordWrapping = WordWrapping.NoWrap;
        return f;
    }

    private void EnsureTarget(int w, int h)
    {
        if (_rtTexture is not null && _texW == w && _texH == h) return;

        _dc.Target = null;
        _target?.Dispose(); _target = null;
        _rtTexture?.Dispose(); _rtTexture = null;
        _stagingTexture?.Dispose(); _stagingTexture = null;

        _texW = w; _texH = h;
        _rtTexture = _d3dDevice.CreateTexture2D(
            new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.RenderTarget | BindFlags.ShaderResource));
        _stagingTexture = _d3dDevice.CreateTexture2D(
            new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)w, (uint)h, 1, 1, BindFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read));

        using IDXGISurface surface = _rtTexture.QueryInterface<IDXGISurface>();
        var props = new BitmapProperties1(
            new DCommon.PixelFormat(Format.B8G8R8A8_UNorm, DCommon.AlphaMode.Premultiplied),
            96f, 96f, BitmapOptions.Target | BitmapOptions.CannotDraw);
        _target = _dc.CreateBitmapFromDxgiSurface(surface, props);
    }

    public void RenderFrame(MeterView view, string? error, int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        EnsureTarget(w, h);
        _dc.Target = _target;
        _dc.BeginDraw();
        _dc.Transform = Matrix3x2.Identity;
        _dc.Clear(new Color4(0f, 0f, 0f, 0f));

        // Panel background (square, semi-transparent).
        _dc.FillRectangle(new Rect(0, 0, w, h), _brPanel);

        // Header.
        _dc.DrawText("Namter", _fmtBrand, new Rect(10, 0, 120, HeaderH), _brText);
        _dc.DrawText("실시간 DPS", _fmtSub, new Rect(74, 0, 120, HeaderH), _brDim);
        DrawClose(w);
        _dc.DrawLine(new Vector2(8, HeaderH), new Vector2(w - 8, HeaderH), _brSep, 1f);

        // Summary strip.
        DrawSummary(view, w);
        float rowsTop = HeaderH + SummaryH;
        _dc.DrawLine(new Vector2(8, rowsTop), new Vector2(w - 8, rowsTop), _brSep, 1f);

        if (!view.Rows.IsDefaultOrEmpty)
            DrawRows(view, w, h, rowsTop);
        else if (!string.IsNullOrEmpty(error))
            _dc.DrawText($"백엔드 오류: {error}", _fmtCenter, new Rect(8, rowsTop + 8, w - 16, h - rowsTop - 16), _brClose);
        else
            _dc.DrawText("전투 대기 중…  (캡처 실행 중, 관리자 권한 필요)", _fmtCenter, new Rect(8, rowsTop + 8, w - 16, h - rowsTop - 16), _brDim);

        _dc.EndDraw();
        _dc.Target = null;
    }

    private void DrawClose(int w)
    {
        float bx = w - HeaderH, cx = bx + HeaderH / 2f, cy = HeaderH / 2f;
        if (CloseHovered)
            _dc.FillRectangle(new Rect(bx, 0, HeaderH, HeaderH), _brClose);
        ID2D1SolidColorBrush b = CloseHovered ? _brText : _brDim;
        _dc.DrawLine(new Vector2(cx - 5, cy - 5), new Vector2(cx + 5, cy + 5), b, 1.5f);
        _dc.DrawLine(new Vector2(cx + 5, cy - 5), new Vector2(cx - 5, cy + 5), b, 1.5f);
    }

    private void DrawSummary(MeterView view, int w)
    {
        string boss = string.IsNullOrEmpty(view.BossName) ? "—" : view.BossName;
        string left = $"{boss}   ⏱ {Elapsed(view.ElapsedMs)}";
        _dc.DrawText(left, _fmtSummaryL, new Rect(10, HeaderH, w - 120, SummaryH), _brText);

        double totalDps = view.ElapsedMs > 0 ? view.TotalDamage / (view.ElapsedMs / 1000.0) : 0;
        string right = view.BossMaxHp is ulong max && max > 0
            ? $"보스 HP {100.0 * (view.BossCurrentHp ?? 0) / max:0}%   총 {Dps(totalDps)}/s"
            : $"총 {Dps(totalDps)}/s";
        _dc.DrawText(right, _fmtSummaryR, new Rect(w - 230, HeaderH, 220, SummaryH), view.Live ? _brGreen : _brDim);
    }

    private void DrawRows(MeterView view, int w, int h, float top)
    {
        ulong topDamage = view.Rows[0].Damage;
        if (topDamage == 0) topDamage = 1;

        float y = top;
        int rank = 0;
        foreach (MeterRow row in view.Rows)
        {
            if (y + RowH > h) break;

            float barMax = w - 16;
            float barW = Math.Max(2f, barMax * (float)((double)row.Damage / topDamage));
            ID2D1SolidColorBrush bar = row.IsSelf ? _brSelf : _brRanks[rank % _brRanks.Length];
            bar.Opacity = row.IsSelf ? 0.28f : 0.24f;
            _dc.FillRectangle(new Rect(8, y + 3, barW, RowH - 6), bar);
            bar.Opacity = 1f;

            if (row.IsSelf)
                _dc.FillRectangle(new Rect(8, y + 3, 3, RowH - 6), _brSelf);

            ID2D1SolidColorBrush nameBrush = row.IsSelf ? _brSelf : _brText;
            string name = string.IsNullOrEmpty(row.Name) ? $"#{row.ActorId}" : row.Name;
            _dc.DrawText(name, _fmtName, new Rect(12, y, w - 250, RowH), nameBrush);

            _dc.DrawText(Num(row.Damage), _fmtNum, new Rect(w - 96, y, 84, RowH), _brText);
            _dc.DrawText($"{Dps(row.DpsPerSec)}/s", _fmtSmall, new Rect(w - 172, y, 68, RowH), _brDim);
            _dc.DrawText($"{row.BossHpShare * 100:0.0}%", _fmtSmall, new Rect(w - 236, y, 58, RowH), _brGreen);

            _dc.DrawLine(new Vector2(8, y + RowH), new Vector2(w - 8, y + RowH), _brSep, 1f);
            y += RowH;
            rank++;
        }
    }

    /// Copies the rendered frame to the staging texture and blits it to the
    /// layered window with per-pixel alpha.
    public unsafe void Present(IntPtr hwnd, int left, int top)
    {
        if (_rtTexture is null || _stagingTexture is null) return;
        ID3D11DeviceContext ctx = _d3dDevice.ImmediateContext;
        ctx.CopyResource(_stagingTexture, _rtTexture);

        MappedSubresource map = ctx.Map(_stagingTexture, 0, MapMode.Read);
        try
        {
            IntPtr hbmp = NativeMethods.CreateDib(_texW, _texH, out IntPtr bits);
            if (hbmp == IntPtr.Zero) return;

            byte* src = (byte*)map.DataPointer;
            byte* dst = (byte*)bits;
            int rowBytes = _texW * 4;
            for (int row = 0; row < _texH; row++)
                Buffer.MemoryCopy(src + (long)row * map.RowPitch, dst + (long)row * rowBytes, rowBytes, rowBytes);

            IntPtr hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            IntPtr old = NativeMethods.SelectObject(hdc, hbmp);
            var ptDst = new NativeMethods.POINT { X = left, Y = top };
            var ptSrc = new NativeMethods.POINT { X = 0, Y = 0 };
            var size = new NativeMethods.SIZE { CX = _texW, CY = _texH };
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };
            NativeMethods.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref ptDst, ref size, hdc, ref ptSrc, 0, ref blend, NativeMethods.ULW_ALPHA);

            NativeMethods.SelectObject(hdc, old);
            NativeMethods.DeleteDC(hdc);
            NativeMethods.DeleteObject(hbmp);
        }
        finally
        {
            ctx.Unmap(_stagingTexture, 0);
        }
    }

    private static string Elapsed(long ms)
    {
        long t = ms / 1000;
        return string.Create(CultureInfo.InvariantCulture, $"{t / 60}:{t % 60:00}");
    }

    private static string Num(ulong v) =>
        v >= 1_000_000_000 ? (v / 1e9).ToString("0.00", CultureInfo.InvariantCulture) + "B"
        : v >= 1_000_000 ? (v / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + "M"
        : v >= 1_000 ? (v / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + "K"
        : v.ToString(CultureInfo.InvariantCulture);

    private static string Dps(double v) =>
        v >= 1_000_000 ? (v / 1e6).ToString("0.0", CultureInfo.InvariantCulture) + "M"
        : v >= 1_000 ? (v / 1e3).ToString("0.0", CultureInfo.InvariantCulture) + "K"
        : v.ToString("0", CultureInfo.InvariantCulture);

    public void Dispose()
    {
        _dc.Target = null;
        foreach (IDWriteTextFormat f in new[] { _fmtBrand, _fmtSub, _fmtName, _fmtNum, _fmtSmall, _fmtSummaryL, _fmtSummaryR, _fmtCenter })
            f?.Dispose();
        foreach (ID2D1SolidColorBrush b in new[] { _brPanel, _brText, _brDim, _brSelf, _brGreen, _brSep, _brClose })
            b?.Dispose();
        if (_brRanks is not null) foreach (ID2D1SolidColorBrush b in _brRanks) b?.Dispose();
        _target?.Dispose();
        _rtTexture?.Dispose();
        _stagingTexture?.Dispose();
        _dc?.Dispose();
        _d2dDevice?.Dispose();
        _d3dDevice?.Dispose();
        _dwFactory?.Dispose();
        _d2dFactory?.Dispose();
    }
}
