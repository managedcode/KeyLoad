namespace KeyLoad.UnitTests.Features.Search;

internal sealed class GraphSearchReachabilitySortClock(CancellationTokenSource? cancellation = null,
    long? cancelAt = null, long? deadlineAt = null) : TimeProvider
{
    private const int DeadlineAdvanceSeconds = 31;
    private long timestampCalls;

    internal long TimestampCalls => Volatile.Read(ref timestampCalls);
    internal bool CancellationTriggered { get; private set; }
    internal bool DeadlineTriggered { get; private set; }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        var current = Interlocked.Increment(ref timestampCalls);
        if (current == cancelAt)
        {
            CancellationTriggered = true;
            cancellation!.Cancel();
        }
        if (current == deadlineAt)
        {
            DeadlineTriggered = true;
            return checked(current + TimestampFrequency * DeadlineAdvanceSeconds);
        }
        return current;
    }
}
