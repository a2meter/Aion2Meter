using Namter.Core.Interop;
using System.Collections.Concurrent;

namespace Namter.Tests.Unit.Interop;

public sealed class NativeLifetimeTests
{
    [Fact]
    public async Task Replay_completes_only_after_explicit_native_eof_signal()
    {
        var observed = new ConcurrentQueue<NativeEventKind>();
        await using var core = new NativeCore(value => observed.Enqueue(value.Kind));
        byte[] emptyPcap =
        [
            0xd4, 0xc3, 0xb2, 0xa1, 0x02, 0x00, 0x04, 0x00,
            0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0xff, 0, 0,
            0x65, 0, 0, 0,
        ];

        await core.ReplayAsync(emptyPcap).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Contains(NativeEventKind.SourceStarted, observed);
        Assert.DoesNotContain(NativeEventKind.SourceCompleted, observed);
        Assert.Equal(1UL, core.GetDiagnostics().StopCount);
    }

    [Fact]
    public async Task Concurrent_source_start_is_rejected_without_replacing_first_completion_signal()
    {
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        await using var core = new NativeCore(value => { if (value.Kind == NativeEventKind.SourceStarted) { entered.Set(); release.Wait(); } });
        byte[] pcap = [0xd4,0xc3,0xb2,0xa1,2,0,4,0,0,0,0,0,0,0,0,0,0xff,0xff,0,0,0x65,0,0,0];
        Task first = Task.Run(() => core.ReplayAsync(pcap)); Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try { await Assert.ThrowsAsync<InvalidOperationException>(() => core.ReplayAsync(pcap)); }
        finally { release.Set(); }
        await first.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SafeHandle_releases_exactly_once()
    {
        var releaseCount = 0;
        using var handle = new NativeCoreHandle(
            new object(),
            _ => { },
            _ => releaseCount++);
        handle.Initialize((nint)123);

        handle.Dispose();
        handle.Dispose();

        Assert.Equal(1, releaseCount);
    }

    [Fact]
    public void SafeHandle_waits_for_inflight_calls_then_stops_destroys_and_unroots()
    {
        var lifecycle = new ConcurrentQueue<string>();
        var (handle, callbackTarget) = CreateOwnedHandle(lifecycle);
        var addRef = false;
        handle.DangerousAddRef(ref addRef);

        handle.Dispose();
        ForceFullGc();

        Assert.True(callbackTarget.IsAlive);
        Assert.Empty(lifecycle);

        handle.DangerousRelease();
        Assert.Equal(new[] { "stop", "destroy" }, lifecycle.ToArray());
        ForceFullGc();
        Assert.False(callbackTarget.IsAlive);
    }

    [Fact]
    public void Undisposed_safe_owner_finalizes_native_handle_and_callback_token()
    {
        var lifecycle = new ConcurrentQueue<string>();
        var (owner, callbackTarget) = CreateFinalizableOwner(lifecycle);

        ForceFullGc();

        Assert.False(owner.IsAlive);
        Assert.False(callbackTarget.IsAlive);
        Assert.Equal(new[] { "stop", "destroy" }, lifecycle.ToArray());
    }

    [Fact]
    public async Task Concurrent_dispose_waits_for_callback_to_leave_native_call()
    {
        using var callbackEntered = new Barrier(2);
        using var releaseCallback = new ManualResetEventSlim();
        using var callbackExited = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        Exception? callbackFailure = null;
        var core = new NativeCore(_ =>
        {
            try
            {
                callbackEntered.SignalAndWait();
                releaseCallback.Wait();
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
            }
            finally
            {
                callbackExited.Set();
            }
        });

        var run = Task.Run(async () =>
            await core.ReplayAsync(new byte[] { 9 }, cancellation.Token));
        callbackEntered.SignalAndWait();
        var dispose = core.DisposeAsync().AsTask();

        releaseCallback.Set();
        Assert.True(callbackExited.Wait(TimeSpan.FromSeconds(5)));
        cancellation.Cancel();
        await AwaitCompletionOrCancellation(run);
        await dispose;

        Assert.Null(callbackFailure);
    }

    [Fact]
    public async Task Callback_target_survives_forced_gc_and_payload_is_copied()
    {
        var (core, weakSink, received) = CreateCoreWithEphemeralSink();
        await using (core)
        {
            ForceFullGc();
            Assert.True(weakSink.IsAlive);

            var source = new byte[] { 1, 2, 3, 4 };
            using var cancellation = new CancellationTokenSource();
            var run = core.ReplayAsync(source, cancellation.Token);
            var nativeEvent = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

            source.AsSpan().Clear();
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, nativeEvent.Payload.AsSpan().ToArray());

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        }
    }

    [Fact]
    public async Task Managed_callback_exception_becomes_incomplete_stream_diagnostic()
    {
        var received = new TaskCompletionSource<NativeDiagnostic>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var core = new NativeCore(
            _ => throw new InvalidOperationException("managed callback failed"),
            diagnostic => received.TrySetResult(diagnostic));
        using var cancellation = new CancellationTokenSource();

        var run = core.ReplayAsync(new byte[] { 7 }, cancellation.Token);
        var diagnostic = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(NativeDiagnosticCode.IncompleteStream, diagnostic.Code);
        Assert.True(diagnostic.Incomplete);
        Assert.Contains("managed callback failed", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(diagnostic, core.GetDiagnostics().ManagedDiagnostics);

        cancellation.Cancel();
        await AwaitCompletionOrCancellation(run);
    }

    [Fact]
    public async Task Cancellation_stops_the_native_core()
    {
        await using var core = new NativeCore();
        using var cancellation = new CancellationTokenSource();
        var run = core.ReplayAsync(
            new byte[]
            {
                0xd4, 0xc3, 0xb2, 0xa1, 0x02, 0x00, 0x04, 0x00,
                0, 0, 0, 0, 0, 0, 0, 0, 0xff, 0xff, 0, 0,
                0x65, 0, 0, 0,
            },
            cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(core.GetDiagnostics().StopCount >= 1);
    }

    [Fact]
    public async Task Worker_diagnostic_can_dispose_core_reentrantly_without_deadlock()
    {
        NativeCore? core = null;
        var disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        core = new NativeCore(diagnosticCallback: _diagnostic =>
        {
            core!.DisposeAsync();
            disposed.TrySetResult();
        });
        using var cancellation = new CancellationTokenSource();
        var run = core.ReplayAsync(new byte[] { 7 }, cancellation.Token);
        await disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await AwaitCompletionOrCancellation(run);
        await core!.DisposeAsync();
    }

    private static (NativeCore Core, WeakReference WeakSink, TaskCompletionSource<NativeEvent> Received)
        CreateCoreWithEphemeralSink()
    {
        var received = new TaskCompletionSource<NativeEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new EventSink(received);
        var weakSink = new WeakReference(sink);
        var core = new NativeCore(sink.OnEvent);
        return (core, weakSink, received);
    }

    private static void ForceFullGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static async Task AwaitCompletionOrCancellation(Task run)
    {
        try { await run; }
        catch (OperationCanceledException) { }
    }

    private static (NativeCoreHandle Handle, WeakReference CallbackTarget) CreateOwnedHandle(
        ConcurrentQueue<string> lifecycle)
    {
        var callbackTarget = new object();
        var weakTarget = new WeakReference(callbackTarget);
        var handle = new NativeCoreHandle(
            callbackTarget,
            _ => lifecycle.Enqueue("stop"),
            _ => lifecycle.Enqueue("destroy"));
        handle.Initialize((nint)123);
        return (handle, weakTarget);
    }

    private static (WeakReference Owner, WeakReference CallbackTarget) CreateFinalizableOwner(
        ConcurrentQueue<string> lifecycle)
    {
        var (handle, callbackTarget) = CreateOwnedHandle(lifecycle);
        return (new WeakReference(handle), callbackTarget);
    }

    private sealed class EventSink(TaskCompletionSource<NativeEvent> received)
    {
        public void OnEvent(NativeEvent nativeEvent) => received.TrySetResult(nativeEvent);
    }
}
