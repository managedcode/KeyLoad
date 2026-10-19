namespace KeyLoad;

/// <summary>Captures the local storage identity used by an atomic partition operation.</summary>
/// <param name="Version">The contract version.</param>
/// <param name="Partition">The complete atomic partition identity.</param>
/// <param name="PhysicalShardId">The actual local storage node identity.</param>
/// <param name="Incarnation">The local database incarnation.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionPlacementAliases.Resolution)]
public sealed record AtomicPartitionPlacementResolution(
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.Partition)] PartitionRef Partition,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.PhysicalShardId)] Guid PhysicalShardId,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.Incarnation)] Guid Incarnation);
