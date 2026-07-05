using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace INGMeter.App;

public sealed class BuffTimerRow : INotifyPropertyChanged
{
	private string _iconPath = "";

	private string _name = "";

	private string _timeText = "";

	private string _tooltipText = "";

	private double _progress;

	private double _iconOpacity = 1.0;

	private double _sortSeconds;

	private bool _isExpired;

	private bool _isCritical;

	private Brush _ringBrush = Brushes.DeepSkyBlue;

	private Brush _badgeBrush = Brushes.Black;

	public int Key { get; init; }

	public string IconPath
	{
		get
		{
			return _iconPath;
		}
		init
		{
			_iconPath = value;
		}
	}

	public string Name
	{
		get
		{
			return _name;
		}
		init
		{
			_name = value;
		}
	}

	public string TimeText
	{
		get
		{
			return _timeText;
		}
		init
		{
			_timeText = value;
		}
	}

	public string TooltipText
	{
		get
		{
			return _tooltipText;
		}
		init
		{
			_tooltipText = value;
		}
	}

	public double Progress
	{
		get
		{
			return _progress;
		}
		init
		{
			_progress = value;
		}
	}

	public double IconOpacity
	{
		get
		{
			return _iconOpacity;
		}
		init
		{
			_iconOpacity = value;
		}
	}

	public double SortSeconds
	{
		get
		{
			return _sortSeconds;
		}
		init
		{
			_sortSeconds = value;
		}
	}

	public bool IsExpired
	{
		get
		{
			return _isExpired;
		}
		init
		{
			_isExpired = value;
		}
	}

	public bool IsCritical
	{
		get
		{
			return _isCritical;
		}
		init
		{
			_isCritical = value;
		}
	}

	public Brush RingBrush
	{
		get
		{
			return _ringBrush;
		}
		init
		{
			_ringBrush = value;
		}
	}

	public Brush BadgeBrush
	{
		get
		{
			return _badgeBrush;
		}
		init
		{
			_badgeBrush = value;
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public void CopyFrom(BuffTimerRow source)
	{
		SetField(ref _iconPath, source.IconPath, "IconPath");
		SetField(ref _name, source.Name, "Name");
		SetField(ref _timeText, source.TimeText, "TimeText");
		SetField(ref _tooltipText, source.TooltipText, "TooltipText");
		SetField(ref _progress, source.Progress, "Progress");
		SetField(ref _iconOpacity, source.IconOpacity, "IconOpacity");
		SetField(ref _sortSeconds, source.SortSeconds, "SortSeconds");
		SetField(ref _isExpired, source.IsExpired, "IsExpired");
		SetField(ref _isCritical, source.IsCritical, "IsCritical");
		SetField(ref _ringBrush, source.RingBrush, "RingBrush");
		SetField(ref _badgeBrush, source.BadgeBrush, "BadgeBrush");
	}

	private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
	{
		if (!EqualityComparer<T>.Default.Equals(field, value))
		{
			field = value;
			this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
