namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Observes actual bytes in one owning store after this leaf creates its original read budget.</summary>
internal sealed class RemotePartitionNativeReadClock(Func<long> bytes, RemotePartitionNativeReadBarrier barrier) : TimeProvider
{
    private const long Unobserved = -1;
    private long before = Unobserved;
    private bool armed = true;
    internal bool Triggered { get; private set; }
    public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
    public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
    public override long GetTimestamp()
    {
        if (before == Unobserved)
        { before = bytes(); }
        else if (armed && bytes() > before)
        {
            armed = false;
            Triggered = true;
            barrier.Arrive();
        }
        return TimeProvider.System.GetTimestamp();
    }
    internal void Disarm() => armed = false;
}
