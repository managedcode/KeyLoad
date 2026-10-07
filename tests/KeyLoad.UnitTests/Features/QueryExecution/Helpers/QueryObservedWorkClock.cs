namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Operation clock observing actual work in the owning native store.</summary>
internal sealed class QueryObservedWorkClock : TimeProvider
{
    private readonly DateTimeOffset epoch = TimeProvider.System.GetUtcNow();
    private Func<bool>? observedWork;
    private Action? afterWork;
    private long timestamp;
    internal bool Triggered { get; private set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => epoch.AddTicks(timestamp);
    public override long GetTimestamp()
    {
        if (observedWork is { } predicate && predicate())
        {
            observedWork = null;
            Triggered = true;
            afterWork!();
        }
        return timestamp;
    }
    internal void Arm(Func<bool> predicate, Action action)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(action);
        observedWork = predicate;
        afterWork = action;
    }
    internal void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    internal void Disarm() { observedWork = null; afterWork = null; }
}
