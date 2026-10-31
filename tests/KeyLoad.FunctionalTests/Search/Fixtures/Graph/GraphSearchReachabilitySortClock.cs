namespace KeyLoad.FunctionalTests.Search;

internal sealed class GraphSearchReachabilitySortClock(CancellationTokenSource? cancellation = null,
    long? cancelAt = null) : TimeProvider
{
    private long timestampCalls;

    internal long TimestampCalls => Volatile.Read(ref timestampCalls);
    internal bool CancellationTriggered
    {
        get; private set;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        var current = Interlocked.Increment(ref timestampCalls);
        if (current == cancelAt)
        {
            CancellationTriggered = true;
            cancellation!.Cancel();
        }
        return current;
    }
}
