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
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private int _disposeState;
    [ThreadStatic]
    private static int s_callbackDepth;

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
            native.CapturedPacketCount,
            native.DroppedCaptureCount,
            native.InvalidPacketCount,
            native.BackendReceived,
            native.BackendDropped,
            native.BackendInterfaceDropped,
            native.QueueHighWater,
            native.Incomplete != 0,
            CurrentCallbackState.ManagedDiagnostics,
            CurrentCallbackState.SuppressedDiagnosticCount);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return ValueTask.CompletedTask;
        }

        if (s_callbackDepth != 0)
        {
            ThreadPool.QueueUserWorkItem(_ => _handle.Dispose());
        }
        else
        {
            _handle.Dispose();
        }

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

    internal unsafe void InvokeDiagnosticCallbackForTesting(ref NativeDiagnosticV1 diagnostic)
    {
        ThrowIfDisposed();
        fixed (NativeDiagnosticV1* pointer = &diagnostic)
        {
            ((delegate* unmanaged[Cdecl]<nint, NativeDiagnosticV1*, void>)&OnNativeDiagnostic)(
                _handle.CallbackToken, pointer);
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
        if (!await _runGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("A Namter core source is already running.");
        try
        {
            Task sourceCompleted = CurrentCallbackState.PrepareSourceCompletion();
            StartSource(sourceKind, sourceData);
            try
            {
                await sourceCompleted.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (Volatile.Read(ref _disposeState) == 0)
                {
                    try { ThrowIfFailed(NativeMethods.nm_core_stop(_handle)); }
                    catch (ObjectDisposedException) when (Volatile.Read(ref _disposeState) != 0) { }
                }
            }
        }
        finally { _runGate.Release(); }
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
            throw new NativeCoreException(
                (uint)status,
                $"The native Namter core returned {status}.",
                status == NativeStatus.NpcapNotInstalled ? "https://npcap.com/#download" : null);
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnNativeEvent(nint user, NativeEventV1* nativeEvent)
    {
        ++s_callbackDepth;
        CallbackState? state = null;
        try
        {
            state = GetCallbackState(user);
            if (nativeEvent == null || nativeEvent->AbiVersion != AbiVersion ||
                nativeEvent->StructSize < sizeof(NativeEventV1))
            {
                throw new InvalidOperationException("The native event record has an incompatible layout.");
            }

            if (nativeEvent->Kind == NativeEventKind.SourceCompleted)
            {
                state.SignalSourceCompleted();
                return;
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
                BuffOperation = nativeEvent->BuffOperation,
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
        finally
        {
            --s_callbackDepth;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void OnNativeDiagnostic(nint user, NativeDiagnosticV1* nativeDiagnostic)
    {
        ++s_callbackDepth;
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
            static string CopyText(byte* pointer, nuint size) =>
                Encoding.UTF8.GetString(NativeCore.CopyBytes((nint)pointer, size).AsSpan());
            state.ReportNativeDiagnostic(new NativeDiagnostic(
                nativeDiagnostic->Code,
                Encoding.UTF8.GetString(messageBytes.AsSpan()),
                nativeDiagnostic->BackendKind == 0 ? null : (NativeSourceKind)nativeDiagnostic->BackendKind,
                nativeDiagnostic->StableError, nativeDiagnostic->NativeError,
                nativeDiagnostic->Incomplete != 0, nativeDiagnostic->AutomaticAction != 0,
                nativeDiagnostic->Received, nativeDiagnostic->Dropped,
                nativeDiagnostic->InterfaceDropped, nativeDiagnostic->QueueHighWater,
                CopyText((byte*)nativeDiagnostic->BackendName, nativeDiagnostic->BackendNameSize),
                CopyText((byte*)nativeDiagnostic->RuntimeVersion, nativeDiagnostic->RuntimeVersionSize),
                CopyText((byte*)nativeDiagnostic->InterfaceIdentity, nativeDiagnostic->InterfaceIdentitySize),
                CopyText((byte*)nativeDiagnostic->HelpUrl, nativeDiagnostic->HelpUrlSize)));
        }
        catch (Exception exception)
        {
            state?.ReportCallbackException(exception);
        }
        finally
        {
            --s_callbackDepth;
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
        private const int MaximumRetainedDiagnostics = 10_000;
        private int _retainedDiagnosticCount;
        private long _suppressedDiagnosticCount;
        private TaskCompletionSource _sourceCompleted = NewCompletion();

        internal Action<NativeEvent>? EventCallback { get; } = eventCallback;

        internal ImmutableArray<NativeDiagnostic> ManagedDiagnostics =>
            _managedDiagnostics.ToArray().ToImmutableArray();
        internal ulong SuppressedDiagnosticCount => checked((ulong)Math.Max(0,Interlocked.Read(ref _suppressedDiagnosticCount)));

        internal Task PrepareSourceCompletion()
        {
            var completion = NewCompletion();
            Volatile.Write(ref _sourceCompleted, completion);
            return completion.Task;
        }

        internal void SignalSourceCompleted() => Volatile.Read(ref _sourceCompleted).TrySetResult();

        private static TaskCompletionSource NewCompletion() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void ReportNativeDiagnostic(NativeDiagnostic diagnostic)
        {
            Retain(diagnostic);
            diagnosticCallback?.Invoke(diagnostic);
        }

        internal void ReportCallbackException(Exception exception)
        {
            var diagnostic = new NativeDiagnostic(
                NativeDiagnosticCode.IncompleteStream,
                exception.Message,
                Incomplete: true);
            Retain(diagnostic);
            try
            {
                diagnosticCallback?.Invoke(diagnostic);
            }
            catch
            {
                // A callback failure must never escape an unmanaged entry point.
            }
        }

        private void Retain(NativeDiagnostic diagnostic)
        {
            int count=Interlocked.Increment(ref _retainedDiagnosticCount);
            if(count<=MaximumRetainedDiagnostics)_managedDiagnostics.Enqueue(diagnostic);
            else Interlocked.Increment(ref _suppressedDiagnosticCount);
        }
    }
}
