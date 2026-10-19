namespace KeyLoad;

/// <summary>Selects the latest authorized sample at an optional inclusive UTC cut.</summary>
/// <param name="Partition">Atomic partition owning the series.</param>
/// <param name="Set">Configured time-series resource.</param>
/// <param name="SeriesId">Series identity.</param>
/// <param name="AtOrBefore">Inclusive timestamp cut, or null for the latest committed sample.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.ReadLatestSampleRequest)]
public sealed record ReadLatestSampleRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId,
    [property: Orleans.Id(3)] DateTimeOffset? AtOrBefore = null);
