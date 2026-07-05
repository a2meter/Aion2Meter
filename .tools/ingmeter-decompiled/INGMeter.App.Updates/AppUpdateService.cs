using System;
using System.Threading;
using System.Threading.Tasks;
using INGMeter.Core;
using Velopack;

namespace INGMeter.App.Updates;

public sealed class AppUpdateService
{
	private UpdateManager _updateManager;

	private string _feedUrl;

	private UpdateInfo? _availableUpdate;

	private VelopackAsset? _pendingUpdate;

	public static string DefaultFeedUrl => WebEndpoint.Url("/updates/ingmeter");

	public AppUpdateState State { get; }

	public AppUpdateService(string? feedUrl = null)
	{
		_feedUrl = feedUrl ?? DefaultFeedUrl;
		_updateManager = new UpdateManager(_feedUrl);
		State = new AppUpdateState
		{
			IsVelopackInstalled = _updateManager.IsInstalled,
			Message = (_updateManager.IsInstalled ? "업데이트 확인 대기 중" : "설치본이 아니어서 자동 업데이트를 사용할 수 없습니다.")
		};
	}

	public void RefreshFeedUrl()
	{
		string defaultFeedUrl = DefaultFeedUrl;
		if (!string.Equals(_feedUrl, defaultFeedUrl, StringComparison.OrdinalIgnoreCase))
		{
			_feedUrl = defaultFeedUrl;
			_updateManager = new UpdateManager(_feedUrl);
			State.IsVelopackInstalled = _updateManager.IsInstalled;
			ClearUpdateState("");
		}
	}

	public async Task CheckAsync(bool notifyWhenCurrent, CancellationToken cancellationToken = default(CancellationToken))
	{
		RefreshFeedUrl();
		State.IsChecking = true;
		State.Message = "업데이트 확인 중";
		try
		{
			State.IsVelopackInstalled = _updateManager.IsInstalled;
			if (!_updateManager.IsInstalled)
			{
				ClearUpdateState(notifyWhenCurrent ? "설치본이 아니어서 자동 업데이트를 사용할 수 없습니다." : "");
				return;
			}
			_pendingUpdate = _updateManager.UpdatePendingRestart;
			if (_pendingUpdate != null)
			{
				SetReadyToInstall(_pendingUpdate);
				return;
			}
			_availableUpdate = await _updateManager.CheckForUpdatesAsync();
			cancellationToken.ThrowIfCancellationRequested();
			if (_availableUpdate == null)
			{
				ClearUpdateState(notifyWhenCurrent ? "현재 최신 버전입니다." : "");
				return;
			}
			VelopackAsset targetFullRelease = _availableUpdate.TargetFullRelease;
			State.IsUpdateAvailable = true;
			State.IsReadyToInstall = false;
			State.DownloadProgress = 0;
			State.LatestVersion = targetFullRelease.Version.ToString();
			State.ReleaseNotes = targetFullRelease.NotesMarkdown ?? targetFullRelease.NotesHTML ?? "";
			State.Message = "새 버전 v" + State.LatestVersion + " 업데이트 가능";
		}
		catch (OperationCanceledException)
		{
			State.Message = "업데이트 확인이 취소되었습니다.";
		}
		catch (Exception ex2)
		{
			ClearUpdateState("업데이트 확인 실패: " + ex2.Message);
		}
		finally
		{
			State.IsChecking = false;
		}
	}

	public async Task DownloadAsync(CancellationToken cancellationToken = default(CancellationToken))
	{
		RefreshFeedUrl();
		if (!_updateManager.IsInstalled)
		{
			ClearUpdateState("설치본이 아니어서 자동 업데이트를 사용할 수 없습니다.");
			return;
		}
		if (_availableUpdate == null)
		{
			await CheckAsync(notifyWhenCurrent: true, cancellationToken);
		}
		if (_availableUpdate == null)
		{
			return;
		}
		State.IsDownloading = true;
		State.DownloadProgress = 0;
		State.Message = $"v{_availableUpdate.TargetFullRelease.Version} 다운로드 중";
		try
		{
			await _updateManager.DownloadUpdatesAsync(_availableUpdate, delegate(int progress)
			{
				State.DownloadProgress = progress;
			}, cancellationToken);
			_pendingUpdate = _updateManager.UpdatePendingRestart ?? _availableUpdate.TargetFullRelease;
			SetReadyToInstall(_pendingUpdate);
		}
		catch (OperationCanceledException)
		{
			State.Message = "업데이트 다운로드가 취소되었습니다.";
		}
		catch (Exception ex2)
		{
			State.Message = "업데이트 다운로드 실패: " + ex2.Message;
		}
		finally
		{
			State.IsDownloading = false;
		}
	}

	public void ApplyAndRestart()
	{
		RefreshFeedUrl();
		if (!_updateManager.IsInstalled)
		{
			ClearUpdateState("설치본이 아니어서 자동 업데이트를 사용할 수 없습니다.");
			return;
		}
		if ((object)_pendingUpdate == null)
		{
			_pendingUpdate = _updateManager.UpdatePendingRestart;
		}
		if (_pendingUpdate == null)
		{
			State.Message = "설치할 업데이트가 없습니다.";
		}
		else
		{
			_updateManager.ApplyUpdatesAndRestart(_pendingUpdate);
		}
	}

	private void ClearUpdateState(string message)
	{
		_availableUpdate = null;
		_pendingUpdate = null;
		State.IsUpdateAvailable = false;
		State.IsReadyToInstall = false;
		State.DownloadProgress = 0;
		State.LatestVersion = "";
		State.ReleaseNotes = "";
		State.Message = message;
	}

	private void SetReadyToInstall(VelopackAsset update)
	{
		State.IsUpdateAvailable = true;
		State.IsReadyToInstall = true;
		State.DownloadProgress = 100;
		State.LatestVersion = update.Version.ToString();
		State.ReleaseNotes = update.NotesMarkdown ?? update.NotesHTML ?? "";
		State.Message = "v" + State.LatestVersion + " 다운로드 완료. 재시작하면 설치됩니다.";
	}
}
