using System.Diagnostics;

namespace KeyLoad.Comparisons;

internal readonly record struct OpenLoopTimeline(long OriginTimestamp, int RatePerSecond,
    OpenLoopExecutionPolicy ExecutionPolicy)
{
    private const int MinimumSpinThresholdTicks = 1;

    internal long DueTimestamp(int index)
        => checked(OriginTimestamp + OpenLoopRateContract.ToStopwatchTicks(
            OpenLoopRateContract.DueOffsetNanoseconds(index, RatePerSecond)));

    internal long DueOffsetNanoseconds(int index)
        => OpenLoopRateContract.DueOffsetNanoseconds(index, RatePerSecond);

    internal long OperationDeadline(int index)
        => checked(DueTimestamp(index) + ExecutionPolicy.OperationDeadlineTicks);

    internal long DrainDeadline()
        => checked(DueTimestamp(OpenLoopRateContract.PlannedOperations - 1)
            + ExecutionPolicy.DrainTicks);

    internal double OffsetMilliseconds(long timestamp)
        => Stopwatch.GetElapsedTime(OriginTimestamp, timestamp).TotalMilliseconds;

    internal double ElapsedSeconds(long timestamp)
        => Stopwatch.GetElapsedTime(OriginTimestamp, timestamp).TotalSeconds;

    internal async Task WaitUntilAsync(long timestamp, CancellationToken cancellationToken)
    {
        var spinThreshold = Math.Max(MinimumSpinThresholdTicks, ExecutionPolicy.SpinWindowTicks);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = timestamp - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return;
            }
            if (remaining > spinThreshold)
            {
                var sleepTicks = remaining - spinThreshold;
                await Task.Delay(TimeSpan.FromSeconds((double)sleepTicks / Stopwatch.Frequency), cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                SpinUntil(timestamp, cancellationToken);
                return;
            }
        }
    }

    private static void SpinUntil(long timestamp, CancellationToken cancellationToken)
    {
        var spinner = new SpinWait();
        while (Stopwatch.GetTimestamp() < timestamp)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spinner.SpinOnce();
        }
    }
}
