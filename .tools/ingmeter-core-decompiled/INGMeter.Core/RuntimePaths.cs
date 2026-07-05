using System;
using System.IO;

namespace INGMeter.Core;

public static class RuntimePaths
{
	private static readonly Lazy<string> InstallRootDirectoryValue = new Lazy<string>(ResolveInstallRootDirectory);

	private static readonly Lazy<string> AppRootDirectoryValue = new Lazy<string>(ResolveAppRootDirectory);

	public static string InstallRootDirectory => EnsureDirectory(InstallRootDirectoryValue.Value);

	public static string ExecutableDirectory => Path.GetFullPath(AppContext.BaseDirectory);

	public static string AppRootDirectory => EnsureDirectory(AppRootDirectoryValue.Value);

	public static string LogsDirectory => EnsureDirectory(Path.Combine(AppRootDirectory, "Logs"));

	public static string EncounterLogsDirectory => EnsureDirectory(Path.Combine(AppRootDirectory, "EncounterLogs"));

	public static string GetLogFilePath(string fileName)
	{
		return Path.Combine(LogsDirectory, Path.GetFileName(fileName));
	}

	private static string ResolveAppRootDirectory()
	{
		return Path.Combine(InstallRootDirectoryValue.Value, "UserData");
	}

	private static string ResolveInstallRootDirectory()
	{
		DirectoryInfo directoryInfo = new DirectoryInfo(Path.GetFullPath(AppContext.BaseDirectory));
		if ((directoryInfo.Name.Equals("current", StringComparison.OrdinalIgnoreCase) || directoryInfo.Name.StartsWith("app-", StringComparison.OrdinalIgnoreCase)) && directoryInfo.Parent != null)
		{
			return directoryInfo.Parent.FullName;
		}
		return directoryInfo.FullName;
	}

	private static string EnsureDirectory(string path)
	{
		Directory.CreateDirectory(path);
		return Path.GetFullPath(path);
	}
}
