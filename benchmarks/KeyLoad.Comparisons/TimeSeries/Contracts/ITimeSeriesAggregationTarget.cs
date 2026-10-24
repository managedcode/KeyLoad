using System.Collections.Immutable;

namespace KeyLoad.Comparisons.BenchmarkComparisons.TimeSeries;

internal interface ITimeSeriesAggregationTarget
{
    Task<ImmutableArray<TimeSeriesBucketValue>> AggregateAsync(TimeSeriesComparisonWorkload workload,
        CancellationToken cancellationToken);
}
