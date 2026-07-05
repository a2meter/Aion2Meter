using System;
using System.CodeDom.Compiler;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using Velopack;

namespace INGMeter.App;

public class App : Application, IComponentConnector
{
	private const string SingleInstanceMutexName = "INGMeter_Unique_Mutex_Name";

	private const string ShowExistingInstanceEventName = "INGMeter_Show_Existing_Instance_Event";

	private static Mutex? _mutex;

	private static EventWaitHandle? _showExistingInstanceEvent;

	private static RegisteredWaitHandle? _showExistingInstanceRegistration;

	private bool _contentLoaded;

	[STAThread]
	private static void Main(string[] args)
	{
		try
		{
			VelopackApp.Build().SetArgs(args).OnAfterInstallFastCallback(delegate
			{
				MigrateUserDataForVelopackHook("AfterInstall");
			})
				.OnAfterUpdateFastCallback(delegate
				{
					MigrateUserDataForVelopackHook("AfterUpdate");
				})
				.OnBeforeUpdateFastCallback(delegate
				{
					MigrateUserDataForVelopackHook("BeforeUpdate");
				})
				.Run();
		}
		catch (Exception ex)
		{
			WriteCrashLog("VelopackStartupException", ex);
		}
		if (!IsVelopackHookInvocation(args) && !TryRequestExistingInstanceLocate() && !TryRelaunchAsAdministrator(args))
		{
			App app = new App();
			app.InitializeComponent();
			app.RegisterExceptionLogging();
			app.Run();
		}
	}

	private static bool IsVelopackHookInvocation(string[] args)
	{
		return args.Any((string arg) => string.Equals(arg, "--veloapp-install", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--veloapp-updated", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--veloapp-obsolete", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--veloapp-uninstall", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--squirrel-install", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--squirrel-updated", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--squirrel-obsolete", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "--squirrel-uninstall", StringComparison.OrdinalIgnoreCase));
	}

	private static void MigrateUserDataForVelopackHook(string hookName)
	{
		try
		{
			AppPaths.MigrateUserDataFromAppDirectory();
		}
		catch (Exception ex)
		{
			WriteCrashLog("Velopack" + hookName + "UserDataMigrationFailed", ex);
		}
	}

	private static bool TryRelaunchAsAdministrator(string[] args)
	{
		if (IsAdministrator())
		{
			return false;
		}
		try
		{
			string text = Environment.ProcessPath;
			if (string.IsNullOrWhiteSpace(text))
			{
				text = Process.GetCurrentProcess().MainModule?.FileName;
			}
			if (string.IsNullOrWhiteSpace(text))
			{
				return false;
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = text,
				UseShellExecute = true,
				Verb = "runas",
				WorkingDirectory = AppContext.BaseDirectory
			};
			foreach (string item in args)
			{
				processStartInfo.ArgumentList.Add(item);
			}
			Process.Start(processStartInfo);
			return true;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			WriteCrashLog("AdministratorRelaunchCanceled", ex);
			return false;
		}
		catch (Exception ex2)
		{
			WriteCrashLog("AdministratorRelaunchFailed", ex2);
			return false;
		}
	}

	private static bool IsAdministrator()
	{
		using WindowsIdentity ntIdentity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(ntIdentity).IsInRole(WindowsBuiltInRole.Administrator);
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		_showExistingInstanceEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, "INGMeter_Show_Existing_Instance_Event");
		_mutex = new Mutex(initiallyOwned: true, "INGMeter_Unique_Mutex_Name", out var createdNew);
		if (!createdNew)
		{
			TryRequestExistingInstanceLocate();
			_mutex.Dispose();
			_mutex = null;
			_showExistingInstanceEvent.Dispose();
			_showExistingInstanceEvent = null;
			Application.Current.Shutdown();
			return;
		}
		base.OnStartup(e);
		base.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		if (!NativeIntegrity.VerifyParser(out string detail))
		{
			WriteCrashLog("NativeIntegrityFailed", detail);
			ThemedMessageBox.Show("A required program file is damaged. Please reinstall INGMeter.", "INGMeter integrity check failed", MessageBoxButton.OK, MessageBoxImage.Hand);
			_mutex?.ReleaseMutex();
			_mutex?.Dispose();
			Application.Current.Shutdown();
			return;
		}
		AppPaths.MigrateUserDataFromAppDirectory();
		if (!PrivacyConsentManager.EnsureAccepted())
		{
			_mutex?.ReleaseMutex();
			_mutex?.Dispose();
			Application.Current.Shutdown();
		}
		else
		{
			MainWindow mainWindow = (MainWindow)(base.MainWindow = new MainWindow());
			RegisterExistingInstanceLocateRequest(mainWindow);
			mainWindow.Show();
			base.ShutdownMode = ShutdownMode.OnLastWindowClose;
			VerifyStartupAuthorizationAsync(mainWindow);
		}
	}

	private void RegisterExceptionLogging()
	{
		base.DispatcherUnhandledException += delegate(object _, DispatcherUnhandledExceptionEventArgs e)
		{
			WriteCrashLog("DispatcherUnhandledException", e.Exception);
		};
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, UnhandledExceptionEventArgs e)
		{
			if (e.ExceptionObject is Exception ex)
			{
				WriteCrashLog("UnhandledException", ex);
			}
			else
			{
				WriteCrashLog("UnhandledException", e.ExceptionObject?.ToString() ?? "Unknown exception");
			}
		};
		TaskScheduler.UnobservedTaskException += delegate(object? _, UnobservedTaskExceptionEventArgs e)
		{
			WriteCrashLog("UnobservedTaskException", e.Exception);
			e.SetObserved();
		};
	}

	private static bool TryRequestExistingInstanceLocate()
	{
		try
		{
			if (_showExistingInstanceEvent != null)
			{
				_showExistingInstanceEvent.Set();
				return true;
			}
			using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting("INGMeter_Show_Existing_Instance_Event");
			eventWaitHandle.Set();
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void RegisterExistingInstanceLocateRequest(MainWindow mainWindow)
	{
		if (_showExistingInstanceEvent != null)
		{
			_showExistingInstanceRegistration = ThreadPool.RegisterWaitForSingleObject(_showExistingInstanceEvent, delegate
			{
				mainWindow.Dispatcher.BeginInvoke(new Action(mainWindow.LocateFromExternalActivation));
			}, null, -1, executeOnlyOnce: false);
		}
	}

	private static void WriteCrashLog(string kind, Exception ex)
	{
		WriteCrashLog(kind, ex.ToString());
	}

	private static void WriteCrashLog(string kind, string detail)
	{
		try
		{
			string logsDirectory = AppPaths.LogsDirectory;
			Directory.CreateDirectory(logsDirectory);
			string path = Path.Combine(logsDirectory, "crash.log");
			StringBuilder stringBuilder = new StringBuilder();
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(3, 2, stringBuilder);
			handler.AppendLiteral("[");
			handler.AppendFormatted(DateTime.Now, "yyyy-MM-dd HH:mm:ss");
			handler.AppendLiteral("] ");
			handler.AppendFormatted(kind);
			StringBuilder stringBuilder2 = stringBuilder.AppendLine(ref handler).AppendLine(detail).AppendLine();
			File.AppendAllText(path, stringBuilder2.ToString());
		}
		catch
		{
		}
	}

	private async Task VerifyStartupAuthorizationAsync(Window mainWindow)
	{
		if (await AuthManager.CheckStartupAsync())
		{
			return;
		}
		await base.Dispatcher.InvokeAsync(delegate
		{
			base.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			try
			{
				mainWindow.Close();
			}
			catch
			{
			}
			Application.Current.Shutdown();
		});
	}

	protected override void OnExit(ExitEventArgs e)
	{
		try
		{
			_showExistingInstanceRegistration?.Unregister(null);
			_showExistingInstanceRegistration = null;
			_showExistingInstanceEvent?.Dispose();
			_showExistingInstanceEvent = null;
			_mutex?.ReleaseMutex();
			_mutex?.Dispose();
			_mutex = null;
		}
		catch
		{
		}
		base.OnExit(e);
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("/INGMeter;V1.6.3.0;component/app.xaml", UriKind.Relative);
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.5.0")]
	[EditorBrowsable(EditorBrowsableState.Never)]
	void IComponentConnector.Connect(int connectionId, object target)
	{
		_contentLoaded = true;
	}
}
