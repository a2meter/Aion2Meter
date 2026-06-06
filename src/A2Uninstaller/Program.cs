using System.Diagnostics;
using Microsoft.Win32;

namespace A2Uninstaller;

internal static class Program
{
    private const string AppName = "A2Meter";
    private const string UninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\A2Meter";

    private static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppName);

    private static readonly string TargetPath = Path.Combine(InstallDir, "A2Meter.exe");
    private static readonly string InstallerPath = Path.Combine(InstallDir, "A2Meter_Installer.exe");
    private static readonly string UninstallerPath = Path.Combine(InstallDir, "uninstall.exe");

    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new UninstallForm());
    }

    private sealed class UninstallForm : Form
    {
        private readonly CheckBox _deleteAllData;
        private readonly Button _removeButton;
        private readonly Button _cancelButton;

        public UninstallForm()
        {
            Text = "A2Meter 제거";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(440, 210);
            Font = new Font("Segoe UI", 9f);

            var title = new Label
            {
                Text = "A2Meter를 제거할까요?",
                AutoSize = false,
                Location = new Point(24, 22),
                Size = new Size(392, 28),
                Font = new Font(Font, FontStyle.Bold),
            };
            Controls.Add(title);

            var body = new Label
            {
                Text = "설치된 실행 파일과 바로가기가 삭제됩니다.",
                AutoSize = false,
                Location = new Point(24, 58),
                Size = new Size(392, 34),
            };
            Controls.Add(body);

            _deleteAllData = new CheckBox
            {
                Text = "미터기 모든 데이터 삭제",
                AutoSize = false,
                Location = new Point(24, 102),
                Size = new Size(392, 24),
            };
            Controls.Add(_deleteAllData);

            var pathHint = new Label
            {
                Text = InstallDir,
                AutoSize = false,
                Location = new Point(44, 128),
                Size = new Size(372, 20),
                ForeColor = SystemColors.GrayText,
            };
            Controls.Add(pathHint);

            _removeButton = new Button
            {
                Text = "제거",
                Location = new Point(248, 166),
                Size = new Size(82, 28),
            };
            _removeButton.Click += (_, _) => Remove();
            Controls.Add(_removeButton);

            _cancelButton = new Button
            {
                Text = "취소",
                Location = new Point(336, 166),
                Size = new Size(82, 28),
            };
            _cancelButton.Click += (_, _) => Close();
            Controls.Add(_cancelButton);

            AcceptButton = _removeButton;
            CancelButton = _cancelButton;
        }

        private void Remove()
        {
            bool deleteAll = _deleteAllData.Checked;
            if (deleteAll)
            {
                var confirm = MessageBox.Show(
                    "설정, 전투 기록, 게임 데이터 캐시를 포함한 모든 A2Meter 데이터를 삭제합니다.\n계속할까요?",
                    "A2Meter 제거",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            try
            {
                Enabled = false;
                KillRunningMeters();
                DeleteShortcuts();
                UnregisterUninstallEntry();

                if (!deleteAll)
                    DeleteInstalledFiles();

                MessageBox.Show(
                    "A2Meter 제거가 완료되었습니다.",
                    "A2Meter 제거",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                StartDeferredCleanup(deleteAll);
                Close();
            }
            catch (Exception ex)
            {
                Enabled = true;
                MessageBox.Show(
                    $"A2Meter 제거에 실패했습니다.\n\n{ex.Message}",
                    "A2Meter 제거",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }

    private static void KillRunningMeters()
    {
        foreach (var proc in Process.GetProcessesByName("A2Meter"))
        {
            try
            {
                if (!proc.CloseMainWindow() || !proc.WaitForExit(3_000))
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3_000);
                }
            }
            catch
            {
                // Continue removing what we can.
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    private static void DeleteShortcuts()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var startMenuDir = Path.Combine(programs, AppName);

        DeleteIfExists(Path.Combine(desktop, $"{AppName}.lnk"));
        DeleteIfExists(Path.Combine(startMenuDir, $"{AppName}.lnk"));
        DeleteIfExists(Path.Combine(startMenuDir, $"{AppName} 제거.lnk"));

        try
        {
            if (Directory.Exists(startMenuDir) && Directory.GetFileSystemEntries(startMenuDir).Length == 0)
                Directory.Delete(startMenuDir);
        }
        catch
        {
        }
    }

    private static void UnregisterUninstallEntry()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallRegistryPath, throwOnMissingSubKey: false);
        }
        catch
        {
        }
    }

    private static void DeleteInstalledFiles()
    {
        DeleteIfExists(TargetPath);
        DeleteIfExists(InstallerPath);
        DeleteIfExists(Path.Combine(InstallDir, "A2Updater.exe"));
        DeleteIfExists(Path.Combine(InstallDir, "install_path.txt"));
        DeleteIfExists(Path.Combine(InstallDir, "updater_tag.txt"));
        DeleteIfExists(TargetPath + ".new");
        DeleteIfExists(InstallerPath + ".new");
        DeleteIfExists(UninstallerPath + ".new");
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static void StartDeferredCleanup(bool deleteAll)
    {
        var self = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(self)) return;

        string command = deleteAll
            ? $"timeout /t 1 /nobreak >nul & rmdir /s /q \"{InstallDir}\""
            : $"timeout /t 1 /nobreak >nul & del /f /q \"{self}\" & rmdir \"{InstallDir}\" 2>nul";

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + command,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            UseShellExecute = false,
        });
    }
}
