using Namter.Core.Interop;

namespace Namter.Tests.Unit.Interop;

public sealed class NativeLoadTests
{
    [Fact]
    public void NativeLibrary_reports_supported_abi() =>
        Assert.Equal(1u, NativeMethods.nm_core_abi_version());
}
