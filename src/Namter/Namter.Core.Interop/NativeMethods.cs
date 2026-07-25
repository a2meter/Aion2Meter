using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Namter.Core.Interop;

internal static unsafe partial class NativeMethods
{
    private const string LibraryName = "Namter.Core.Native";

    [LibraryImport(LibraryName, EntryPoint = "nm_core_abi_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint nm_core_abi_version();

    [LibraryImport(LibraryName, EntryPoint = "nm_core_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus nm_core_create(
        in NativeCoreConfigV1 config,
        in NativeCallbacksV1 callbacks,
        out nint handle);

    [LibraryImport(LibraryName, EntryPoint = "nm_core_set_protocol_snapshot")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus nm_core_set_protocol_snapshot(
        NativeCoreHandle handle,
        byte* data,
        nuint size);

    [LibraryImport(LibraryName, EntryPoint = "nm_core_set_packet_log")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus nm_core_set_packet_log(
        NativeCoreHandle handle,
        byte* directory,
        nuint size);

    [LibraryImport(LibraryName, EntryPoint = "nm_core_start")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus nm_core_start(
        NativeCoreHandle handle,
        in NativeSourceConfigV1 source);

    [LibraryImport(LibraryName, EntryPoint = "nm_core_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus nm_core_stop(NativeCoreHandle handle);

    [LibraryImport(LibraryName, EntryPoint = "nm_core_stop")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus nm_core_stop_raw(nint handle);

    [LibraryImport(LibraryName, EntryPoint = "nm_core_get_diagnostics")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial NativeStatus nm_core_get_diagnostics(
        NativeCoreHandle handle,
        ref NativeDiagnosticsV1 diagnostics);

    [LibraryImport(LibraryName, EntryPoint = "nm_core_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void nm_core_destroy(nint handle);
}
