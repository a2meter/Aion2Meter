using System.Runtime.InteropServices;

namespace Namter.Core.Interop;

internal sealed class NativeCoreHandle : SafeHandle
{
    private static readonly nint TokenOnlySentinel = 1;

    private readonly Action<nint> _stop;
    private readonly Action<nint> _destroy;
    private GCHandle _callbackRoot;
    private bool _hasNativeHandle;

    internal NativeCoreHandle(
        object callbackTarget,
        Action<nint>? stop = null,
        Action<nint>? destroy = null)
        : base(IntPtr.Zero, ownsHandle: true)
    {
        ArgumentNullException.ThrowIfNull(callbackTarget);
        _stop = stop ?? StopNative;
        _destroy = destroy ?? NativeMethods.nm_core_destroy;
        _callbackRoot = GCHandle.Alloc(callbackTarget);
        SetHandle(TokenOnlySentinel);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    internal nint CallbackToken => GCHandle.ToIntPtr(_callbackRoot);

    internal object CallbackTarget => _callbackRoot.Target!;

    internal void Initialize(nint value)
    {
        if (_hasNativeHandle || value == IntPtr.Zero || value == new IntPtr(-1))
        {
            throw new InvalidOperationException("The native handle cannot be initialized.");
        }

        SetHandle(value);
        _hasNativeHandle = true;
    }

    protected override bool ReleaseHandle()
    {
        var succeeded = true;
        var nativeHandle = handle;

        if (_hasNativeHandle)
        {
            try
            {
                _stop(nativeHandle);
            }
            catch
            {
                succeeded = false;
            }

            try
            {
                _destroy(nativeHandle);
            }
            catch
            {
                succeeded = false;
            }
        }

        if (_callbackRoot.IsAllocated)
        {
            _callbackRoot.Free();
        }
        handle = IntPtr.Zero;
        return succeeded;
    }

    private static void StopNative(nint nativeHandle) =>
        _ = NativeMethods.nm_core_stop_raw(nativeHandle);
}
