using Microsoft.Win32.SafeHandles;

namespace Namter.Core.Interop;

internal sealed class NativeCoreHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly Action<nint> _release;

    internal NativeCoreHandle(nint value, Action<nint>? release = null)
        : base(ownsHandle: true)
    {
        _release = release ?? NativeMethods.nm_core_destroy;
        SetHandle(value);
    }

    protected override bool ReleaseHandle()
    {
        try
        {
            _release(handle);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
