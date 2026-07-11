using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Namter.Core.Interop;

public static class NativeCoreLimits
{
    public const uint NativeQueueCapacityMin = 64;
    public const uint NativeQueueCapacityMax = 1_048_576;
    public const uint MaxLiveFlowsMin = 1;
    public const uint MaxLiveFlowsMax = 1_048_576;
    public const uint MaxOutOfOrderBytesPerFlowMin = 1_024;
    public const uint MaxOutOfOrderBytesPerFlowMax = 67_108_864;
    public const uint MaxFrameBytesMin = 1_024;
    public const uint MaxFrameBytesMax = 16_777_216;
    public const uint MaxDecompressedBytesMin = 1_024;
    public const uint MaxDecompressedBytesMax = 67_108_864;
    public const uint ProtocolSnapshotMax = 16_777_216;
}

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
    uint NativeQueueCapacity = 1_024,
    uint MaxLiveFlows = 512,
    uint MaxOutOfOrderBytesPerFlow = 1_048_576,
    uint MaxFrameBytes = 1_048_576,
    uint MaxDecompressedBytes = 4_194_304);

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
