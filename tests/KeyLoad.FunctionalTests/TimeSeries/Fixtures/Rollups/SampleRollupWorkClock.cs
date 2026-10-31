namespace KeyLoad.FunctionalTests.TimeSeries;

/// <summary>Owner-authorized operation clock; observes real execution work without substituting storage.</summary>
internal sealed class SampleRollupWorkClock : TimeProvider
{
    private readonly DateTimeOffset epoch = TimeProvider.System.GetUtcNow();
    private Func<bool>? observedWork;
    private Action? afterWork;
    internal bool Triggered
    {
        get; private set;
    }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => epoch;
    public override long GetTimestamp()
    {
        if (observedWork is { } predicate && predicate())
        {
            observedWork = null;
            Triggered = true;
            afterWork!();
        }
        return 0;
    }
    internal void Arm(Func<bool> predicate, Action action)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(action);
        Triggered = false;
        afterWork = action;
        observedWork = predicate;
    }
    internal void Disarm()
    {
        observedWork = null;
        afterWork = null;
    }
}
