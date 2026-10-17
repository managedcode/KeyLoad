namespace KeyLoad.RecoveryTests.Features.Messaging;

internal sealed class RecordedQueueFollowerClockFault : TimeProvider
{
    internal TimeSpan Offset
    {
        get; set;
    }
    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + Offset;
    public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
    public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => TimeProvider.System.CreateTimer(callback, state, dueTime, period);
}
