using System.Collections.Immutable;

namespace KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries;

internal sealed record TimeSeriesSamplePoint(string EventId, DateTimeOffset Timestamp, double Value, long Sequence,
    string TagsJson);

internal sealed record TimeSeriesBucketValue(DateTimeOffset TimestampUtc, double Sum);

internal sealed record TimeSeriesReadRange(string Name, DateTimeOffset From, DateTimeOffset Until,
    int ExpectedCount, string? ExpectedErrorCode = null);

internal sealed record TimeSeriesComparisonWorkload(string RunId, PartitionRef Partition, string SetName,
    string SeriesId, TimeSpan BucketWidth, ImmutableArray<TimeSeriesSamplePoint> Samples,
    ImmutableArray<TimeSeriesBucketValue> ExpectedBuckets, ImmutableArray<TimeSeriesReadRange> ReadRanges,
    string ContentHash);

internal sealed record TimeSeriesReadResult(bool Succeeded, ImmutableArray<TimeSeriesSamplePoint> Samples,
    string? ErrorCode);

internal sealed record TimeSeriesTargetMetadata(string Name, string StorageModel, string PersistenceGuarantee,
    string AcknowledgementGuarantee, string? Image, string? PackageVersion);

internal interface ITimeSeriesPersistentTarget : IAsyncDisposable
{
    TimeSeriesTargetMetadata Metadata
    {
        get;
    }

    Task InitializeAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken);

    Task SeedAsync(TimeSeriesComparisonWorkload workload, CancellationToken cancellationToken);

    Task<TimeSeriesReadResult> ReadAsync(TimeSeriesComparisonWorkload workload, TimeSeriesReadRange range,
        CancellationToken cancellationToken);
}

internal interface ITimeSeriesAggregationTarget
{
    Task<ImmutableArray<TimeSeriesBucketValue>> AggregateAsync(TimeSeriesComparisonWorkload workload,
        CancellationToken cancellationToken);
}
