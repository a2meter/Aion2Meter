using System;
using System.Collections.Generic;
using System.IO;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.WIC;
using D2DAlphaMode = Vortice.DCommon.AlphaMode;
using WicPixelFormat = Vortice.WIC.PixelFormat;

namespace A2Meter.Direct2D;

/// Loads the eight job-icon PNGs into ID2D1Bitmap1 keyed by Korean job name.
/// Icons are cached under %APPDATA%/A2Meter/job_icons/ (downloaded from CDN).
/// Falls back to bundled Assets/Icons if present.
internal sealed class JobIconAtlas : IDisposable
{
    private readonly Dictionary<string, ID2D1Bitmap1> _bitmaps = new();
    private readonly IWICImagingFactory _wic;

    private static readonly string CacheDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "job_icons");

    public JobIconAtlas(ID2D1DeviceContext dc)
    {
        _wic = new IWICImagingFactory();

        // Primary: cached icons from CDN.
        if (Directory.Exists(CacheDir))
            LoadFromDir(dc, CacheDir);

        // Fallback: bundled assets (for offline / first-run before download).
        if (_bitmaps.Count == 0)
        {
            var assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Icons");
            if (Directory.Exists(assetsDir))
                LoadFromDir(dc, assetsDir);
        }
    }

    private void LoadFromDir(ID2D1DeviceContext dc, string dir)
    {
        foreach (var path in Directory.EnumerateFiles(dir, "*.png"))
        {
            var key = Path.GetFileNameWithoutExtension(path);
            if (_bitmaps.ContainsKey(key)) continue;
            try
            {
                var bmp = LoadBitmap(dc, path);
                if (bmp != null) _bitmaps[key] = bmp;
            }
            catch { /* missing/corrupt asset just shows the colored-dot fallback */ }
        }
    }

    public ID2D1Bitmap1? Get(string jobName)
        => _bitmaps.TryGetValue(jobName, out var b) ? b : null;

    private ID2D1Bitmap1? LoadBitmap(ID2D1DeviceContext dc, string path)
    {
        using var decoder = _wic.CreateDecoderFromFileName(path);
        using var frame   = decoder.GetFrame(0);
        using var conv    = _wic.CreateFormatConverter();
        conv.Initialize(frame, WicPixelFormat.Format32bppPBGRA,
                        BitmapDitherType.None, null, 0.0, BitmapPaletteType.Custom);
        // Let D2D infer pixel format / alpha mode from the WIC source.
        return dc.CreateBitmapFromWicBitmap(conv, null);
    }

    public void Dispose()
    {
        foreach (var b in _bitmaps.Values) b.Dispose();
        _bitmaps.Clear();
        _wic.Dispose();
    }
}
