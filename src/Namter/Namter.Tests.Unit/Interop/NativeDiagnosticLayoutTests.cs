using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Namter.Core.Interop;

namespace Namter.Tests.Unit.Interop;

public sealed class NativeDiagnosticLayoutTests
{
    [Fact]
    public void Diagnostic_layout_matches_frozen_x64_native_abi()
    {
        Assert.Equal(144, Unsafe.SizeOf<NativeDiagnosticV1>());
        AssertOffsets<NativeDiagnosticV1>(
            (nameof(NativeDiagnosticV1.AbiVersion), 0), (nameof(NativeDiagnosticV1.StructSize), 4),
            (nameof(NativeDiagnosticV1.Code), 8), (nameof(NativeDiagnosticV1.Message), 16),
            (nameof(NativeDiagnosticV1.MessageSize), 24), (nameof(NativeDiagnosticV1.BackendKind), 32),
            (nameof(NativeDiagnosticV1.StableError), 36), (nameof(NativeDiagnosticV1.NativeError), 40),
            (nameof(NativeDiagnosticV1.Incomplete), 44), (nameof(NativeDiagnosticV1.AutomaticAction), 45),
            (nameof(NativeDiagnosticV1.Reserved), 46), (nameof(NativeDiagnosticV1.Received), 48),
            (nameof(NativeDiagnosticV1.Dropped), 56), (nameof(NativeDiagnosticV1.InterfaceDropped), 64),
            (nameof(NativeDiagnosticV1.QueueHighWater), 72), (nameof(NativeDiagnosticV1.BackendName), 80),
            (nameof(NativeDiagnosticV1.BackendNameSize), 88), (nameof(NativeDiagnosticV1.RuntimeVersion), 96),
            (nameof(NativeDiagnosticV1.RuntimeVersionSize), 104), (nameof(NativeDiagnosticV1.InterfaceIdentity), 112),
            (nameof(NativeDiagnosticV1.InterfaceIdentitySize), 120), (nameof(NativeDiagnosticV1.HelpUrl), 128),
            (nameof(NativeDiagnosticV1.HelpUrlSize), 136));
    }

    [Fact]
    public void Diagnostics_layout_matches_frozen_x64_native_abi()
    {
        Assert.Equal(96, Unsafe.SizeOf<NativeDiagnosticsV1>());
        AssertOffsets<NativeDiagnosticsV1>(
            (nameof(NativeDiagnosticsV1.AbiVersion), 0), (nameof(NativeDiagnosticsV1.StructSize), 4),
            (nameof(NativeDiagnosticsV1.StartCount), 8), (nameof(NativeDiagnosticsV1.StopCount), 16),
            (nameof(NativeDiagnosticsV1.EmittedEventCount), 24), (nameof(NativeDiagnosticsV1.CapturedPacketCount), 32),
            (nameof(NativeDiagnosticsV1.DroppedCaptureCount), 40), (nameof(NativeDiagnosticsV1.InvalidPacketCount), 48),
            (nameof(NativeDiagnosticsV1.BackendReceived), 56), (nameof(NativeDiagnosticsV1.BackendDropped), 64),
            (nameof(NativeDiagnosticsV1.BackendInterfaceDropped), 72), (nameof(NativeDiagnosticsV1.QueueHighWater), 80),
            (nameof(NativeDiagnosticsV1.Incomplete), 88));
    }

    private static void AssertOffsets<T>(params (string Field, int Offset)[] expected)
    {
        foreach (var (field, offset) in expected)
            Assert.Equal(offset, Marshal.OffsetOf<T>(field).ToInt32());
    }
}
