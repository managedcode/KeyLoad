using System.Collections.Immutable;
using ManagedCode.TimeSeries.Abstractions;
using ManagedCode.TimeSeries.Summers;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal static class ManagedCodeTimeSeriesAggregation
{
    internal static ImmutableArray<TimeSeriesBucketValue> Aggregate(TimeSeriesComparisonWorkload workload)
    {
        ArgumentNullException.ThrowIfNull(workload);
        var summer = new DoubleTimeSeriesSummer(workload.BucketWidth, maxSamplesCount: 0, Strategy.Sum);
        foreach (var sample in workload.Samples)
        {
            summer.AddNewData(sample.Timestamp, sample.Value);
        }

        return summer.Buckets.Select(bucket => new TimeSeriesBucketValue(
            bucket.Key.ToUniversalTime(), bucket.Value)).ToImmutableArray();
    }
}
