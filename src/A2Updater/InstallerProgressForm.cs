using System.Drawing;

namespace A2Updater;

internal sealed class InstallerProgressForm : Form
{
    private readonly Label _title;
    private readonly Label _status;
    private readonly Label _detail;
    private readonly ProgressBar _overallProgress;
    private readonly ProgressBar _currentProgress;
    private readonly TextBox _log;
    private readonly Button _closeButton;
    private bool _finished;

    public InstallerProgressForm()
    {
        Text = "A2Meter 설치";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(520, 360);
        BackColor = Color.FromArgb(30, 30, 42);
        Font = new Font("Malgun Gothic", 9.5f);

        _title = new Label
        {
            Text = "A2Meter 설치 중",
            AutoSize = false,
            Location = new Point(20, 18),
            Size = new Size(480, 28),
            ForeColor = Color.White,
            Font = new Font(Font, FontStyle.Bold),
        };

        _status = new Label
        {
            Text = "준비 중...",
            AutoSize = false,
            Location = new Point(20, 56),
            Size = new Size(480, 24),
            ForeColor = Color.FromArgb(214, 218, 230),
        };

        _overallProgress = new ProgressBar
        {
            Location = new Point(20, 90),
            Size = new Size(480, 18),
            Minimum = 0,
            Maximum = 100,
        };

        _detail = new Label
        {
            Text = "",
            AutoSize = false,
            Location = new Point(20, 120),
            Size = new Size(480, 22),
            ForeColor = Color.FromArgb(178, 184, 202),
        };

        _currentProgress = new ProgressBar
        {
            Location = new Point(20, 150),
            Size = new Size(480, 16),
            Minimum = 0,
            Maximum = 100,
        };

        _log = new TextBox
        {
            Location = new Point(20, 184),
            Size = new Size(480, 110),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(22, 24, 31),
            ForeColor = Color.FromArgb(214, 218, 230),
            BorderStyle = BorderStyle.FixedSingle,
        };

        _closeButton = new Button
        {
            Text = "닫기",
            Enabled = false,
            Location = new Point(400, 310),
            Size = new Size(100, 32),
        };
        _closeButton.Click += (_, _) =>
        {
            _finished = true;
            Close();
        };

        Controls.Add(_title);
        Controls.Add(_status);
        Controls.Add(_overallProgress);
        Controls.Add(_detail);
        Controls.Add(_currentProgress);
        Controls.Add(_log);
        Controls.Add(_closeButton);
    }

    public void SetStep(int percent, string status)
    {
        RunOnUi(() =>
        {
            _overallProgress.Value = Math.Clamp(percent, _overallProgress.Minimum, _overallProgress.Maximum);
            _status.Text = status;
            _detail.Text = "";
            _currentProgress.Style = ProgressBarStyle.Marquee;
            AddLogCore(status);
        });
    }

    public void SetDetail(string detail)
    {
        RunOnUi(() => _detail.Text = detail);
    }

    public void SetCurrentProgress(long downloadedBytes, long totalBytes)
    {
        RunOnUi(() =>
        {
            if (totalBytes <= 0)
            {
                _currentProgress.Style = ProgressBarStyle.Marquee;
                _detail.Text = $"{FormatBytes(downloadedBytes)} 다운로드됨";
                return;
            }

            _currentProgress.Style = ProgressBarStyle.Blocks;
            int percent = (int)Math.Clamp(downloadedBytes * 100d / totalBytes, 0, 100);
            _currentProgress.Value = percent;
            _detail.Text = $"{FormatBytes(downloadedBytes)} / {FormatBytes(totalBytes)} ({percent}%)";
        });
    }

    public void Complete(bool success, string message)
    {
        RunOnUi(() =>
        {
            _finished = true;
            _title.Text = success ? "A2Meter 설치 완료" : "A2Meter 설치 실패";
            _status.Text = message;
            _detail.Text = "";
            _overallProgress.Value = success ? 100 : _overallProgress.Value;
            _currentProgress.Style = ProgressBarStyle.Blocks;
            _currentProgress.Value = success ? 100 : 0;
            _closeButton.Enabled = true;
            AddLogCore(message);
        });
    }

    public void CloseAfterComplete()
    {
        RunOnUi(() =>
        {
            _finished = true;
            Close();
        });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_finished && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private void RunOnUi(Action action)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
            BeginInvoke(action);
        else
            action();
    }

    private void AddLogCore(string message)
    {
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} {units[unit]}" : $"{value:0.0} {units[unit]}";
    }
}
