using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;

namespace A2Meter.Forms;

internal static class TierIconCache
{
    private static readonly ConcurrentDictionary<string, Image?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Image? Get(string? tier)
    {
        var normalized = Normalize(tier);
        return Cache.GetOrAdd(normalized, Load);
    }

    public static string Ko(string? tier) => Normalize(tier) switch
    {
        "Grandmaster" => "그랜드마스터",
        "Master"      => "마스터",
        "Diamond"     => "다이아",
        "Platinum"    => "플래티넘",
        "Gold"        => "골드",
        "Silver"      => "실버",
        "Bronze"      => "브론즈",
        "Iron"        => "아이언",
        _             => "없음",
    };

    private static string Normalize(string? tier)
        => string.IsNullOrWhiteSpace(tier) ? "None" : tier.Trim();

    private static Image? Load(string tier)
    {
        string file = tier switch
        {
            "Grandmaster" => "UT_Arena_Ranking_Grade_GrandMaster.png",
            "Master"      => "UT_Arena_Ranking_Grade_Master.png",
            "Diamond"     => "UT_Arena_Ranking_Grade_Diamond.png",
            "Platinum"    => "UT_Arena_Ranking_Grade_Platinum.png",
            "Gold"        => "UT_Arena_Ranking_Grade_Gold.png",
            "Silver"      => "UT_Arena_Ranking_Grade_Silver.png",
            "Bronze"      => "UT_Arena_Ranking_Grade_Bronze.png",
            "Iron"        => "UT_Arena_Ranking_Grade_Iron.png",
            _             => "UT_Arena_Ranking_Grade_None.png",
        };

        string path = Path.Combine(AppContext.BaseDirectory, "Tier_Img", file);
        if (!File.Exists(path))
            path = Path.Combine(AppContext.BaseDirectory, "..", "Tier_Img", file);
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var img = Image.FromStream(stream);
            return new Bitmap(img);
        }
        catch
        {
            return null;
        }
    }
}
