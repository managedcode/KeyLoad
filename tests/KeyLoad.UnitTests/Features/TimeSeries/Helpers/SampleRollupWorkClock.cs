namespace KeyLoad.UnitTests.Features.TimeSeries;

/// <summary>Owner-authorized operation clock; observes real native work without substituting storage.</summary>
internal sealed class SampleRollupWorkClock : TimeProvider
{
    private readonly DateTimeOffset epoch = TimeProvider.System.GetUtcNow();
    private Func<bool>? observedWork;
    private Action? afterWork;
    private long timestamp;
    internal bool Triggered { get; private set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => epoch.AddTicks(Interlocked.Read(ref timestamp));
    public override long GetTimestamp()
    {
        if (observedWork is { } predicate && predicate())
        {
            observedWork = null;
            Triggered = true;
            afterWork!();
        }
        return Interlocked.Read(ref timestamp);
    }
    internal void Arm(Func<bool> predicate, Action action)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(action);
        Triggered = false;
        afterWork = action;
        observedWork = predicate;
    }
    internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);
    internal void Disarm() { observedWork = null; afterWork = null; }
}
