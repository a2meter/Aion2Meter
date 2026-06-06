using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace A2Meter.Core;

/// Checks GitHub releases for a newer version and hands off install/update work
/// to the AppData installer.
internal static class AutoUpdater
{
    private const string RepoOwner = "a2meter";
    private const string RepoName = "Aion2Meter";
    private const string UpdaterAssetName = "A2Meter_Installer.exe";

    private static readonly string UpdaterDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "A2Meter");
    private static readonly string UpdaterPath = Path.Combine(UpdaterDir, UpdaterAssetName);
    private static readonly string UpdaterTagFile = Path.Combine(UpdaterDir, "updater_tag.txt");
    private static readonly string InstallPathFile = Path.Combine(UpdaterDir, "install_path.txt");

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "A2Meter-AutoUpdater" } },
        Timeout = TimeSpan.FromSeconds(30),
    };

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    /// 본체 시작 시 호출 — 현재 실행 파일 경로를 appdata에 기록.
    /// 업데이터가 본체 exe 위치를 알아내기 위해 사용.
    public static void PersistInstallPath(Action<string>? log = null)
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            Directory.CreateDirectory(UpdaterDir);
            File.WriteAllText(InstallPathFile, exe);
        }
        catch (Exception ex)
        {
            log?.Invoke($"[updater] persist install path failed: {ex.Message}");
        }
    }

    /// 본체 시작 시 호출 — 업데이터가 없거나 태그가 구버전이면 GitHub에서 다운로드.
    /// `UpdaterReleaseTag` 상수를 올리면 다음 실행 시 자동으로 새 업데이터로 교체됨.
    public static async Task EnsureUpdaterAsync(Action<string>? log = null)
    {
        try
        {
            var release = await GetLatestReleaseAsync();
            if (release?.Assets == null || string.IsNullOrWhiteSpace(release.TagName)) return;

            string installedTag = "";
            try
            {
                if (File.Exists(UpdaterTagFile))
                    installedTag = File.ReadAllText(UpdaterTagFile).Trim();
            }
            catch { /* treat as missing */ }

            bool upToDate = File.Exists(UpdaterPath)
                            && string.Equals(installedTag, release.TagName, StringComparison.Ordinal);
            if (upToDate) return;

            log?.Invoke($"[updater] need installer {release.TagName} (installed='{installedTag}'), downloading...");
            Directory.CreateDirectory(UpdaterDir);

            string? updaterUrl = null;
            foreach (var a in release.Assets)
                if (string.Equals(a.Name, UpdaterAssetName, StringComparison.OrdinalIgnoreCase))
                { updaterUrl = a.BrowserDownloadUrl; break; }

            if (updaterUrl == null) { log?.Invoke("[updater] installer asset not found in release"); return; }

            // Download to a temp file first, then atomically replace to avoid leaving a
            // half-written exe if the connection drops mid-stream.
            string tmpPath = UpdaterPath + ".new";
            using (var stream = await Http.GetStreamAsync(updaterUrl))
            using (var fs = File.Create(tmpPath))
                await stream.CopyToAsync(fs);

            if (File.Exists(UpdaterPath)) File.Delete(UpdaterPath);
            File.Move(tmpPath, UpdaterPath);
            File.WriteAllText(UpdaterTagFile, release.TagName);
            log?.Invoke($"[updater] {UpdaterAssetName} ({release.TagName}) deployed to {UpdaterPath}");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[updater] ensure updater failed: {ex.Message}");
        }
    }

    /// Check only — returns (remoteVersion, downloadUrl, releaseNotes) or null.
    public static async Task<(Version Version, string Url, string Notes)?> CheckAsync(Action<string>? log = null)
    {
        try
        {
            log?.Invoke("[updater] checking for updates...");
            var release = await GetLatestReleaseAsync();
            if (release == null) return null;

            var remoteVer = ParseTag(release.TagName);
            if (remoteVer == null) return null;

            if (remoteVer <= CurrentVersion)
            {
                log?.Invoke($"[updater] up to date ({CurrentVersion})");
                return null;
            }

            string? downloadUrl = null;
            if (release.Assets != null)
                foreach (var a in release.Assets)
                    if (string.Equals(a.Name, UpdaterAssetName, StringComparison.OrdinalIgnoreCase))
                    { downloadUrl = a.BrowserDownloadUrl; break; }

            if (downloadUrl == null) { log?.Invoke("[updater] installer asset not found"); return null; }

            log?.Invoke($"[updater] update available: {remoteVer}");
            return (remoteVer, downloadUrl, release.Body ?? "");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[updater] check error: {ex.Message}");
            return null;
        }
    }

    /// 업데이터를 실행. 업데이터가 GitHub 재확인 → 사용자 확인 → 교체까지 처리.
    /// 호출 후 본체는 즉시 종료해야 함.
    public static async Task<bool> LaunchInstallerIfOutdatedAsync(Action<string>? log = null)
    {
        await EnsureUpdaterAsync(log);

        var update = await CheckAsync(log);
        if (!update.HasValue) return false;

        log?.Invoke($"[updater] forcing update to {update.Value.Version}");
        return LaunchUpdaterAndExit(log);
    }

    public static bool LaunchUpdaterAndExit(Action<string>? log = null)
    {
        var pid = Environment.ProcessId;

        if (!File.Exists(UpdaterPath))
        {
            log?.Invoke($"[updater] {UpdaterAssetName} not found - cannot update");
            return false;
        }

        // Ensure install_path.txt is up-to-date for the updater.
        PersistInstallPath(log);

        log?.Invoke($"[updater] launching A2Meter_Installer (pid={pid})");

        Process.Start(new ProcessStartInfo
        {
            FileName = UpdaterPath,
            Arguments = $"--pid {pid}",
            UseShellExecute = false,
            CreateNoWindow = true,
        });

        return true;
    }

    private static async Task<GitHubRelease?> GetLatestReleaseAsync()
    {
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        var resp = await Http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<GitHubRelease>();
    }

    private static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrEmpty(tag)) return null;
        var s = tag.TrimStart('v', 'V');
        return Version.TryParse(s, out var v) ? v : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]  public string? TagName { get; set; }
        [JsonPropertyName("body")]      public string? Body { get; set; }
        [JsonPropertyName("assets")]    public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]                  public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")]  public string? BrowserDownloadUrl { get; set; }
    }
}
