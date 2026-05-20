using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using A2Meter.Core;
using A2Meter.Data;
using Microsoft.Win32;

namespace A2Meter.Forms;

/// First-launch setup dialog.
/// Shows Npcap installation status + game data download progress.
/// Blocks until prerequisites are satisfied (or user explicitly skips Npcap).
internal sealed class SetupForm : Form
{
    private readonly Label _titleLabel;
    private readonly Label _npcapStatusLabel;
    private readonly Button _npcapInstallBtn;
    private readonly Label _dataStatusLabel;
    private readonly ProgressBar _dataProgress;
    private readonly Label _dataDetailLabel;
    private readonly Button _startBtn;
    private readonly Label _skipNpcapLink;

    private bool _npcapReady;
    private bool _dataReady;
    private bool _downloading;
    private bool _npcapInstalling;

    private const string NpcapInstallerUrl = "https://npcap.com/dist/npcap-1.80.exe";
    private static readonly string NpcapInstallerPath =
        Path.Combine(Path.GetTempPath(), "npcap-installer.exe");

    public SetupForm()
    {
        var theme = AppSettings.Instance.Theme;

        Text = "A2Meter";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = true;
        Size = new Size(420, 320);
        BackColor = theme.BgColor;
        ForeColor = theme.TextColor;
        DoubleBuffered = true;
        Font = new Font("Malgun Gothic", 9f);

        // ── Title ──
        _titleLabel = new Label
        {
            Text = "A2Meter 초기 설정",
            Font = new Font("Malgun Gothic", 14f, FontStyle.Bold),
            ForeColor = theme.TextColor,
            Location = new Point(28, 24),
            AutoSize = true,
        };
        Controls.Add(_titleLabel);

        // ── Section 1: Npcap ──
        var npcapHeader = new Label
        {
            Text = "1. 패킷 캡처 드라이버 (Npcap)",
            Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold),
            ForeColor = theme.TextColor,
            Location = new Point(28, 70),
            AutoSize = true,
        };
        Controls.Add(npcapHeader);

        _npcapStatusLabel = new Label
        {
            Location = new Point(44, 96),
            Size = new Size(300, 20),
            ForeColor = theme.TextDimColor,
        };
        Controls.Add(_npcapStatusLabel);

        _npcapInstallBtn = new Button
        {
            Text = "Npcap 설치",
            FlatStyle = FlatStyle.Flat,
            BackColor = theme.HeaderColor,
            ForeColor = theme.AccentColor,
            Size = new Size(120, 28),
            Location = new Point(44, 120),
            Cursor = Cursors.Hand,
            Visible = false,
        };
        _npcapInstallBtn.FlatAppearance.BorderColor = theme.AccentColor;
        _npcapInstallBtn.Click += OnNpcapInstallClick;
        Controls.Add(_npcapInstallBtn);

        _skipNpcapLink = new Label
        {
            Text = "건너뛰기",
            ForeColor = theme.TextDimColor,
            Font = new Font("Malgun Gothic", 8f, FontStyle.Underline),
            Location = new Point(174, 127),
            AutoSize = true,
            Cursor = Cursors.Hand,
            Visible = false,
        };
        _skipNpcapLink.Click += (_, _) =>
        {
            _npcapReady = true;
            UpdateUI();
        };
        Controls.Add(_skipNpcapLink);

        // ── Section 2: Game Data ──
        var dataHeader = new Label
        {
            Text = "2. 게임 데이터",
            Font = new Font("Malgun Gothic", 9.5f, FontStyle.Bold),
            ForeColor = theme.TextColor,
            Location = new Point(28, 164),
            AutoSize = true,
        };
        Controls.Add(dataHeader);

        _dataStatusLabel = new Label
        {
            Location = new Point(44, 190),
            Size = new Size(340, 20),
            ForeColor = theme.TextDimColor,
        };
        Controls.Add(_dataStatusLabel);

        _dataProgress = new ProgressBar
        {
            Location = new Point(44, 214),
            Size = new Size(330, 18),
            Style = ProgressBarStyle.Continuous,
            Visible = false,
        };
        Controls.Add(_dataProgress);

        _dataDetailLabel = new Label
        {
            Location = new Point(44, 235),
            Size = new Size(340, 16),
            ForeColor = theme.TextDimColor,
            Font = new Font("Malgun Gothic", 8f),
        };
        Controls.Add(_dataDetailLabel);

        // ── Start button ──
        _startBtn = new Button
        {
            Text = "시작",
            FlatStyle = FlatStyle.Flat,
            BackColor = theme.AccentColor,
            ForeColor = Color.FromArgb(20, 24, 36),
            Font = new Font("Malgun Gothic", 10f, FontStyle.Bold),
            Size = new Size(120, 34),
            Location = new Point((420 - 120) / 2, 265),
            Cursor = Cursors.Hand,
            Enabled = false,
        };
        _startBtn.FlatAppearance.BorderSize = 0;
        _startBtn.Click += (_, _) => DialogResult = DialogResult.OK;
        Controls.Add(_startBtn);

        // ── Initial state check ──
        CheckNpcap();
        CheckData();
        UpdateUI();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_dataReady)
            _ = DownloadDataAsync();
    }

    // ── Npcap Detection ──

    private void CheckNpcap()
    {
        _npcapReady = IsNpcapInstalled();
    }

    private static bool IsNpcapInstalled()
    {
        // Method 1: Registry (most reliable).
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Npcap");
            if (key != null) return true;
        }
        catch { }

        // Method 2: Check for npcap DLL in System32\Npcap.
        var npcapDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "Npcap");
        if (File.Exists(Path.Combine(npcapDir, "wpcap.dll")))
            return true;

        // Method 3: Check legacy WinPcap path.
        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (File.Exists(Path.Combine(sys32, "wpcap.dll")))
            return true;

        return false;
    }

    private async void OnNpcapInstallClick(object? sender, EventArgs e)
    {
        if (_npcapInstalling) return;
        _npcapInstalling = true;
        _npcapInstallBtn.Enabled = false;
        _npcapStatusLabel.Text = "Npcap 다운로드 중...";
        _npcapStatusLabel.ForeColor = AppSettings.Instance.Theme.TextDimColor;

        try
        {
            // Download installer.
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            using var resp = await http.GetAsync(NpcapInstallerUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            using (var source = await resp.Content.ReadAsStreamAsync())
            using (var fs = File.Create(NpcapInstallerPath))
                await source.CopyToAsync(fs);

            _npcapStatusLabel.Text = "설치 중... (UAC 확인 필요)";

            // Run silent install. UAC prompt will appear.
            var psi = new ProcessStartInfo
            {
                FileName = NpcapInstallerPath,
                Arguments = "/S /winpcap_mode=yes",
                UseShellExecute = true,
                Verb = "runas",
            };

            var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();

                // Re-check installation.
                await Task.Delay(500); // brief delay for registry to propagate
                CheckNpcap();

                if (_npcapReady)
                {
                    _npcapStatusLabel.Text = "✓ 설치 완료";
                    _npcapStatusLabel.ForeColor = AppSettings.Instance.Theme.AccentColor;
                }
                else
                {
                    _npcapStatusLabel.Text = "✗ 설치에 실패했습니다. 수동으로 설치해 주세요.";
                    _npcapStatusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                    _npcapInstallBtn.Enabled = true;
                }
            }
        }
        catch (Win32Exception)
        {
            // UAC denied by user.
            _npcapStatusLabel.Text = "✗ 관리자 권한이 필요합니다";
            _npcapStatusLabel.ForeColor = Color.FromArgb(255, 100, 100);
            _npcapInstallBtn.Enabled = true;
        }
        catch (Exception ex)
        {
            _npcapStatusLabel.Text = $"✗ 오류: {ex.Message}";
            _npcapStatusLabel.ForeColor = Color.FromArgb(255, 100, 100);
            _npcapInstallBtn.Enabled = true;
        }
        finally
        {
            _npcapInstalling = false;
            UpdateUI();
            // Clean up installer.
            try { if (File.Exists(NpcapInstallerPath)) File.Delete(NpcapInstallerPath); } catch { }
        }
    }

    // ── Data Download ──

    private void CheckData()
    {
        _dataReady = DataManager.IsReady;
    }

    private async Task DownloadDataAsync()
    {
        if (_dataReady || _downloading) return;
        _downloading = true;

        Invoke(() =>
        {
            _dataProgress.Visible = true;
            _dataProgress.Style = ProgressBarStyle.Marquee;
            _dataStatusLabel.Text = "데이터 다운로드 중...";
            _dataDetailLabel.Text = "";
        });

        try
        {
            var success = await DataManager.EnsureDataWithProgressAsync(
                (downloaded, total) =>
                {
                    if (IsDisposed) return;
                    try
                    {
                        Invoke(() =>
                        {
                            if (total > 0)
                            {
                                _dataProgress.Style = ProgressBarStyle.Continuous;
                                _dataProgress.Maximum = 100;
                                _dataProgress.Value = (int)(downloaded * 100 / total);
                                _dataDetailLabel.Text = $"{downloaded / 1024 / 1024}MB / {total / 1024 / 1024}MB";
                            }
                        });
                    }
                    catch { }
                });

            if (IsDisposed) return;
            Invoke(() =>
            {
                _dataReady = success;
                _downloading = false;
                if (success)
                {
                    _dataProgress.Value = 100;
                    _dataStatusLabel.Text = "✓ 게임 데이터 준비 완료";
                    _dataStatusLabel.ForeColor = AppSettings.Instance.Theme.AccentColor;
                    _dataDetailLabel.Text = "";
                }
                else
                {
                    _dataStatusLabel.Text = "✗ 다운로드 실패 (인터넷 연결을 확인하세요)";
                    _dataStatusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                    _dataDetailLabel.Text = "재시도하려면 프로그램을 다시 실행하세요.";
                }
                UpdateUI();
            });
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            Invoke(() =>
            {
                _downloading = false;
                _dataStatusLabel.Text = $"✗ 오류: {ex.Message}";
                _dataStatusLabel.ForeColor = Color.FromArgb(255, 100, 100);
                UpdateUI();
            });
        }
    }

    // ── UI State ──

    private void UpdateUI()
    {
        if (_npcapReady)
        {
            _npcapStatusLabel.Text = "✓ 설치됨";
            _npcapStatusLabel.ForeColor = AppSettings.Instance.Theme.AccentColor;
            _npcapInstallBtn.Visible = false;
            _skipNpcapLink.Visible = false;
        }
        else
        {
            _npcapStatusLabel.Text = "✗ Npcap이 설치되어 있지 않습니다";
            _npcapStatusLabel.ForeColor = Color.FromArgb(255, 100, 100);
            _npcapInstallBtn.Visible = true;
            _skipNpcapLink.Visible = true;
        }

        if (_dataReady && !_downloading)
        {
            _dataStatusLabel.Text = "✓ 게임 데이터 준비 완료";
            _dataStatusLabel.ForeColor = AppSettings.Instance.Theme.AccentColor;
            _dataProgress.Visible = false;
        }

        // Enable start when data is ready (Npcap can be skipped).
        _startBtn.Enabled = _dataReady && _npcapReady;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Subtle accent line under title.
        var theme = AppSettings.Instance.Theme;
        using var pen = new Pen(theme.AccentColor, 2f);
        g.DrawLine(pen, 28, 54, 140, 54);
    }
}
