namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Observes provider use while forwarding every clock and timer operation to the real system provider.</summary>
internal sealed class NativeDatabaseFlowTimeProvider : TimeProvider
{
    private readonly TimeProvider inner;
    private int utcNowReads;
    private int timerCreations;

    internal NativeDatabaseFlowTimeProvider(TimeProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
    }

    internal int UtcNowReads => Volatile.Read(ref utcNowReads);
    internal int TimerCreations => Volatile.Read(ref timerCreations);

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;
    public override long TimestampFrequency => inner.TimestampFrequency;

    public override DateTimeOffset GetUtcNow()
    {
        Interlocked.Increment(ref utcNowReads);
        return inner.GetUtcNow();
    }

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref timerCreations);
        return inner.CreateTimer(callback, state, dueTime, period);
    }
}
