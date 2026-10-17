namespace KeyLoad;

/// <summary>Owns exact finite statistics and the raw sequence/retention watermark.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupNativeAliases.SampleRollupBucket)]
public sealed record SampleRollupBucket(
    [property: Orleans.Id(0)] DateTimeOffset From, [property: Orleans.Id(1)] DateTimeOffset UntilExclusive,
    [property: Orleans.Id(2)] long SourceSequence, [property: Orleans.Id(3)] DateTimeOffset? RetentionBefore,
    [property: Orleans.Id(4)] SampleAggregate Aggregate);
