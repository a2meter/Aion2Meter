using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Namter.GameData.Publisher;

internal static class WindowsPathIdentity
{
    private const uint FileFlagBackupSemantics = 0x02000000;
    public static bool IsSameOrDescendant(string candidate, string root)
    {
        string resolvedCandidate = Resolve(candidate);
        string resolvedRoot = Path.TrimEndingDirectorySeparator(Resolve(root));
        string normalizedCandidate = Path.TrimEndingDirectorySeparator(resolvedCandidate);
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedCandidate, resolvedRoot, comparison)
            || normalizedCandidate.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static string Resolve(string path)
    {
        string full = Path.GetFullPath(path);
        if (!OperatingSystem.IsWindows()) return full;

        var missing = new Stack<string>();
        string existing = full;
        while (!File.Exists(existing) && !Directory.Exists(existing))
        {
            string? name = Path.GetFileName(existing);
            string? parent = Path.GetDirectoryName(existing);
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(parent)) break;
            missing.Push(name);
            existing = parent;
        }

        string resolved = ResolveExisting(existing);
        while (missing.TryPop(out string? component)) resolved = Path.Combine(resolved, component);
        return Path.GetFullPath(resolved);
    }

    private static string ResolveExisting(string path)
    {
        using SafeFileHandle handle = CreateFile(
            path,
            0,
            FileShare.Read | FileShare.Write | FileShare.Delete,
            IntPtr.Zero,
            FileMode.Open,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid) throw new IOException($"Could not resolve final path identity: {Marshal.GetLastWin32Error()}.");
        var buffer = new StringBuilder(32768);
        uint length = GetFinalPathNameByHandle(handle, buffer, checked((uint)buffer.Capacity), 0);
        if (length == 0 || length >= buffer.Capacity)
            throw new IOException($"Could not read final path identity: {Marshal.GetLastWin32Error()}.");
        string value = buffer.ToString();
        return value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            ? @"\\" + value[8..]
            : value.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ? value[4..] : value;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        IntPtr securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        uint flags);
}
