namespace KeyLoad.Server;

internal sealed class AdminHttpMetrics
{
    private readonly object gate = new();
    private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;
    private readonly Guid processInstance = Guid.NewGuid();
    private long completed;
    private long failed;
    private double milliseconds;

    internal void Record(TimeSpan elapsed, bool failure)
    {
        lock (gate)
        {
            completed++;
            if (failure)
            { failed++; }
            milliseconds += elapsed.TotalMilliseconds;
        }
    }

    internal AdminHttpSnapshot Snapshot()
    {
        lock (gate)
        { return new(startedAt, processInstance, completed, failed, milliseconds); }
    }
}
