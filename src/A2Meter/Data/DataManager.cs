using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace A2Meter.Data;

/// Downloads game data files from CDN to AppData on first launch.
/// Subsequent launches reuse the cached files.
internal static class DataManager
{
    private static readonly string[] CdnBases =
    {
        "https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@v1.0.1/Assets/Game/",
        "https://raw.githubusercontent.com/a2meter/a2meter.github.io/v1.0.1/Assets/Game/",
        "https://www.aion2meter.com/data/",
    };
    private static readonly string[] IconCdnBases =
    {
        "https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@v1.0.1/Assets/Icon/Job/",
        "https://raw.githubusercontent.com/a2meter/a2meter.github.io/v1.0.1/Assets/Icon/Job/",
        "https://www.aion2meter.com/data/icons/",
    };

    private static readonly string[] RequiredFiles = { "game_db.sqlite" };
    private static readonly string[] JobIconFiles =
    {
        "검성.png", "궁성.png", "마도성.png", "살성.png",
        "수호성.png", "정령성.png", "치유성.png", "호법성.png",
    };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };


    /// AppData directory where game data is stored.
    public static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "Data");

    /// AppData directory where job icons are cached.
    public static readonly string JobIconDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter", "job_icons");

    /// Full path to the SQLite database.
    public static string DatabasePath => Path.Combine(DataDir, "game_db.sqlite");

    /// Returns true if all required data files are present (DB + icons).
    public static bool IsReady => File.Exists(DatabasePath) && JobIconsReady;

    /// Returns true if all job icon PNGs are cached.
    public static bool JobIconsReady
    {
        get
        {
            foreach (var f in JobIconFiles)
                if (!File.Exists(Path.Combine(JobIconDir, f))) return false;
            return true;
        }
    }

    /// Downloads missing data files + job icons. Safe to call multiple times.
    /// Returns true if all files are available after the call.
    public static async Task<bool> EnsureDataAsync(Action<string>? progress = null)
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(JobIconDir);

        // DB files.
        foreach (var file in RequiredFiles)
        {
            var localPath = Path.Combine(DataDir, file);
            if (File.Exists(localPath)) continue;

            progress?.Invoke($"다운로드 중: {file}...");
            bool downloaded = false;

            foreach (var cdnBase in CdnBases)
            {
                var url = cdnBase + file;

                try
                {
                    var tempPath = localPath + ".tmp";
                    using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
    
                        resp.EnsureSuccessStatusCode();
                        using var fs = File.Create(tempPath);
                        await resp.Content.CopyToAsync(fs);
                    }
                    File.Move(tempPath, localPath, overwrite: true);

                    progress?.Invoke($"완료: {file}");
                    downloaded = true;
                    break;
                }
                catch (Exception ex)
                {

                    progress?.Invoke($"실패({url}): {ex.Message}");
                    var tempPath = localPath + ".tmp";
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }

            if (!downloaded)
            {

                return false;
            }
        }

        // Job icons (best-effort — failures don't block startup).
        await DownloadJobIconsAsync(progress);

        return File.Exists(DatabasePath);
    }

    /// Downloads with byte-level progress reporting (for UI progress bars).
    /// progressBytes: (bytesDownloaded, totalBytes) — totalBytes may be 0 if unknown.
    public static async Task<bool> EnsureDataWithProgressAsync(Action<long, long>? progressBytes = null)
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(JobIconDir);

        // DB files (with byte-level progress).
        foreach (var file in RequiredFiles)
        {
            var localPath = Path.Combine(DataDir, file);
            if (File.Exists(localPath)) continue;

            bool downloaded = false;
            foreach (var cdnBase in CdnBases)
            {
                var url = cdnBase + file;

                try
                {
                    var tempPath = localPath + ".tmp";
                    long dl = 0;
                    using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
    
                        resp.EnsureSuccessStatusCode();

                        long totalBytes = resp.Content.Headers.ContentLength ?? 0;

                        using (var source = await resp.Content.ReadAsStreamAsync())
                        using (var fs = File.Create(tempPath))
                        {
                            var buffer = new byte[81920];
                            int bytesRead;
                            while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fs.WriteAsync(buffer, 0, bytesRead);
                                dl += bytesRead;
                                progressBytes?.Invoke(dl, totalBytes);
                            }
                        }
                    }
                    File.Move(tempPath, localPath, overwrite: true);

                    downloaded = true;
                    break;
                }
                catch (Exception ex)
                {

                    var tempPath = localPath + ".tmp";
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
            }

            if (!downloaded) return false;
        }

        // Job icons (best-effort).
        await DownloadJobIconsAsync(null);

        return File.Exists(DatabasePath);
    }

    /// Downloads missing job icon PNGs from CDN. Best-effort; partial failures are fine.
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
                    if (bytes.Length > 100) // skip error pages
                    {
                        await File.WriteAllBytesAsync(localPath, bytes);
                        break;
                    }
                }
                catch
                {
                    // Try next CDN.
                }
            }
        }
    }

    /// Force re-download all data files (e.g., after version update).
    public static async Task<bool> UpdateAsync(Action<string>? progress = null)
    {
        foreach (var file in RequiredFiles)
        {
            var localPath = Path.Combine(DataDir, file);
            if (File.Exists(localPath)) File.Delete(localPath);
        }
        return await EnsureDataAsync(progress);
    }

    private const string VersionUrl = "https://api.aion2meter.com/api/gamedata/version";
    private static readonly string HashPath = Path.Combine(DataDir, "game_db.hash");

    /// Checks remote version and re-downloads if hash differs. Best-effort.
    public static async Task CheckForUpdateAsync()
    {
        try
        {
            if (!File.Exists(DatabasePath)) return;

            var json = await Http.GetStringAsync(VersionUrl);
            using var doc = JsonDocument.Parse(json);
            var remoteHash = doc.RootElement.GetProperty("hash").GetString() ?? "";
            if (string.IsNullOrEmpty(remoteHash)) return;

            var localHash = GetLocalHash();
            if (string.Equals(localHash, remoteHash, StringComparison.OrdinalIgnoreCase)) return;

            // Hash differs — re-download.
            File.Delete(DatabasePath);
            if (File.Exists(HashPath)) File.Delete(HashPath);
            await EnsureDataAsync(null);

            // Save new hash.
            SaveLocalHash();
        }
        catch
        {
            // Best-effort — don't block startup.
        }
    }

    private static string GetLocalHash()
    {
        // Use cached hash file if available.
        if (File.Exists(HashPath))
        {
            var cached = File.ReadAllText(HashPath).Trim();
            if (cached.Length == 64) return cached;
        }

        if (!File.Exists(DatabasePath)) return "";

        var hash = ComputeSha256(DatabasePath);
        try { File.WriteAllText(HashPath, hash); } catch { }
        return hash;
    }

    private static void SaveLocalHash()
    {
        if (!File.Exists(DatabasePath)) return;
        try { File.WriteAllText(HashPath, ComputeSha256(DatabasePath)); } catch { }
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var bytes = sha.ComputeHash(fs);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}
