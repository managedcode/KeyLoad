namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Native ITimer ownership for the explicit controlled test clock.</summary>
internal sealed class ControlledReadTimer(ControlledReadClock clock, TimerCallback callback, object? state) : ITimer
{
    private long dueAt = long.MaxValue;
    private TimeSpan interval;
    private bool disposed;

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, Timeout.InfiniteTimeSpan);
        ArgumentOutOfRangeException.ThrowIfLessThan(period, Timeout.InfiniteTimeSpan);
        lock (clock.Gate)
        {
            if (disposed)
            { return false; }
            interval = period;
            dueAt = dueTime == Timeout.InfiniteTimeSpan
                ? long.MaxValue : checked(clock.CurrentTimestamp + dueTime.Ticks);
            return true;
        }
    }
    internal void FireIfDue()
    {
        lock (clock.Gate)
        {
            if (disposed || dueAt == long.MaxValue || clock.CurrentTimestamp < dueAt)
            { return; }
            dueAt = interval > TimeSpan.Zero ? checked(clock.CurrentTimestamp + interval.Ticks) : long.MaxValue;
        }
        callback(state);
    }
    public void Dispose()
    {
        lock (clock.Gate)
        {
            if (disposed)
            { return; }
            disposed = true;
            clock.Remove(this);
        }
    }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
