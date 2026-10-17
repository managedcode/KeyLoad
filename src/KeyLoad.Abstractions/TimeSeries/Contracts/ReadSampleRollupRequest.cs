namespace KeyLoad;

/// <summary>Selects one current derived bucket under fresh persisted series read rights.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(SampleRollupNativeAliases.ReadSampleRollupRequest)]
public sealed record ReadSampleRollupRequest(
    [property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Set,
    [property: Orleans.Id(2)] string SeriesId, [property: Orleans.Id(3)] DateTimeOffset From,
    [property: Orleans.Id(4)] DateTimeOffset UntilExclusive);
