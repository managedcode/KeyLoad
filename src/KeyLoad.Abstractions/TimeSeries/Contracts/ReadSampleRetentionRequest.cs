namespace KeyLoad;

/// <summary>Requests the persisted retention progress of one series.</summary>
/// <param name="Partition">The owning atomic partition.</param>
/// <param name="Set">The configured time-series resource.</param>
/// <param name="SeriesId">The canonical series identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadSampleRetentionRequest)]
public sealed record ReadSampleRetentionRequest([property: Orleans.Id(0)] PartitionRef Partition,
    [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId);
