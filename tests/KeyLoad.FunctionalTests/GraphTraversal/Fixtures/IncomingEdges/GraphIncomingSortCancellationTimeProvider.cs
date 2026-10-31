namespace KeyLoad.FunctionalTests.GraphTraversal;

internal sealed class GraphIncomingSortCancellationTimeProvider(CancellationTokenSource cancellation)
    : TimeProvider
{
    private int cancellationCountdown = -1;
    private long timestamp;

    internal bool CancellationTriggered
    {
        get; private set;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        var current = Interlocked.Increment(ref timestamp);
        var cancellationRemaining = Volatile.Read(ref cancellationCountdown);
        if (cancellationRemaining > 0 && Interlocked.Decrement(ref cancellationCountdown) == 0)
        {
            CancellationTriggered = true;
            cancellation.Cancel();
        }
        return current;
    }

    internal void CancelAfterTimestampCalls(int calls)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(calls);
        Volatile.Write(ref cancellationCountdown, calls);
    }
}
