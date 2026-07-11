using System.Runtime.InteropServices;

namespace Namter.Core.Interop;

internal static partial class NativeMethods
{
    [LibraryImport("Namter.Core.Native", EntryPoint = "nm_core_abi_version")]
    internal static partial uint nm_core_abi_version();
}
