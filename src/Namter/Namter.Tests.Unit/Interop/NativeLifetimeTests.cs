using Namter.Core.Interop;

namespace Namter.Tests.Unit.Interop;

public sealed class NativeLifetimeTests
{
    [Fact]
    public void SafeHandle_releases_exactly_once()
    {
        var releaseCount = 0;
        using var handle = new NativeCoreHandle((nint)123, _ => releaseCount++);

        handle.Dispose();
        handle.Dispose();

        Assert.Equal(1, releaseCount);
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
        Assert.Contains("managed callback failed", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(diagnostic, core.GetDiagnostics().ManagedDiagnostics);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task Cancellation_stops_the_native_core()
    {
        await using var core = new NativeCore();
        using var cancellation = new CancellationTokenSource();
        var run = core.CaptureAsync(
            NativeSourceKind.Npcap,
            ReadOnlyMemory<byte>.Empty,
            cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);

        Assert.True(core.GetDiagnostics().StopCount >= 1);
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

    private sealed class EventSink(TaskCompletionSource<NativeEvent> received)
    {
        public void OnEvent(NativeEvent nativeEvent) => received.TrySetResult(nativeEvent);
    }
}
