// A2Updater — 본체(A2Meter.exe) 종료 대기 → GitHub 재확인 → 사용자 확인 → 교체.
//
// Usage:
//   A2Updater.exe --pid 1234
//
// 동작:
//   1. install_path.txt에서 본체 위치 읽기
//   2. PID 종료 대기 (최대 10s)
//   3. GitHub releases 최신 버전 확인
//   4. 현재 본체 버전과 비교
//   5. 신버전이면 MessageBox로 묻기
//      - Yes: 본체 삭제 → 다운로드 → 재실행
//      - No : 본체 그대로 재실행
//   6. 동일 버전이거나 확인 실패 시: 본체 그대로 재실행

using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace A2Updater;

internal static class Program
{
    private const string RepoOwner = "a2meter";
    private const string RepoName = "Aion2Meter";
    private const string AssetName = "A2Meter.exe";

    private static readonly string InstallPathFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "A2Meter", "install_path.txt");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_YESNO = 0x00000004;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_TOPMOST = 0x00040000;
    private const int IDYES = 6;

    private static async Task<int> Main(string[] args)
    {
        int pid = 0;
        for (int i = 0; i < args.Length; i++)
            if (args[i] == "--pid" && i + 1 < args.Length) int.TryParse(args[++i], out pid);

        // 1. 본체 설치 경로 읽기
        string target = ReadInstallPath();
        if (string.IsNullOrEmpty(target) || !File.Exists(target))
        {
            Console.Error.WriteLine($"[A2Updater] install path invalid: {target}");
            return 1;
        }

        // 2. PID 종료 대기
        WaitForExit(pid);

        // 3. GitHub 재확인
        var latest = await CheckLatestAsync();
        var currentVer = GetFileVersion(target);

        bool shouldUpdate = false;
        if (latest != null && currentVer != null && latest.Version > currentVer)
        {
            // 4. 사용자 확인
            int rc = MessageBoxW(IntPtr.Zero,
                $"새 버전이 있습니다.\n\n현재: v{currentVer}\n최신: v{latest.Version}\n\n업데이트하시겠습니까?",
                "A2Meter 업데이트",
                MB_YESNO | MB_ICONQUESTION | MB_TOPMOST);
            shouldUpdate = rc == IDYES;
        }

        if (shouldUpdate && latest != null)
        {
            try
            {
                // 5-Yes. 본체 삭제 → 다운로드 → 재실행
                Console.WriteLine($"[A2Updater] removing old: {target}");
                DeleteWithRetry(target);

                Console.WriteLine($"[A2Updater] downloading: {latest.Url}");
                await DownloadAsync(latest.Url, target);
                Console.WriteLine($"[A2Updater] download complete");
            }
            catch (Exception ex)
            {
                MessageBoxW(IntPtr.Zero,
                    $"업데이트 실패: {ex.Message}\n\n수동으로 GitHub에서 다시 받아주세요.",
                    "A2Meter 업데이트", MB_ICONQUESTION | MB_TOPMOST);
                return 2;
            }
        }
        else
        {
            Console.WriteLine("[A2Updater] no update / declined — relaunching existing meter");
        }

        // 6. 본체 재실행 (성공/거절/체크실패 모두 공통)
        if (File.Exists(target))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(target),
            });
        }

        return 0;
    }

    private static string ReadInstallPath()
    {
        try
        {
            if (!File.Exists(InstallPathFile)) return "";
            return File.ReadAllText(InstallPathFile).Trim();
        }
        catch { return ""; }
    }

    private static void WaitForExit(int pid)
    {
        if (pid <= 0) return;
        try
        {
            var proc = Process.GetProcessById(pid);
            proc.WaitForExit(10_000);
        }
        catch (ArgumentException) { /* already exited */ }
    }

    private static Version? GetFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (string.IsNullOrEmpty(info.FileVersion)) return null;
            // FileVersion may contain 4-part with revision; trim to 3 for comparison stability.
            return Version.TryParse(info.FileVersion, out var v) ? v : null;
        }
        catch { return null; }
    }

    private static async Task<LatestRelease?> CheckLatestAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.Add("User-Agent", "A2Updater");

            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var release = await http.GetFromJsonAsync<GitHubRelease>(url);
            if (release?.TagName == null) return null;

            string tag = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var ver)) return null;

            if (release.Assets == null) return null;
            foreach (var a in release.Assets)
            {
                if (string.Equals(a.Name, AssetName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(a.BrowserDownloadUrl))
                {
                    return new LatestRelease(ver, a.BrowserDownloadUrl);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[A2Updater] github check failed: {ex.Message}");
            return null;
        }
    }

    private static void DeleteWithRetry(string path)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (!File.Exists(path)) return;
            }
            catch (IOException) when (attempt < 19) { }
            Thread.Sleep(500);
        }
        if (File.Exists(path)) throw new IOException($"could not delete {path}");
    }

    private static async Task DownloadAsync(string url, string targetPath)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.Add("User-Agent", "A2Updater");

        var bytes = await http.GetByteArrayAsync(url);
        await File.WriteAllBytesAsync(targetPath, bytes);
    }

    private sealed record LatestRelease(Version Version, string Url);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")]   public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]                 public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
