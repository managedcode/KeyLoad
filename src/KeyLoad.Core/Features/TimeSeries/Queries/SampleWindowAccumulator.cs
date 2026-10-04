namespace KeyLoad.Core.Features.TimeSeries;

internal sealed class SampleWindowAccumulator
{
    private SampleAggregateAccumulator? aggregate;

    internal SampleWindowAccumulator(long fromTicks, long untilExclusiveTicks)
    {
        From = new(fromTicks, TimeSpan.Zero);
        UntilExclusive = untilExclusiveTicks > DateTimeOffset.MaxValue.UtcTicks
            ? null
            : new DateTimeOffset(untilExclusiveTicks, TimeSpan.Zero);
    }

    internal DateTimeOffset From { get; }
    internal DateTimeOffset? UntilExclusive { get; }

    internal void Add(SampleRecord sample)
    {
        aggregate ??= new();
        aggregate.Add(sample);
    }

    internal SampleAggregateWindow Complete() => new(From, UntilExclusive,
        aggregate?.Complete() ?? SampleAggregateAccumulator.Empty);
}
