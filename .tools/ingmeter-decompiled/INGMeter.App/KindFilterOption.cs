using System.ComponentModel;

namespace INGMeter.App;

public sealed class KindFilterOption : INotifyPropertyChanged
{
	private bool _isSelected;

	public string Key { get; }

	public string Label { get; }

	public bool IsSelected
	{
		get
		{
			return _isSelected;
		}
		set
		{
			if (_isSelected != value)
			{
				_isSelected = value;
				this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsSelected"));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	public KindFilterOption(string key, string label, bool isSelected = false)
	{
		Key = key;
		Label = label;
		_isSelected = isSelected;
	}
}
