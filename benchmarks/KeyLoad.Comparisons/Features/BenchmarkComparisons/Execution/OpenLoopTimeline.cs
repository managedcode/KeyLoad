
namespace KeyLoad.Comparisons;

internal readonly record struct OpenLoopTimeline(long OriginTimestamp, int RatePerSecond,
    OpenLoopExecutionPolicy ExecutionPolicy, TimeProvider TimeProvider)
{
    private const int MinimumSpinThresholdTicks = 1;

    internal long DueTimestamp(int index)
        => checked(OriginTimestamp + OpenLoopRateContract.ToTimestampTicks(
            OpenLoopRateContract.DueOffsetNanoseconds(index, RatePerSecond), timeProvider: TimeProvider));

    internal long DueOffsetNanoseconds(int index)
        => OpenLoopRateContract.DueOffsetNanoseconds(index, RatePerSecond);

    internal long OperationDeadline(int index)
        => checked(DueTimestamp(index) + ExecutionPolicy.OperationDeadlineTicks(TimeProvider));

    internal long DrainDeadline()
        => checked(DueTimestamp(OpenLoopRateContract.PlannedOperations - MinimumSpinThresholdTicks)
            + ExecutionPolicy.DrainTicks(TimeProvider));

    internal double OffsetMilliseconds(long timestamp)
        => TimeProvider.GetElapsedTime(OriginTimestamp, timestamp).TotalMilliseconds;

    internal double ElapsedSeconds(long timestamp)
        => TimeProvider.GetElapsedTime(OriginTimestamp, timestamp).TotalSeconds;

    internal async Task WaitUntilAsync(long timestamp, CancellationToken cancellationToken)
    {
        const int NoObservedItems = 0;

        var spinThreshold = Math.Max(MinimumSpinThresholdTicks, ExecutionPolicy.SpinWindowTicks(TimeProvider));
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = timestamp - TimeProvider.GetTimestamp();
            if (remaining <= NoObservedItems)
            {
                return;
            }
            if (remaining > spinThreshold)
            {
                var sleepTicks = remaining - spinThreshold;
                await Task.Delay(TimeSpan.FromSeconds((double)sleepTicks / TimeProvider.TimestampFrequency), TimeProvider, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                SpinUntil(timestamp, TimeProvider, cancellationToken);
                return;
            }
        }
    }

    private static void SpinUntil(long timestamp, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var spinner = new SpinWait();
        while (timeProvider.GetTimestamp() < timestamp)
        {
            cancellationToken.ThrowIfCancellationRequested();
            spinner.SpinOnce();
        }
    }
}
