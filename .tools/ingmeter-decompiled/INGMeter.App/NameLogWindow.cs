using System;
using System.CodeDom.Compiler;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using INGMeter.Core;

namespace INGMeter.App;

public class NameLogWindow : Window, IComponentConnector
{
	private readonly NameCache _names;

	internal DataGrid dgvLog;

	private bool _contentLoaded;

	public ObservableCollection<NameLogEntry> Logs { get; } = new ObservableCollection<NameLogEntry>();

	public NameLogWindow(NameCache names)
	{
		InitializeComponent();
		_names = names;
		dgvLog.ItemsSource = Logs;
		_names.NameMapped += OnNameMapped;
	}

	private void OnNameMapped(int id, string name, string source, byte[]? packet)
	{
		base.Dispatcher.Invoke(delegate
		{
			Logs.Insert(0, new NameLogEntry
			{
				Time = DateTime.Now.ToString("HH:mm:ss.fff"),
				ActorId = id,
				Name = name,
				Source = source,
				RawPacket = packet
			});
			if (Logs.Count > 1000)
			{
				Logs.RemoveAt(Logs.Count - 1);
			}
		});
	}

	private void DgvLog_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (dgvLog.SelectedItem is NameLogEntry { RawPacket: not null } nameLogEntry)
		{
			string summary = $"시간: {nameLogEntry.Time}\nActorID: {nameLogEntry.ActorId}\n매핑이름: {nameLogEntry.Name}\n추출출처: {nameLogEntry.Source}\n패킷크기: {nameLogEntry.RawPacket.Length} bytes";
			PacketDetailsWindow packetDetailsWindow = new PacketDetailsWindow("\ud83d\udd0d 매핑 상세 정보 — " + nameLogEntry.Name, summary, nameLogEntry.RawPacket);
			packetDetailsWindow.Owner = this;
			packetDetailsWindow.Show();
		}
	}

	private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount != 2)
		{
			DragMove();
		}
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		_names.NameMapped -= OnNameMapped;
		Close();
	}

	private void BtnClear_Click(object sender, RoutedEventArgs e)
	{
		Logs.Clear();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/namelogwindow.xaml", UriKind.Relative);
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
			((Border)target).MouseLeftButtonDown += TitleBar_MouseLeftButtonDown;
			break;
		case 2:
			((Button)target).Click += BtnClose_Click;
			break;
		case 3:
			dgvLog = (DataGrid)target;
			dgvLog.MouseDoubleClick += DgvLog_MouseDoubleClick;
			break;
		case 4:
			((Button)target).Click += BtnClear_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
