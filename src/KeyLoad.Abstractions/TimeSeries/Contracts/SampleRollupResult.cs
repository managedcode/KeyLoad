namespace KeyLoad;

/// <summary>Exposes absent/tombstoned buckets with their current CAS revision.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupNativeAliases.SampleRollupResult)]
public sealed record SampleRollupResult([property: Orleans.Id(0)] long Revision,
    [property: Orleans.Id(1)] SampleRollupBucket? Bucket);
