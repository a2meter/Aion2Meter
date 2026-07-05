using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;

namespace INGMeter.WpfUI;

public class EncounterHistoryWindow : Window, IComponentConnector
{
	private readonly ObservableCollection<EncounterHistoryRow> _records;

	private readonly ListCollectionView _recordsView;

	private bool _isLoading;

	internal TextBlock txtTitleRecordCount;

	internal TextBox txtSearch;

	internal Button btnOpen;

	internal ListView lstRecords;

	internal TextBlock txtEmpty;

	private bool _contentLoaded;

	public ICollectionView RecordsView => _recordsView;

	public EncounterHistoryRow? SelectedRecord { get; private set; }

	public EncounterHistoryWindow(IEnumerable<EncounterHistoryRow>? records = null)
	{
		InitializeComponent();
		_records = new ObservableCollection<EncounterHistoryRow>((records ?? Array.Empty<EncounterHistoryRow>()).OrderByDescending((EncounterHistoryRow x) => x.StartUtc));
		_recordsView = (ListCollectionView)CollectionViewSource.GetDefaultView(_records);
		_recordsView.GroupDescriptions.Add(new PropertyGroupDescription("DateGroup"));
		_recordsView.SortDescriptions.Add(new SortDescription("StartUtc", ListSortDirection.Descending));
		_recordsView.Filter = FilterRecord;
		base.DataContext = this;
		UpdateCountAndEmptyState();
	}

	public void SetLoading(bool value)
	{
		_isLoading = value;
		if (value)
		{
			txtTitleRecordCount.Text = "불러오는 중";
			txtEmpty.Text = "기록을 불러오는 중입니다.";
			txtEmpty.Visibility = Visibility.Visible;
		}
		else
		{
			txtEmpty.Text = "저장된 기록이 없습니다.";
			UpdateCountAndEmptyState();
		}
	}

	public void SetRecords(IEnumerable<EncounterHistoryRow> records)
	{
		_records.Clear();
		foreach (EncounterHistoryRow item in records.OrderByDescending((EncounterHistoryRow x) => x.StartUtc))
		{
			_records.Add(item);
		}
		_recordsView.Refresh();
		_isLoading = false;
		txtEmpty.Text = "저장된 기록이 없습니다.";
		UpdateCountAndEmptyState();
	}

	private bool FilterRecord(object item)
	{
		if (!(item is EncounterHistoryRow encounterHistoryRow))
		{
			return false;
		}
		string text = txtSearch?.Text?.Trim() ?? "";
		if (text.Length != 0)
		{
			return encounterHistoryRow.SearchText.Contains(text, StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}

	private void Search_TextChanged(object sender, TextChangedEventArgs e)
	{
		_recordsView.Refresh();
		UpdateCountAndEmptyState();
	}

	private void Records_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		SelectedRecord = lstRecords.SelectedItem as EncounterHistoryRow;
		btnOpen.IsEnabled = SelectedRecord != null;
	}

	private void Records_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (lstRecords.SelectedItem is EncounterHistoryRow)
		{
			OpenSelected();
		}
	}

	private void Open_Click(object sender, RoutedEventArgs e)
	{
		OpenSelected();
	}

	private void Close_Click(object sender, RoutedEventArgs e)
	{
		Close();
	}

	protected override void OnPreviewKeyDown(KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			e.Handled = true;
			Close();
		}
		else
		{
			base.OnPreviewKeyDown(e);
		}
	}

	private void OpenSelected()
	{
		SelectedRecord = lstRecords.SelectedItem as EncounterHistoryRow;
		if (SelectedRecord != null)
		{
			base.DialogResult = true;
			Close();
		}
	}

	private void UpdateCountAndEmptyState()
	{
		if (!_isLoading)
		{
			int num = _recordsView.Cast<object>().Count();
			int count = _records.Count;
			txtTitleRecordCount.Text = ((num == count) ? $"{count:N0}개" : $"{num:N0}/{count:N0}개");
			txtEmpty.Visibility = ((num > 0) ? Visibility.Collapsed : Visibility.Visible);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/encounterhistorywindow.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		switch (connectionId)
		{
		case 1:
			txtTitleRecordCount = (TextBlock)target;
			break;
		case 2:
			txtSearch = (TextBox)target;
			txtSearch.TextChanged += Search_TextChanged;
			break;
		case 3:
			btnOpen = (Button)target;
			btnOpen.Click += Open_Click;
			break;
		case 4:
			lstRecords = (ListView)target;
			lstRecords.SelectionChanged += Records_SelectionChanged;
			lstRecords.MouseDoubleClick += Records_MouseDoubleClick;
			break;
		case 5:
			txtEmpty = (TextBlock)target;
			break;
		case 6:
			((Button)target).Click += Close_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
