namespace KeyLoad;

/// <summary>Reads one partition assignment and the committed default-owner tuple from one read view.</summary>
/// <param name="Version">The public request version.</param>
/// <param name="Partition">The complete four-field atomic partition identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionPlacementAliases.ReadRequest)]
public sealed record AtomicPartitionPlacementReadRequest(
    [property: Orleans.Id(AtomicPartitionPlacementReadRequestFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionPlacementReadRequestFieldIds.Partition)] PartitionRef Partition);
