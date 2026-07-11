using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Namter.Core.Interop;

public enum NativeSourceKind : uint
{
    WinDivert = 1,
    Npcap = 2,
    Pcap = 3,
}

public enum NativeEventKind : uint
{
    SourceStarted = 1,
}

public enum NativeDiagnosticCode : uint
{
    IncompleteStream = 1,
}

public sealed record NativeCoreConfig(
    uint NativeQueueCapacity = 1024,
    uint MaxLiveFlows = 512,
    uint MaxOutOfOrderBytesPerFlow = 1024 * 1024,
    uint MaxFrameBytes = 1024 * 1024,
    uint MaxDecompressedBytes = 4 * 1024 * 1024);

public sealed record NativeEvent(NativeEventKind Kind, ImmutableArray<byte> Payload);

public sealed record NativeDiagnostic(NativeDiagnosticCode Code, string Message);

public sealed record NativeDiagnostics(
    ulong StartCount,
    ulong StopCount,
    ulong EmittedEventCount,
    ImmutableArray<NativeDiagnostic> ManagedDiagnostics);

internal enum NativeStatus
{
    Ok = 0,
    InvalidArgument = 1,
    AbiMismatch = 2,
    InvalidState = 3,
    InternalError = 4,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCoreConfigV1
{
    internal uint AbiVersion;
    internal uint StructSize;
    internal uint NativeQueueCapacity;
    internal uint MaxLiveFlows;
    internal uint MaxOutOfOrderBytesPerFlow;
    internal uint MaxFrameBytes;
    internal uint MaxDecompressedBytes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCallbacksV1
{
    internal uint AbiVersion;
    internal uint StructSize;
    internal nint User;
    internal nint EventCallback;
    internal nint DiagnosticCallback;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeSourceConfigV1
{
    internal uint AbiVersion;
    internal uint StructSize;
    internal NativeSourceKind Kind;
    internal nint SourceData;
    internal nuint SourceDataSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEventV1
{
    internal uint AbiVersion;
    internal uint StructSize;
    internal NativeEventKind Kind;
    internal nint Payload;
    internal nuint PayloadSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDiagnosticV1
{
    internal uint AbiVersion;
    internal uint StructSize;
    internal NativeDiagnosticCode Code;
    internal nint Message;
    internal nuint MessageSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDiagnosticsV1
{
    internal uint AbiVersion;
    internal uint StructSize;
    internal ulong StartCount;
    internal ulong StopCount;
    internal ulong EmittedEventCount;
}
