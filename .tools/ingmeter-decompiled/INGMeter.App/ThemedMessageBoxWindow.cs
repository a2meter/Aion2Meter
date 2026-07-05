using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

namespace INGMeter.App;

public class ThemedMessageBoxWindow : Window, IComponentConnector
{
	private MessageBoxResult _result;

	internal TextBlock txtTitle;

	internal TextBlock txtIcon;

	internal TextBlock txtMessage;

	internal StackPanel buttonPanel;

	private bool _contentLoaded;

	public MessageBoxResult Result => _result;

	public ThemedMessageBoxWindow(string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
	{
		InitializeComponent();
		base.Title = caption;
		txtTitle.Text = caption;
		txtMessage.Text = message;
		txtIcon.Text = GetIconText(image);
		BuildButtons(buttons);
	}

	private static string GetIconText(MessageBoxImage image)
	{
		return image switch
		{
			MessageBoxImage.Question => "?", 
			MessageBoxImage.Exclamation => "!", 
			MessageBoxImage.Hand => "!", 
			_ => "i", 
		};
	}

	private void BuildButtons(MessageBoxButton buttons)
	{
		buttonPanel.Children.Clear();
		switch (buttons)
		{
		case MessageBoxButton.OKCancel:
			AddButton("확인", MessageBoxResult.OK, isDefault: true);
			AddButton("취소", MessageBoxResult.Cancel, isDefault: false, isCancel: true);
			break;
		case MessageBoxButton.YesNo:
			AddButton("예", MessageBoxResult.Yes, isDefault: true);
			AddButton("아니요", MessageBoxResult.No, isDefault: false, isCancel: true);
			break;
		case MessageBoxButton.YesNoCancel:
			AddButton("예", MessageBoxResult.Yes, isDefault: true);
			AddButton("아니요", MessageBoxResult.No);
			AddButton("취소", MessageBoxResult.Cancel, isDefault: false, isCancel: true);
			break;
		default:
			AddButton("확인", MessageBoxResult.OK, isDefault: true, isCancel: true);
			break;
		}
	}

	private void AddButton(string text, MessageBoxResult result, bool isDefault = false, bool isCancel = false)
	{
		Button button = new Button
		{
			Content = text,
			IsDefault = isDefault,
			IsCancel = isCancel,
			Style = (Style)FindResource("DialogButton")
		};
		button.Click += delegate
		{
			_result = result;
			base.DialogResult = true;
			Close();
		};
		buttonPanel.Children.Add(button);
	}

	private void Header_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ChangedButton == MouseButton.Left)
		{
			DragMove();
		}
	}

	private void BtnClose_Click(object sender, RoutedEventArgs e)
	{
		CloseWithCancelResult();
	}

	private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Escape)
		{
			CloseWithCancelResult();
			e.Handled = true;
		}
	}

	private void CloseWithCancelResult()
	{
		if (_result == MessageBoxResult.None)
		{
			_result = MessageBoxResult.Cancel;
		}
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
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/themedmessageboxwindow.xaml", UriKind.Relative);
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
			((ThemedMessageBoxWindow)target).PreviewKeyDown += Window_PreviewKeyDown;
			break;
		case 2:
			((Border)target).MouseDown += Header_MouseDown;
			break;
		case 3:
			txtTitle = (TextBlock)target;
			break;
		case 4:
			((Button)target).Click += BtnClose_Click;
			break;
		case 5:
			txtIcon = (TextBlock)target;
			break;
		case 6:
			txtMessage = (TextBlock)target;
			break;
		case 7:
			buttonPanel = (StackPanel)target;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
