namespace KeyLoad.UnitTests.StorageRecovery;

/// <summary>Owner-advanced operation clock for read-cut elapsed limits; it never waits on the system clock.</summary>
internal sealed class ReadCutAdvancingClock : TimeProvider
{
    private static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => Epoch.AddTicks(Interlocked.Read(ref timestamp));
    public override long GetTimestamp() => Interlocked.Read(ref timestamp);
    internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);
}
