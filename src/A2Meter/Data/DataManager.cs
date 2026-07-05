using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace A2Meter.Data;

/// Manages local assets that still need disk caching. Static game data is loaded
/// through GameDataClient, which keeps an AppData bootstrap cache for offline startup.
internal static class DataManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private static readonly string[] IconCdnBases =
    {
        "https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@v1.0.1/Assets/Icon/Job/",
        "https://raw.githubusercontent.com/a2meter/a2meter.github.io/v1.0.1/Assets/Icon/Job/",
        "https://www.aion2meter.com/data/icons/",
    };

    private static readonly string[] JobIconFiles =
    {
        "검성.png", "궁성.png", "마도성.png", "살성.png",
        "수호성.png", "정령성.png", "치유성.png", "호법성.png",
        "권성.png",
    };

    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "Data");

    public static readonly string JobIconDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "job_icons");

    /// Kept for compatibility with older callers; new meter game data does not use this file.
    public static string DatabasePath => Path.Combine(DataDir, "game_db.sqlite");

    public static bool IsReady => JobIconsReady;

    public static bool JobIconsReady
    {
        get
        {
            foreach (var f in JobIconFiles)
                if (!File.Exists(Path.Combine(JobIconDir, f))) return false;
            return true;
        }
    }

    public static async Task<bool> EnsureDataAsync(Action<string>? progress = null)
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(JobIconDir);
        await DownloadJobIconsAsync(progress);
        return JobIconsReady;
    }

    public static async Task<bool> EnsureDataWithProgressAsync(Action<long, long>? progressBytes = null)
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(JobIconDir);
        await DownloadJobIconsAsync(null);
        progressBytes?.Invoke(JobIconsReady ? 1 : 0, 1);
        return JobIconsReady;
    }

    public static async Task<bool> UpdateAsync(Action<string>? progress = null)
        => await EnsureDataAsync(progress);

    public static async Task CheckForUpdateAsync()
        => await Task.CompletedTask;

    private static async Task DownloadJobIconsAsync(Action<string>? progress)
    {
        foreach (var file in JobIconFiles)
        {
            var localPath = Path.Combine(JobIconDir, file);
            if (File.Exists(localPath)) continue;

            progress?.Invoke($"아이콘 다운로드: {file}");
            foreach (var iconBase in IconCdnBases)
            {
                try
                {
                    var url = iconBase + Uri.EscapeDataString(file);
                    var bytes = await Http.GetByteArrayAsync(url);
                    if (bytes.Length <= 100) continue;

                    await File.WriteAllBytesAsync(localPath, bytes);
                    break;
                }
                catch
                {
                    // Try next CDN.
                }
            }
        }
    }
}
