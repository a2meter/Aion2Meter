using System.Diagnostics;
using Microsoft.Win32;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A2Updater;

internal static class Program
{
    private const string AppName = "A2Meter";
    private const string RepoOwner = "a2meter";
    private const string RepoName = "Aion2Meter";
    private const string AssetName = "A2Meter.exe";
    private const string UninstallerAssetName = "uninstall.exe";
    private const string DefaultGameDataTag = "v1.0.1";
    private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\A2Meter";

    private static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    private static readonly string TargetPath = Path.Combine(InstallDir, "A2Meter.exe");
    private static readonly string InstallerPath = Path.Combine(InstallDir, "A2Meter_Installer.exe");
    private static readonly string UninstallerPath = Path.Combine(InstallDir, UninstallerAssetName);
    private static readonly string InstallPathFile = Path.Combine(InstallDir, "install_path.txt");
    private static readonly string DataDir = Path.Combine(InstallDir, "Data");
    private static readonly string GameDbPath = Path.Combine(DataDir, "game_db.sqlite");
    private static readonly string GameDbHashPath = Path.Combine(DataDir, "game_db.hash");
    private static readonly string JobIconDir = Path.Combine(InstallDir, "job_icons");

    private static readonly string[] VersionUrls =
    {
        "https://www.aion2meter.com/data/version.json",
        "https://api.aion2meter.com/api/gamedata/version",
        "https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@main/Assets/Game/version.json",
        "https://raw.githubusercontent.com/a2meter/a2meter.github.io/main/Assets/Game/version.json",
    };

    private static readonly string[] JobIconBases =
    {
        "https://www.aion2meter.com/data/icons/",
        "https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@main/Assets/Icon/Job/",
        "https://raw.githubusercontent.com/a2meter/a2meter.github.io/main/Assets/Icon/Job/",
        "https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@v1.0.1/Assets/Icon/Job/",
        "https://raw.githubusercontent.com/a2meter/a2meter.github.io/v1.0.1/Assets/Icon/Job/",
    };

    private static readonly string[] JobIconFiles =
    {
        "검성.png", "궁성.png", "마도성.png", "살성.png",
        "수호성.png", "정령성.png", "치유성.png", "호법성.png",
    };

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(5),
        DefaultRequestHeaders = { { "User-Agent", "A2Updater" } },
    };
    private static int _exitCode = 1;

    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var form = new InstallerProgressForm();
        form.Shown += async (_, _) =>
        {
            _exitCode = await MainAsync(args, form);
            if (_exitCode == 0)
            {
                await Task.Delay(1200);
                form.CloseAfterComplete();
            }
        };

        Application.Run(form);
        return _exitCode;
    }

    private static async Task<int> MainAsync(string[] args, InstallerProgressForm progress)
    {
        try
        {
            var pid = ParsePid(args);
            progress.SetStep(2, pid > 0 ? "기존 A2Meter 종료 대기 중..." : "설치 준비 중...");
            await Task.Run(() => WaitForExit(pid));

            progress.SetStep(6, "설치 폴더 준비 중...");
            Directory.CreateDirectory(InstallDir);
            Directory.CreateDirectory(DataDir);
            Directory.CreateDirectory(JobIconDir);

            progress.SetStep(12, "최신 A2Meter 릴리스 확인 중...");
            var latest = await CheckLatestAsync()
                ?? throw new InvalidOperationException("최신 A2Meter 릴리즈를 찾지 못했습니다.");

            progress.SetStep(18, "인스톨러 파일 보관 중...");
            CopySelfToInstallDir();

            progress.SetStep(28, $"A2Meter v{latest.Version} 다운로드 중...");
            await DownloadFileAsync(latest.Url, TargetPath, progress: progress.SetCurrentProgress);

            progress.SetStep(42, "제거 프로그램 다운로드 중...");
            await DownloadFileAsync(latest.UninstallerUrl, UninstallerPath, progress: progress.SetCurrentProgress);

            progress.SetStep(48, "설치 경로 저장 중...");
            File.WriteAllText(InstallPathFile, TargetPath);

            await EnsureGameDataAsync(progress);
            await EnsureJobIconsAsync(progress);

            progress.SetStep(84, "바탕화면 및 시작 메뉴 바로가기 생성 중...");
            CreateShortcuts();

            progress.SetStep(90, "프로그램 추가/제거 등록 중...");
            RegisterUninstallEntry(latest.Version);

            progress.SetStep(96, "A2Meter 실행 중...");
            LaunchInstalledMeter();
            progress.Complete(true, "설치가 완료되었습니다. A2Meter를 실행합니다.");

            return 0;
        }
        catch (Exception ex)
        {
            progress.Complete(false, $"설치 실패: {ex.Message}");
            MessageBox.Show(
                progress,
                $"A2Meter 설치/업데이트에 실패했습니다.\n\n{ex.Message}",
                "A2Meter 설치",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int ParsePid(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--pid", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Length
                && int.TryParse(args[i + 1], out var pid))
            {
                return pid;
            }
        }

        return 0;
    }

    private static void WaitForExit(int pid)
    {
        if (pid <= 0) return;

        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            // Already exited.
        }
    }

    private static async Task<LatestRelease?> CheckLatestAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var release = await Http.GetFromJsonAsync<GitHubRelease>(url);
            if (release?.TagName == null || release.Assets == null) return null;

            var tag = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tag, out var version)) return null;

            string? meterUrl = null;
            string? uninstallerUrl = null;
            foreach (var asset in release.Assets)
            {
                if (string.Equals(asset.Name, AssetName, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                {
                    meterUrl = asset.BrowserDownloadUrl;
                }
                else if (string.Equals(asset.Name, UninstallerAssetName, StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
                {
                    uninstallerUrl = asset.BrowserDownloadUrl;
                }
            }

            if (!string.IsNullOrWhiteSpace(meterUrl) && !string.IsNullOrWhiteSpace(uninstallerUrl))
                return new LatestRelease(version, meterUrl, uninstallerUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[A2Updater] latest check failed: {ex.Message}");
        }

        return null;
    }

    private static async Task EnsureGameDataAsync(InstallerProgressForm progress)
    {
        progress.SetStep(54, "게임 데이터 버전 확인 중...");
        var version = await TryGetGameDataVersionAsync();
        Exception? lastError = null;

        foreach (var url in BuildGameDbUrls(version?.Tag))
        {
            try
            {
                progress.SetStep(58, "게임 데이터 다운로드 중...");
                await DownloadFileAsync(url, GameDbPath, version?.Hash, progress.SetCurrentProgress);
                var hash = version?.Hash ?? ComputeSha256(GameDbPath);
                File.WriteAllText(GameDbHashPath, hash);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("게임 데이터 다운로드에 실패했습니다.", lastError);
    }

    private static async Task<GameDataVersion?> TryGetGameDataVersionAsync()
    {
        foreach (var url in VersionUrls)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var json = await Http.GetStringAsync(url, cts.Token);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("hash", out var hashElement)) continue;

                var hash = hashElement.GetString()?.Trim() ?? "";
                if (hash.Length != 64) continue;

                var tag = doc.RootElement.TryGetProperty("tag", out var tagElement)
                    ? tagElement.GetString()?.Trim()
                    : null;

                return new GameDataVersion(
                    string.IsNullOrWhiteSpace(tag) ? DefaultGameDataTag : tag,
                    hash.ToLowerInvariant());
            }
            catch
            {
                // Try the next endpoint.
            }
        }

        return null;
    }

    private static IEnumerable<string> BuildGameDbUrls(string? tag)
    {
        var resolvedTag = string.IsNullOrWhiteSpace(tag) ? "main" : tag.Trim();

        yield return "https://www.aion2meter.com/data/game_db.sqlite";
        yield return $"https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@{resolvedTag}/Assets/Game/game_db.sqlite";
        yield return $"https://raw.githubusercontent.com/a2meter/a2meter.github.io/{resolvedTag}/Assets/Game/game_db.sqlite";

        if (!string.Equals(resolvedTag, DefaultGameDataTag, StringComparison.OrdinalIgnoreCase))
        {
            yield return $"https://cdn.jsdelivr.net/gh/a2meter/a2meter.github.io@{DefaultGameDataTag}/Assets/Game/game_db.sqlite";
            yield return $"https://raw.githubusercontent.com/a2meter/a2meter.github.io/{DefaultGameDataTag}/Assets/Game/game_db.sqlite";
        }
    }

    private static async Task EnsureJobIconsAsync(InstallerProgressForm progress)
    {
        for (int i = 0; i < JobIconFiles.Length; i++)
        {
            var file = JobIconFiles[i];
            progress.SetStep(66 + (i * 16 / Math.Max(1, JobIconFiles.Length)), $"직업 아이콘 확인 중... ({i + 1}/{JobIconFiles.Length})");
            var target = Path.Combine(JobIconDir, file);
            if (File.Exists(target)) continue;

            foreach (var iconBase in JobIconBases)
            {
                try
                {
                    progress.SetDetail(file);
                    await DownloadFileAsync(iconBase + Uri.EscapeDataString(file), target, progress: progress.SetCurrentProgress);
                    if (new FileInfo(target).Length > 100) break;
                    File.Delete(target);
                }
                catch
                {
                    // Try the next mirror; icons are not worth failing the install.
                }
            }
        }
    }

    private static async Task DownloadFileAsync(
        string url,
        string targetPath,
        string? expectedSha256 = null,
        Action<long, long>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        var tempPath = targetPath + ".new";
        if (File.Exists(tempPath)) File.Delete(tempPath);

        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            var totalBytes = resp.Content.Headers.ContentLength ?? 0;
            progress?.Invoke(0, totalBytes);

            await using (var source = await resp.Content.ReadAsStreamAsync())
            await using (var target = File.Create(tempPath))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                while (true)
                {
                    int read = await source.ReadAsync(buffer);
                    if (read == 0) break;

                    await target.WriteAsync(buffer.AsMemory(0, read));
                    downloaded += read;
                    progress?.Invoke(downloaded, totalBytes);
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256)
                && !string.Equals(ComputeSha256(tempPath), expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"다운로드 파일 hash가 맞지 않습니다: {url}");
            }

            DeleteWithRetry(targetPath);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
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
            catch (IOException) when (attempt < 19)
            {
            }
            catch (UnauthorizedAccessException) when (attempt < 19)
            {
            }

            Thread.Sleep(500);
        }

        if (File.Exists(path)) throw new IOException($"파일을 교체할 수 없습니다: {path}");
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private static void CreateShortcuts()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var startMenuDir = Path.Combine(programs, AppName);

        CreateShortcut(Path.Combine(desktop, $"{AppName}.lnk"));
        CreateShortcut(Path.Combine(startMenuDir, $"{AppName}.lnk"));
    }

    private static void RegisterUninstallEntry(Version version)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallRegistryPath);
        if (key == null) return;

        key.SetValue("DisplayName", AppName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", version.ToString(), RegistryValueKind.String);
        key.SetValue("Publisher", "a2meter", RegistryValueKind.String);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
        key.SetValue("InstallLocation", InstallDir, RegistryValueKind.String);
        key.SetValue("DisplayIcon", TargetPath, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{UninstallerPath}\"", RegistryValueKind.String);
        key.SetValue("URLInfoAbout", "https://aion2meter.com", RegistryValueKind.String);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

        var estimatedSizeKb = GetDirectorySize(InstallDir) / 1024;
        if (estimatedSizeKb > 0 && estimatedSizeKb <= int.MaxValue)
            key.SetValue("EstimatedSize", (int)estimatedSizeKb, RegistryValueKind.DWord);
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Sum(file =>
                {
                    try { return new FileInfo(file).Length; }
                    catch { return 0L; }
                });
        }
        catch
        {
            return 0;
        }
    }

    private static void CopySelfToInstallDir()
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(self)) return;
        if (string.Equals(Path.GetFullPath(self), Path.GetFullPath(InstallerPath), StringComparison.OrdinalIgnoreCase)) return;

        File.Copy(self, InstallerPath, overwrite: true);
    }

    private static void CreateShortcut(string shortcutPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                ?? throw new InvalidOperationException("WScript.Shell을 사용할 수 없습니다.");

            shell = Activator.CreateInstance(shellType);
            dynamic shellDispatch = shell!;
            shortcut = shellDispatch.CreateShortcut(shortcutPath);

            dynamic link = shortcut;
            link.TargetPath = TargetPath;
            link.WorkingDirectory = InstallDir;
            link.IconLocation = TargetPath;
            link.Description = AppName;
            link.Save();
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj == null) return;
        try
        {
            if (Marshal.IsComObject(obj))
                Marshal.FinalReleaseComObject(obj);
        }
        catch
        {
        }
    }

    private static void LaunchInstalledMeter()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = TargetPath,
            WorkingDirectory = InstallDir,
            UseShellExecute = true,
        });
    }

    private sealed record LatestRelease(Version Version, string Url, string UninstallerUrl);
    private sealed record GameDataVersion(string Tag, string Hash);

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("assets")] public GitHubAsset[]? Assets { get; set; }
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}
