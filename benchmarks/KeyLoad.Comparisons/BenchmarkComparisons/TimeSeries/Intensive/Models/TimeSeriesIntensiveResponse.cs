using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimeSeriesIntensiveResponse(ImmutableArray<SampleRecord> Raw = default,
    SampleRecord? Latest = null, SampleAggregate? Aggregate = null,
    ImmutableArray<SampleAggregateWindow> Windows = default, TimeSeriesIntensiveAppendReceipt? Receipt = null)
{
    private const int SingleItemCount = 1;
    private const int NoObservedItems = 0;

    internal long? Count(TimeSeriesIntensiveScenario scenario) => scenario switch
    {
        TimeSeriesIntensiveScenario.Append => Receipt is null ? null : SingleItemCount,
        TimeSeriesIntensiveScenario.RawRangeRead => Raw.IsDefault ? null : Raw.Length,
        TimeSeriesIntensiveScenario.Latest => Latest is null ? NoObservedItems : SingleItemCount,
        TimeSeriesIntensiveScenario.Aggregate => Aggregate?.Count,
        TimeSeriesIntensiveScenario.Windows => Windows.IsDefault ? null : Windows.Length,
        _ => null
    };
}
