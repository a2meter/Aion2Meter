using System.Diagnostics;

namespace Namter.Cli;

public static class ReplaySchedulingPolicy
{
    public static TimeSpan DueTime(ulong firstTimestampNs, ulong currentTimestampNs, int speed)
    {
        if (speed == 0) return TimeSpan.Zero;
        if (speed is not (1 or 10)) throw new ArgumentOutOfRangeException(nameof(speed));
        if (currentTimestampNs < firstTimestampNs) throw new InvalidDataException("Replay capture timestamps are out of order.");
        ulong scaled = (currentTimestampNs - firstTimestampNs) / checked((ulong)speed);
        return TimeSpan.FromTicks(checked((long)(scaled / 100)));
    }
}

internal sealed class ReplayPacer(int speed)
{
    private ulong? firstTimestampNs;
    private long startedAt;
    internal bool Wait(ulong timestampNs, CancellationToken cancellationToken)
    {
        if (speed == 0) return !cancellationToken.IsCancellationRequested;
        if (firstTimestampNs is null) { firstTimestampNs=timestampNs; startedAt=Stopwatch.GetTimestamp(); return !cancellationToken.IsCancellationRequested; }
        TimeSpan due=ReplaySchedulingPolicy.DueTime(firstTimestampNs.Value,timestampNs,speed);TimeSpan elapsed=Stopwatch.GetElapsedTime(startedAt);TimeSpan remaining=due-elapsed;
        return remaining<=TimeSpan.Zero ? !cancellationToken.IsCancellationRequested : !cancellationToken.WaitHandle.WaitOne(remaining);
    }
}
