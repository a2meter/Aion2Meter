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
    Damage = 2,
    Dot = 3,
    Buff = 4,
    SelfActor = 5,
    OtherActor = 6,
    MobSpawn = 7,
    BossHp = 8,
    EntityRemoved = 9,
    Party = 10,
    Content = 11,
    CombatState = 12,
    UnknownProtocol = 13,
}

public enum NativeBuffOperation : byte
{
    Unknown = 0,
    Apply = 1,
    Refresh = 2,
    Remove = 3,
}

public enum NativeDiagnosticCode : uint
{
    IncompleteStream = 1,
    CaptureQueueOverflow = 2,
    CaptureBackendFailed = 3,
}

public sealed record NativeCoreConfig(
    uint NativeQueueCapacity = 1_024,
    uint MaxLiveFlows = 512,
    uint MaxOutOfOrderBytesPerFlow = 1_048_576,
    uint MaxFrameBytes = 1_048_576,
    uint MaxDecompressedBytes = 4_194_304);

public sealed record NativeEvent
{
    public NativeEvent() { }
    public NativeEvent(NativeEventKind kind, ImmutableArray<byte> payload) { Kind = kind; Payload = payload; }

    public NativeEventKind Kind { get; init; }
    public ulong FirstTimestampNs { get; init; }
    public ulong LastTimestampNs { get; init; }
    public ulong Epoch { get; init; }
    public ulong FirstFileOffset { get; init; }
    public ulong LastFileOffset { get; init; }
    public uint SourceAddress { get; init; }
    public uint DestinationAddress { get; init; }
    public ushort SourcePort { get; init; }
    public ushort DestinationPort { get; init; }
    public uint ActorId { get; init; }
    public uint TargetId { get; init; }
    public uint OwnerId { get; init; }
    public uint SkillId { get; init; }
    public uint BuffId { get; init; }
    public uint MobId { get; init; }
    public uint BossId { get; init; }
    public uint ContentId { get; init; }
    public uint DungeonId { get; init; }
    public uint PartyId { get; init; }
    public ushort ServerId { get; init; }
    public ushort JobId { get; init; }
    public ulong Damage { get; init; }
    public ulong MultiDamage { get; init; }
    public ulong Healing { get; init; }
    public ulong CurrentHp { get; init; }
    public ulong MaxHp { get; init; }
    public uint SpecialMask { get; init; }
    public uint DurationMs { get; init; }
    public byte State { get; init; }
    public byte Action { get; init; }
    public NativeBuffOperation BuffOperation { get; init; }
    public byte DamageType { get; init; }
    public bool IsDot { get; init; }
    public bool IsSelf { get; init; }
    public bool IsBoss { get; init; }
    public string Name { get; init; } = string.Empty;
    public ImmutableArray<byte> Payload { get; init; } = ImmutableArray<byte>.Empty;
}

public sealed record NativeDiagnostic(
    NativeDiagnosticCode Code,
    string Message,
    NativeSourceKind? Backend = null,
    uint StableError = 0,
    uint NativeError = 0,
    bool Incomplete = false,
    bool AutomaticAction = false,
    ulong Received = 0,
    ulong Dropped = 0,
    ulong InterfaceDropped = 0,
    ulong QueueHighWater = 0,
    string BackendName = "",
    string RuntimeVersion = "",
    string InterfaceIdentity = "",
    string HelpUrl = "");

public sealed class NativeCoreException : InvalidOperationException
{
    public NativeCoreException(uint statusCode, string message, string? helpUrl = null)
        : base(message) { StatusCode = statusCode; HelpUrl = helpUrl; }
    public uint StatusCode { get; }
    public string? HelpUrl { get; }
}

public sealed record NativeDiagnostics(
    ulong StartCount,
    ulong StopCount,
    ulong EmittedEventCount,
    ulong CapturedPacketCount,
    ulong DroppedCaptureCount,
    ulong InvalidPacketCount,
    ulong BackendReceived,
    ulong BackendDropped,
    ulong BackendInterfaceDropped,
    ulong QueueHighWater,
    bool Incomplete,
    ImmutableArray<NativeDiagnostic> ManagedDiagnostics);

internal enum NativeStatus
{
    Ok = 0,
    InvalidArgument = 1,
    AbiMismatch = 2,
    InvalidState = 3,
    InternalError = 4,
    NpcapNotInstalled = 5,
    BackendUnavailable = 6,
    BackendError = 7,
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
    internal uint Reserved;
    internal ulong FirstTimestampNs;
    internal ulong LastTimestampNs;
    internal ulong Epoch;
    internal ulong FirstFileOffset;
    internal ulong LastFileOffset;
    internal uint SourceAddress;
    internal uint DestinationAddress;
    internal ushort SourcePort;
    internal ushort DestinationPort;
    internal uint ActorId;
    internal uint TargetId;
    internal uint OwnerId;
    internal uint SkillId;
    internal uint BuffId;
    internal uint MobId;
    internal uint BossId;
    internal uint ContentId;
    internal uint DungeonId;
    internal uint PartyId;
    internal ushort ServerId;
    internal ushort JobId;
    internal ulong Damage;
    internal ulong MultiDamage;
    internal ulong Healing;
    internal ulong CurrentHp;
    internal ulong MaxHp;
    internal uint SpecialMask;
    internal uint DurationMs;
    internal byte State;
    internal byte Action;
    internal byte DamageType;
    internal byte IsDot;
    internal byte IsSelf;
    internal byte IsBoss;
    internal NativeBuffOperation BuffOperation;
    internal byte FlagsReserved;
    internal nint Name;
    internal nuint NameSize;
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
    internal uint BackendKind;
    internal uint StableError;
    internal uint NativeError;
    internal byte Incomplete;
    internal byte AutomaticAction;
    internal ushort Reserved;
    internal ulong Received;
    internal ulong Dropped;
    internal ulong InterfaceDropped;
    internal ulong QueueHighWater;
    internal nint BackendName;
    internal nuint BackendNameSize;
    internal nint RuntimeVersion;
    internal nuint RuntimeVersionSize;
    internal nint InterfaceIdentity;
    internal nuint InterfaceIdentitySize;
    internal nint HelpUrl;
    internal nuint HelpUrlSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDiagnosticsV1
{
    internal uint AbiVersion;
    internal uint StructSize;
    internal ulong StartCount;
    internal ulong StopCount;
    internal ulong EmittedEventCount;
    internal ulong CapturedPacketCount;
    internal ulong DroppedCaptureCount;
    internal ulong InvalidPacketCount;
    internal ulong BackendReceived;
    internal ulong BackendDropped;
    internal ulong BackendInterfaceDropped;
    internal ulong QueueHighWater;
    internal byte Incomplete;
}
