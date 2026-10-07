namespace KeyLoad;

/// <summary>Rebuilds one complete explicit half-open UTC bucket from canonical raw samples.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupNativeAliases.RefreshSampleRollup)]
public sealed record RefreshSampleRollup(
    [property: Orleans.Id(0)] string SeriesSet, [property: Orleans.Id(1)] string SeriesId,
    [property: Orleans.Id(2)] DateTimeOffset From, [property: Orleans.Id(3)] DateTimeOffset UntilExclusive,
    [property: Orleans.Id(4)] long ExpectedRevision,
    [property: Orleans.Id(5)] int MaxSamples = TimeSeriesReadDefaults.MaxSamples) : Mutation(SeriesSet);

/// <summary>Drops derived statistics while retaining the monotone revision tombstone.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupNativeAliases.DropSampleRollup)]
public sealed record DropSampleRollup(
    [property: Orleans.Id(0)] string SeriesSet, [property: Orleans.Id(1)] string SeriesId,
    [property: Orleans.Id(2)] DateTimeOffset From, [property: Orleans.Id(3)] DateTimeOffset UntilExclusive,
    [property: Orleans.Id(4)] long ExpectedRevision) : Mutation(SeriesSet);

/// <summary>Selects one current derived bucket under fresh persisted series read rights.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupNativeAliases.ReadSampleRollupRequest)]
public sealed record ReadSampleRollupRequest(
    [property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Set,
    [property: Orleans.Id(2)] string SeriesId, [property: Orleans.Id(3)] DateTimeOffset From,
    [property: Orleans.Id(4)] DateTimeOffset UntilExclusive);

/// <summary>Owns exact finite statistics and the raw sequence/retention watermark.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupNativeAliases.SampleRollupBucket)]
public sealed record SampleRollupBucket(
    [property: Orleans.Id(0)] DateTimeOffset From, [property: Orleans.Id(1)] DateTimeOffset UntilExclusive,
    [property: Orleans.Id(2)] long SourceSequence, [property: Orleans.Id(3)] DateTimeOffset? RetentionBefore,
    [property: Orleans.Id(4)] SampleAggregate Aggregate);

/// <summary>Exposes absent/tombstoned buckets with their current CAS revision.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupNativeAliases.SampleRollupResult)]
public sealed record SampleRollupResult([property: Orleans.Id(0)] long Revision,
    [property: Orleans.Id(1)] SampleRollupBucket? Bucket);
