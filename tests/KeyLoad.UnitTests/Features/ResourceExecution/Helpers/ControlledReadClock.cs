namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Explicit test-owned UTC, monotonic time and timers; storage and transport remain real.</summary>
internal sealed class ControlledReadClock(DateTimeOffset utcNow) : TimeProvider
{
    private readonly List<ControlledReadTimer> timers = [];
    internal TimeSpan TimestampStep { get; set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() { lock (Gate) { return utcNow; } }
    public override long GetTimestamp()
    {
        lock (Gate)
        {
            var result = CurrentTimestamp;
            CurrentTimestamp = checked(CurrentTimestamp + TimestampStep.Ticks);
            return result;
        }
    }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ControlledReadTimer(this, callback, state);
        lock (Gate)
        {
            timer.Change(dueTime, period);
            timers.Add(timer);
        }
        return timer;
    }
    internal int ActiveTimers { get { lock (Gate) { return timers.Count; } } }
    internal Lock Gate { get; } = new();
    internal long CurrentTimestamp { get; private set; }
    internal void Remove(ControlledReadTimer timer) => timers.Remove(timer);
    internal void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        ControlledReadTimer[] pending;
        lock (Gate)
        {
            CurrentTimestamp = checked(CurrentTimestamp + elapsed.Ticks);
            utcNow += elapsed;
            pending = timers.ToArray();
        }
        foreach (var timer in pending)
        { timer.FireIfDue(); }
    }
    internal void MoveUtc(DateTimeOffset instant) { lock (Gate) { utcNow = instant; } }
}
