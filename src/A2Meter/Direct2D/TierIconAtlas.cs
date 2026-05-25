using System;
using System.Collections.Generic;
using System.IO;
using Vortice.Direct2D1;
using Vortice.WIC;
using WicPixelFormat = Vortice.WIC.PixelFormat;

namespace A2Meter.Direct2D;

/// Loads bundled tier PNGs into D2D bitmaps for the overlay lookup tab.
internal sealed class TierIconAtlas : IDisposable
{
    private readonly Dictionary<string, ID2D1Bitmap1> _bitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly IWICImagingFactory _wic;

    private static readonly Dictionary<string, string> FileByTier = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Challenger1"] = "UT_Arena_Ranking_Grade_Challenger_01.png",
        ["Challenger2"] = "UT_Arena_Ranking_Grade_Challenger_02.png",
        ["Challenger3"] = "UT_Arena_Ranking_Grade_Challenger_03.png",
        ["Challenger"]  = "UT_Arena_Ranking_Grade_Challenger_01.png",
        ["Grandmaster"] = "UT_Arena_Ranking_Grade_GrandMaster.png",
        ["Master"]      = "UT_Arena_Ranking_Grade_Master.png",
        ["Diamond"]     = "UT_Arena_Ranking_Grade_Diamond.png",
        ["Platinum"]    = "UT_Arena_Ranking_Grade_Platinum.png",
        ["Gold"]        = "UT_Arena_Ranking_Grade_Gold.png",
        ["Silver"]      = "UT_Arena_Ranking_Grade_Silver.png",
        ["Bronze"]      = "UT_Arena_Ranking_Grade_Bronze.png",
        ["Iron"]        = "UT_Arena_Ranking_Grade_Iron.png",
        ["Unranked"]    = "UT_Arena_Ranking_Grade_None.png",
        ["None"]        = "UT_Arena_Ranking_Grade_None.png",
    };

    public TierIconAtlas(ID2D1DeviceContext dc)
    {
        _wic = new IWICImagingFactory();

        var dirs = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Tier_Img"),
            Path.Combine(AppContext.BaseDirectory, "..", "Tier_Img"),
        };

        foreach (var pair in FileByTier)
        {
            foreach (var dir in dirs)
            {
                var path = Path.Combine(dir, pair.Value);
                if (!File.Exists(path)) continue;

                try
                {
                    var bmp = LoadBitmap(dc, path);
                    if (bmp != null) _bitmaps[pair.Key] = bmp;
                    break;
                }
                catch { /* missing/corrupt tier icon falls back to text only */ }
            }
        }
    }

    public ID2D1Bitmap1? Get(string? tier)
    {
        var key = string.IsNullOrWhiteSpace(tier) ? "None" : tier.Trim();
        return _bitmaps.TryGetValue(key, out var b) ? b : null;
    }

    private ID2D1Bitmap1? LoadBitmap(ID2D1DeviceContext dc, string path)
    {
        using var decoder = _wic.CreateDecoderFromFileName(path);
        using var frame = decoder.GetFrame(0);
        using var conv = _wic.CreateFormatConverter();
        conv.Initialize(frame, WicPixelFormat.Format32bppPBGRA,
                        BitmapDitherType.None, null, 0.0, BitmapPaletteType.Custom);
        return dc.CreateBitmapFromWicBitmap(conv, null);
    }

    public void Dispose()
    {
        foreach (var b in _bitmaps.Values) b.Dispose();
        _bitmaps.Clear();
        _wic.Dispose();
    }
}
