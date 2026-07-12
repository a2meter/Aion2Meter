using System.Runtime.CompilerServices;
using System.Text;
using Namter.Core.Interop;

namespace Namter.Tests.Unit.Interop;

public sealed class NativeDiagnosticTests
{
    [Fact]
    public async Task Structured_diagnostic_strings_are_copied_during_callback()
    {
        NativeDiagnostic? seen = null;
        var core = new NativeCore(diagnosticCallback: value => seen = value);
        var message = Encoding.UTF8.GetBytes("missing");
        var backend = Encoding.UTF8.GetBytes("npcap");
        var version = Encoding.UTF8.GetBytes("Npcap 1.x");
        var adapter = Encoding.UTF8.GetBytes("NPF_TEST");
        var help = Encoding.UTF8.GetBytes("https://npcap.com/#download");
        InvokeNativeDiagnostic(core, message, backend, version, adapter, help);
        Array.Fill(message, (byte)'x'); Array.Fill(backend, (byte)'x');
        Array.Fill(version, (byte)'x'); Array.Fill(adapter, (byte)'x');
        Array.Fill(help, (byte)'x');
        Assert.NotNull(seen); Assert.Equal("missing", seen.Message);
        Assert.Equal(NativeSourceKind.Npcap, seen.Backend);
        Assert.Equal("npcap", seen.BackendName); Assert.Equal("Npcap 1.x", seen.RuntimeVersion);
        Assert.Equal("NPF_TEST", seen.InterfaceIdentity);
        Assert.Equal("https://npcap.com/#download", seen.HelpUrl);
        Assert.True(seen.Incomplete); Assert.False(seen.AutomaticAction);
        await core.DisposeAsync();
    }

    private static unsafe void InvokeNativeDiagnostic(
        NativeCore core,
        byte[] message,
        byte[] backend,
        byte[] version,
        byte[] adapter,
        byte[] help)
    {
        fixed (byte* m = message, b = backend, v = version, a = adapter, h = help)
        {
            var native = new NativeDiagnosticV1
            {
                AbiVersion = 1, StructSize = (uint)Unsafe.SizeOf<NativeDiagnosticV1>(),
                Code = NativeDiagnosticCode.CaptureBackendFailed,
                Message = (nint)m, MessageSize = (nuint)message.Length,
                BackendKind = (uint)NativeSourceKind.Npcap, StableError = 7,
                Incomplete = 1, AutomaticAction = 0, Received = 10, Dropped = 2,
                BackendName = (nint)b, BackendNameSize = (nuint)backend.Length,
                RuntimeVersion = (nint)v, RuntimeVersionSize = (nuint)version.Length,
                InterfaceIdentity = (nint)a, InterfaceIdentitySize = (nuint)adapter.Length,
                HelpUrl = (nint)h, HelpUrlSize = (nuint)help.Length,
            };
            core.InvokeDiagnosticCallbackForTesting(ref native);
        }
    }

    [Fact]
    public void Npcap_missing_exception_exposes_official_help_programmatically()
    {
        var exception = new NativeCoreException(5, "missing", "https://npcap.com/#download");
        Assert.Equal(5u, exception.StatusCode);
        Assert.Equal("https://npcap.com/#download", exception.HelpUrl);
    }

    [Fact]
    public async Task Managed_diagnostic_retention_is_bounded_and_suppression_is_counted()
    {
        var core=new NativeCore();byte[] message=Encoding.UTF8.GetBytes("loss");byte[] empty=[];
        for(int i=0;i<10_005;i++)InvokeNativeDiagnostic(core,message,empty,empty,empty,empty);
        NativeDiagnostics diagnostics=core.GetDiagnostics();Assert.Equal(10_000,diagnostics.ManagedDiagnostics.Length);Assert.Equal(5UL,diagnostics.SuppressedManagedDiagnosticCount);await core.DisposeAsync();
    }
}
