namespace KeyLoad.UnitTests.Features.EventStreams;

/// <summary>Controls UTC token expiry while borrowing the original monotonic deadline/timer owner.</summary>
internal sealed class StreamTraversalObservedClock(TimeProvider source) : TimeProvider
{
    private DateTimeOffset? observedUtc;
    public override DateTimeOffset GetUtcNow() => observedUtc ?? source.GetUtcNow();
    public override long GetTimestamp() => source.GetTimestamp();
    public override long TimestampFrequency => source.TimestampFrequency;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => source.CreateTimer(callback, state, dueTime, period);
    internal void AdvanceBy(TimeSpan originalLifetime) => observedUtc = GetUtcNow() + originalLifetime;
}
