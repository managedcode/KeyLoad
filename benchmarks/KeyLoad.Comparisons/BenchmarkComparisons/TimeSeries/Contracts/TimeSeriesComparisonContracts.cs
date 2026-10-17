using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed record TimeSeriesComparisonWorkload(string RunId, PartitionRef Partition, string SetName,
    string SeriesId, TimeSpan BucketWidth, ImmutableArray<TimeSeriesSamplePoint> Samples,
    ImmutableArray<TimeSeriesBucketValue> ExpectedBuckets, ImmutableArray<TimeSeriesReadRange> ReadRanges,
    string ContentHash);
