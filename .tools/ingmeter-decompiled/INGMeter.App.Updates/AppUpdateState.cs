using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace INGMeter.App.Updates;

public sealed class AppUpdateState : INotifyPropertyChanged
{
	private bool _isVelopackInstalled;

	private bool _isChecking;

	private bool _isUpdateAvailable;

	private bool _isDownloading;

	private bool _isReadyToInstall;

	private int _downloadProgress;

	private string _latestVersion = "";

	private string _message = "";

	private string _releaseNotes = "";

	public bool IsVelopackInstalled
	{
		get
		{
			return _isVelopackInstalled;
		}
		set
		{
			SetField(ref _isVelopackInstalled, value, "IsVelopackInstalled");
		}
	}

	public bool IsChecking
	{
		get
		{
			return _isChecking;
		}
		set
		{
			SetField(ref _isChecking, value, "IsChecking");
		}
	}

	public bool IsUpdateAvailable
	{
		get
		{
			return _isUpdateAvailable;
		}
		set
		{
			SetField(ref _isUpdateAvailable, value, "IsUpdateAvailable");
		}
	}

	public bool IsDownloading
	{
		get
		{
			return _isDownloading;
		}
		set
		{
			SetField(ref _isDownloading, value, "IsDownloading");
		}
	}

	public bool IsReadyToInstall
	{
		get
		{
			return _isReadyToInstall;
		}
		set
		{
			SetField(ref _isReadyToInstall, value, "IsReadyToInstall");
		}
	}

	public int DownloadProgress
	{
		get
		{
			return _downloadProgress;
		}
		set
		{
			SetField(ref _downloadProgress, Math.Clamp(value, 0, 100), "DownloadProgress");
		}
	}

	public string LatestVersion
	{
		get
		{
			return _latestVersion;
		}
		set
		{
			SetField(ref _latestVersion, value, "LatestVersion");
		}
	}

	public string Message
	{
		get
		{
			return _message;
		}
		set
		{
			SetField(ref _message, value, "Message");
		}
	}

	public string ReleaseNotes
	{
		get
		{
			return _releaseNotes;
		}
		set
		{
			SetField(ref _releaseNotes, value, "ReleaseNotes");
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (!object.Equals(field, value))
		{
			field = value;
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
