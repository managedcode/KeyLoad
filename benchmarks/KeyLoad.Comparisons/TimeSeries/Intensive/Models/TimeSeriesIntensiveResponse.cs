using System.Collections.Immutable;

namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries.Intensive;

internal readonly record struct TimeSeriesIntensiveResponse(ImmutableArray<SampleRecord> Raw = default,
    SampleRecord? Latest = null, SampleAggregate? Aggregate = null,
    ImmutableArray<SampleAggregateWindow> Windows = default, TimeSeriesIntensiveAppendReceipt? Receipt = null)
{
    private const int SingleItemCount = 1;
    private const int NoObservedItems = 0;

    internal (bool HasValue, long? Value) Count(TimeSeriesIntensiveScenario scenario) => scenario switch
    {
        TimeSeriesIntensiveScenario.Append => Receipt is null ? (false, default(long?)) : (true, ((long?)(SingleItemCount))),
        TimeSeriesIntensiveScenario.RawRangeRead => Raw.IsDefault ? (false, default(long?)) : (true, ((long?)(Raw.Length))),
        TimeSeriesIntensiveScenario.Latest => Latest is null ? (true, ((long?)(NoObservedItems))) : (true, ((long?)(SingleItemCount))),
        TimeSeriesIntensiveScenario.Aggregate => ((long?)(Aggregate?.Count)) is { } outcomeValue7 ? (true, outcomeValue7) : (false, default(long?)),
        TimeSeriesIntensiveScenario.Windows => Windows.IsDefault ? (false, default(long?)) : (true, ((long?)(Windows.Length))),
        _ => (false, default(long?))
    };
}
