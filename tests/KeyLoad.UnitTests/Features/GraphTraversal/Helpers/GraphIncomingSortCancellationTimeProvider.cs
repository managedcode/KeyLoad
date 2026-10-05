namespace KeyLoad.UnitTests.Features.GraphTraversal;

internal sealed class GraphIncomingSortCancellationTimeProvider(CancellationTokenSource cancellation)
    : TimeProvider
{
    private int cancellationCountdown = -1;
    private int deadlineCountdown = -1;
    private long timestamp;

    internal bool CancellationTriggered { get; private set; }
    internal bool DeadlineTriggered { get; private set; }

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
        var deadlineRemaining = Volatile.Read(ref deadlineCountdown);
        if (deadlineRemaining > 0 && Interlocked.Decrement(ref deadlineCountdown) == 0)
        {
            DeadlineTriggered = true;
            return checked(current + TimestampFrequency * 2);
        }
        return current;
    }

    internal void CancelAfterTimestampCalls(int calls)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(calls);
        Volatile.Write(ref cancellationCountdown, calls);
    }

    internal void ExceedDeadlineAfterTimestampCalls(int calls)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(calls);
        Volatile.Write(ref deadlineCountdown, calls);
    }
}
