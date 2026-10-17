using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal interface ITimeSeriesAggregationTarget
{
    Task<ImmutableArray<TimeSeriesBucketValue>> AggregateAsync(TimeSeriesComparisonWorkload workload,
        CancellationToken cancellationToken);
}
