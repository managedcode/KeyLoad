namespace KeyLoad.UnitTests.Features.GraphTraversal;

/// <summary>Advances the permitted operation clock after real native adjacency work has started.</summary>
internal sealed class GraphTraversalDeadlineClock : TimeProvider
{
    private static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private Func<bool>? advanceAfterWork;
    private long timestamp;
    private long advanceTicks;

    internal bool ElapsedCapTriggered { get; private set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => Epoch.AddTicks(Interlocked.Read(ref timestamp));

    public override long GetTimestamp()
    {
        if (advanceAfterWork is { } observedWork && observedWork())
        {
            advanceAfterWork = null;
            Interlocked.Add(ref timestamp, advanceTicks);
            ElapsedCapTriggered = true;
        }
        return Interlocked.Read(ref timestamp);
    }

    internal void ArmAfterNativeWork(Func<bool> observedWork, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(observedWork);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(elapsed, TimeSpan.Zero);
        advanceTicks = elapsed.Ticks;
        ElapsedCapTriggered = false;
        advanceAfterWork = observedWork;
    }

    internal void StopAdvancing() => advanceAfterWork = null;
}
