namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkRetentionClock(DateTimeOffset initial) : TimeProvider
{
    private DateTimeOffset current = initial;

    public override DateTimeOffset GetUtcNow() => current;

    internal void AdvanceTo(DateTimeOffset next)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(next, current);
        current = next;
    }
}
