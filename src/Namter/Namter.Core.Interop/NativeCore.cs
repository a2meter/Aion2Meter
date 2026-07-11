using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Namter.Core.Interop;

public sealed class NativeCore : IAsyncDisposable
{
    private const uint AbiVersion = 1;

    private readonly NativeCoreHandle _handle;
    private int _disposeState;

    public unsafe NativeCore(
        Action<NativeEvent>? eventCallback = null,
        Action<NativeDiagnostic>? diagnosticCallback = null,
        NativeCoreConfig? config = null)
    {
        if (NativeMethods.nm_core_abi_version() != AbiVersion)
        {
            throw new InvalidOperationException("The native Namter ABI version is not supported.");
        }

        var callbackState = new CallbackState(eventCallback, diagnosticCallback);
        _handle = new NativeCoreHandle(callbackState);

        try
        {
            var nativeConfig = CreateNativeConfig(config ?? new NativeCoreConfig());
            var callbacks = new NativeCallbacksV1
            {
                AbiVersion = AbiVersion,
                StructSize = (uint)sizeof(NativeCallbacksV1),
                User = _handle.CallbackToken,
                EventCallback = (nint)(delegate* unmanaged[Cdecl]<nint, NativeEventV1*, void>)&OnNativeEvent,
                DiagnosticCallback = (nint)(delegate* unmanaged[Cdecl]<nint, NativeDiagnosticV1*, void>)&OnNativeDiagnostic,
            };

            var status = NativeMethods.nm_core_create(nativeConfig, callbacks, out var handle);
            if (handle != IntPtr.Zero)
            {
                _handle.Initialize(handle);
            }
            ThrowIfFailed(status);
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("The native Namter core returned a null handle.");
            }
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    public unsafe void SetProtocolSnapshot(ReadOnlySpan<byte> snapshot)
    {
        ThrowIfDisposed();
        if (snapshot.IsEmpty)
        {
            throw new ArgumentException("The protocol snapshot cannot be empty.", nameof(snapshot));
        }

        fixed (byte* data = snapshot)
        {
            ThrowIfFailed(NativeMethods.nm_core_set_protocol_snapshot(
                _handle,
                data,
                checked((nuint)snapshot.Length)));
        }
    }

    public Task ReplayAsync(
        ReadOnlyMemory<byte> sourceData,
        CancellationToken cancellationToken = default) =>
        RunSourceAsync(NativeSourceKind.Pcap, sourceData, cancellationToken);

    public Task CaptureAsync(
        NativeSourceKind sourceKind,
        ReadOnlyMemory<byte> sourceData,
        CancellationToken cancellationToken = default)
    {
        if (sourceKind == NativeSourceKind.Pcap)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceKind),
                "Use ReplayAsync for PCAP input.");
        }

        return RunSourceAsync(sourceKind, sourceData, cancellationToken);
    }

    public NativeDiagnostics GetDiagnostics()
    {
        ThrowIfDisposed();
        var native = new NativeDiagnosticsV1
        {
            AbiVersion = AbiVersion,
            StructSize = (uint)Unsafe.SizeOf<NativeDiagnosticsV1>(),
        };
        ThrowIfFailed(NativeMethods.nm_core_get_diagnostics(_handle, ref native));

        return new NativeDiagnostics(
            native.StartCount,
            native.StopCount,
            native.EmittedEventCount,
            CurrentCallbackState.ManagedDiagnostics);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        _handle.Dispose();

        return ValueTask.CompletedTask;
    }

    internal unsafe void InvokeEventCallbackForTesting(ref NativeEventV1 nativeEvent)
    {
        ThrowIfDisposed();
        fixed (NativeEventV1* pointer = &nativeEvent)
        {
            ((delegate* unmanaged[Cdecl]<nint, NativeEventV1*, void>)&OnNativeEvent)(
                _handle.CallbackToken,
                pointer);
        }
    }

    private CallbackState CurrentCallbackState => (CallbackState)_handle.CallbackTarget;

    private async Task RunSourceAsync(
        NativeSourceKind sourceKind,
        ReadOnlyMemory<byte> sourceData,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        StartSource(sourceKind, sourceData);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (Volatile.Read(ref _disposeState) == 0)
            {
                try
                {
                    ThrowIfFailed(NativeMethods.nm_core_stop(_handle));
                }
                catch (ObjectDisposedException) when (Volatile.Read(ref _disposeState) != 0)
                {
                    // Concurrent disposal owns the stop/destroy sequence.
                }
            }
        }
    }

    private unsafe void StartSource(NativeSourceKind sourceKind, ReadOnlyMemory<byte> sourceData)
    {
        var ownedSourceData = sourceData.ToArray();
        fixed (byte* data = ownedSourceData)
        {
            var source = new NativeSourceConfigV1
            {
                AbiVersion = AbiVersion,
                StructSize = (uint)sizeof(NativeSourceConfigV1),
                Kind = sourceKind,
                SourceData = ownedSourceData.Length == 0 ? 0 : (nint)data,
                SourceDataSize = checked((nuint)ownedSourceData.Length),
            };
            ThrowIfFailed(NativeMethods.nm_core_start(_handle, source));
        }
    }

    private static NativeCoreConfigV1 CreateNativeConfig(NativeCoreConfig config) => new()
    {
        AbiVersion = AbiVersion,
        StructSize = (uint)Unsafe.SizeOf<NativeCoreConfigV1>(),
        NativeQueueCapacity = config.NativeQueueCapacity,
        MaxLiveFlows = config.MaxLiveFlows,
        MaxOutOfOrderBytesPerFlow = config.MaxOutOfOrderBytesPerFlow,
        MaxFrameBytes = config.MaxFrameBytes,
        MaxDecompressedBytes = config.MaxDecompressedBytes,
    };

    private static void ThrowIfFailed(NativeStatus status)
    {
        if (status != NativeStatus.Ok)
        {
            throw new InvalidOperationException($"The native Namter core returned {status}.");
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnNativeEvent(nint user, NativeEventV1* nativeEvent)
    {
        CallbackState? state = null;
        try
        {
            state = GetCallbackState(user);
            if (nativeEvent == null || nativeEvent->AbiVersion != AbiVersion ||
                nativeEvent->StructSize < sizeof(NativeEventV1))
            {
                throw new InvalidOperationException("The native event record has an incompatible layout.");
            }

            var nameBytes = CopyBytes(nativeEvent->Name, nativeEvent->NameSize);
            var payload = CopyBytes(nativeEvent->Payload, nativeEvent->PayloadSize);
            state.EventCallback?.Invoke(new NativeEvent
            {
                Kind = nativeEvent->Kind,
                FirstTimestampNs = nativeEvent->FirstTimestampNs,
                LastTimestampNs = nativeEvent->LastTimestampNs,
                Epoch = nativeEvent->Epoch,
                FirstFileOffset = nativeEvent->FirstFileOffset,
                LastFileOffset = nativeEvent->LastFileOffset,
                SourceAddress = nativeEvent->SourceAddress,
                DestinationAddress = nativeEvent->DestinationAddress,
                SourcePort = nativeEvent->SourcePort,
                DestinationPort = nativeEvent->DestinationPort,
                ActorId = nativeEvent->ActorId,
                TargetId = nativeEvent->TargetId,
                OwnerId = nativeEvent->OwnerId,
                SkillId = nativeEvent->SkillId,
                BuffId = nativeEvent->BuffId,
                MobId = nativeEvent->MobId,
                BossId = nativeEvent->BossId,
                ContentId = nativeEvent->ContentId,
                DungeonId = nativeEvent->DungeonId,
                PartyId = nativeEvent->PartyId,
                ServerId = nativeEvent->ServerId,
                JobId = nativeEvent->JobId,
                Damage = nativeEvent->Damage,
                MultiDamage = nativeEvent->MultiDamage,
                Healing = nativeEvent->Healing,
                CurrentHp = nativeEvent->CurrentHp,
                MaxHp = nativeEvent->MaxHp,
                SpecialMask = nativeEvent->SpecialMask,
                DurationMs = nativeEvent->DurationMs,
                State = nativeEvent->State,
                Action = nativeEvent->Action,
                DamageType = nativeEvent->DamageType,
                IsDot = nativeEvent->IsDot != 0,
                IsSelf = nativeEvent->IsSelf != 0,
                IsBoss = nativeEvent->IsBoss != 0,
                Name = Encoding.UTF8.GetString(nameBytes.AsSpan()),
                Payload = payload,
            });
        }
        catch (Exception exception)
        {
            state?.ReportCallbackException(exception);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnNativeDiagnostic(nint user, NativeDiagnosticV1* nativeDiagnostic)
    {
        CallbackState? state = null;
        try
        {
            state = GetCallbackState(user);
            if (nativeDiagnostic == null || nativeDiagnostic->AbiVersion != AbiVersion ||
                nativeDiagnostic->StructSize < sizeof(NativeDiagnosticV1))
            {
                throw new InvalidOperationException("The native diagnostic record has an incompatible layout.");
            }

            var messageBytes = CopyBytes(nativeDiagnostic->Message, nativeDiagnostic->MessageSize);
            state.ReportNativeDiagnostic(new NativeDiagnostic(
                nativeDiagnostic->Code,
                Encoding.UTF8.GetString(messageBytes.AsSpan())));
        }
        catch (Exception exception)
        {
            state?.ReportCallbackException(exception);
        }
    }

    private static CallbackState GetCallbackState(nint user)
    {
        if (user == 0 || GCHandle.FromIntPtr(user).Target is not CallbackState state)
        {
            throw new InvalidOperationException("The native callback token is invalid.");
        }

        return state;
    }

    private static unsafe ImmutableArray<byte> CopyBytes(nint data, nuint size)
    {
        if (size == 0)
        {
            return ImmutableArray<byte>.Empty;
        }
        if (data == 0)
        {
            throw new InvalidOperationException("A native pointer/length view has a null pointer.");
        }

        var copy = new ReadOnlySpan<byte>((void*)data, checked((int)size)).ToArray();
        return ImmutableArray.Create(copy);
    }

    private sealed class CallbackState(
        Action<NativeEvent>? eventCallback,
        Action<NativeDiagnostic>? diagnosticCallback)
    {
        private readonly ConcurrentQueue<NativeDiagnostic> _managedDiagnostics = new();

        internal Action<NativeEvent>? EventCallback { get; } = eventCallback;

        internal ImmutableArray<NativeDiagnostic> ManagedDiagnostics =>
            _managedDiagnostics.ToArray().ToImmutableArray();

        internal void ReportNativeDiagnostic(NativeDiagnostic diagnostic)
        {
            _managedDiagnostics.Enqueue(diagnostic);
            diagnosticCallback?.Invoke(diagnostic);
        }

        internal void ReportCallbackException(Exception exception)
        {
            var diagnostic = new NativeDiagnostic(
                NativeDiagnosticCode.IncompleteStream,
                exception.Message);
            _managedDiagnostics.Enqueue(diagnostic);
            try
            {
                diagnosticCallback?.Invoke(diagnostic);
            }
            catch
            {
                // A callback failure must never escape an unmanaged entry point.
            }
        }
    }
}
