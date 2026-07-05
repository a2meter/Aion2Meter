using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace INGMeter.App;

public class CharacterConsentWindow : Window, IComponentConnector
{
	internal TextBlock txtCharacter;

	private bool _contentLoaded;

	public bool IsAccepted { get; private set; }

	public bool HasChoice { get; private set; }

	public CharacterConsentWindow(string characterName, string serverName)
	{
		InitializeComponent();
		txtCharacter.Text = characterName + " [" + serverName + "]";
	}

	private void Accept_Click(object sender, RoutedEventArgs e)
	{
		HasChoice = true;
		IsAccepted = true;
		base.DialogResult = true;
		Close();
	}

	private void Decline_Click(object sender, RoutedEventArgs e)
	{
		HasChoice = true;
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
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/characterconsentwindow.xaml", UriKind.Relative);
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
			txtCharacter = (TextBlock)target;
			break;
		case 2:
			((Button)target).Click += Decline_Click;
			break;
		case 3:
			((Button)target).Click += Accept_Click;
			break;
		default:
			_contentLoaded = true;
			break;
		}
	}
}
