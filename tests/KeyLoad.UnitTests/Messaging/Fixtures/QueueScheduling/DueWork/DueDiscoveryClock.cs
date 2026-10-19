namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class DueDiscoveryClock : TimeProvider
{
    private const long BoundaryCrossingTicks = 1;
    private Func<bool>? observedProgress;
    private long elapsedTicks;
    private long deadlineTicks;

    internal bool ExpiredAfterProgress
    {
        get; private set;
    }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => RecurringSagaDatabase.Epoch.AddTicks(elapsedTicks);
    public override long GetTimestamp()
    {
        if (observedProgress is { } progress && progress())
        {
            observedProgress = null;
            ExpiredAfterProgress = true;
            elapsedTicks = checked(elapsedTicks + deadlineTicks + BoundaryCrossingTicks);
        }
        return elapsedTicks;
    }

    internal void Arm(Func<bool> progress, TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(progress);
        observedProgress = progress;
        deadlineTicks = deadline.Ticks;
    }

    internal void Disarm() => observedProgress = null;
}
