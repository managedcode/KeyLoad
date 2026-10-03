using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimeSeriesIntensiveResponse(ImmutableArray<SampleRecord> Raw = default,
    SampleRecord? Latest = null, SampleAggregate? Aggregate = null,
    ImmutableArray<SampleAggregateWindow> Windows = default, TimeSeriesIntensiveAppendReceipt? Receipt = null)
{
    internal long? Count(TimeSeriesIntensiveScenario scenario) => scenario switch
    {
        TimeSeriesIntensiveScenario.Append => Receipt is null ? null : 1,
        TimeSeriesIntensiveScenario.RawRangeRead => Raw.IsDefault ? null : Raw.Length,
        TimeSeriesIntensiveScenario.Latest => Latest is null ? 0 : 1,
        TimeSeriesIntensiveScenario.Aggregate => Aggregate?.Count,
        TimeSeriesIntensiveScenario.Windows => Windows.IsDefault ? null : Windows.Length,
        _ => null
    };
}
