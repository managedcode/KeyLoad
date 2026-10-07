namespace KeyLoad.UnitTests.Features.Search;

internal sealed class HybridExplainObservedWorkClock : TimeProvider
{
    private readonly DateTimeOffset now = TimeProvider.System.GetUtcNow();
    private Func<bool>? observed;
    private Action? cancel;
    internal bool Triggered { get; private set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => now;
    public override long GetTimestamp()
    {
        if (observed is { } predicate && predicate())
        {
            observed = null;
            Triggered = true;
            cancel!();
        }
        return default;
    }
    internal void Arm(Func<bool> predicate, Action action)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(action);
        observed = predicate;
        cancel = action;
    }
    internal void Disarm() { observed = null; cancel = null; }
}
