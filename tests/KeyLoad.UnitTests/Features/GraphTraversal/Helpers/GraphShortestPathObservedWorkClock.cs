namespace KeyLoad.UnitTests.Features.GraphTraversal;

/// <summary>Observes charged native adjacency work on the original operation thread.</summary>
internal sealed class GraphShortestPathObservedWorkClock : TimeProvider
{
    private Func<bool>? observed;
    private Action? cancel;
    internal bool Triggered { get; private set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp()
    {
        if (observed is { } predicate && predicate())
        {
            observed = null;
            Triggered = true;
            cancel!();
        }
        return 0;
    }
    internal void Arm(Func<bool> predicate, Action action)
    {
        observed = predicate;
        cancel = action;
    }
    internal void Disarm() { observed = null; cancel = null; }
}
