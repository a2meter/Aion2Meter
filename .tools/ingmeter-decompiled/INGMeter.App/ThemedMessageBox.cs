using System.Windows;

namespace INGMeter.App;

public static class ThemedMessageBox
{
	public static MessageBoxResult Show(string message, string caption = "알림", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Asterisk)
	{
		return Show(Application.Current?.MainWindow, message, caption, buttons, image);
	}

	public static MessageBoxResult Show(Window? owner, string message, string caption = "알림", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Asterisk)
	{
		ThemedMessageBoxWindow themedMessageBoxWindow = new ThemedMessageBoxWindow(message, caption, buttons, image);
		if (owner != null && owner.IsVisible)
		{
			themedMessageBoxWindow.Owner = owner;
		}
		themedMessageBoxWindow.ShowDialog();
		if (themedMessageBoxWindow.Result != MessageBoxResult.None)
		{
			return themedMessageBoxWindow.Result;
		}
		return MessageBoxResult.Cancel;
	}
}
