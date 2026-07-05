using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace INGMeter.App;

public class PrivacyConsentWindow : Window, IComponentConnector
{
	internal CheckBox chkAccept;

	internal TextBlock txtConsentVersion;

	internal Button btnAccept;

	private bool _contentLoaded;

	public bool IsAccepted { get; private set; }

	public PrivacyConsentWindow()
	{
		InitializeComponent();
		txtConsentVersion.Text = "동의 버전: 2026-05-01";
	}

	private void ChkAccept_Changed(object sender, RoutedEventArgs e)
	{
		btnAccept.IsEnabled = chkAccept.IsChecked == true;
	}

	private void BtnAccept_Click(object sender, RoutedEventArgs e)
	{
		if (chkAccept.IsChecked == true)
		{
			IsAccepted = true;
			base.DialogResult = true;
			Close();
		}
	}

	private void BtnDecline_Click(object sender, RoutedEventArgs e)
	{
		CloseWithDecline();
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		CloseWithDecline();
	}

	private void Header_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left)
		{
			DragMove();
		}
	}

	private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			CloseWithDecline();
			e.Handled = true;
		}
	}

	private void CloseWithDecline()
	{
		IsAccepted = false;
		base.DialogResult = false;
		Close();
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/privacyconsentwindow.xaml", UriKind.Relative);
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
			((PrivacyConsentWindow)target).PreviewKeyDown += Window_PreviewKeyDown;
			break;
		case 2:
			((Border)target).MouseDown += Header_MouseDown;
			break;
		case 3:
			((Button)target).Click += BtnClose_Click;
			break;
		case 4:
			chkAccept = (CheckBox)target;
			chkAccept.Checked += ChkAccept_Changed;
			chkAccept.Unchecked += ChkAccept_Changed;
			break;
		case 5:
			txtConsentVersion = (TextBlock)target;
			break;
		case 6:
			((Button)target).Click += BtnDecline_Click;
			break;
		case 7:
			btnAccept = (Button)target;
			btnAccept.Click += BtnAccept_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
