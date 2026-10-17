namespace KeyLoad;

/// <summary>Requests an administrator-authorized placement row under directory revision CAS.</summary>
/// <param name="Version">The command format version.</param>
/// <param name="ExpectedRevision">The required current directory revision.</param>
/// <param name="Partition">The complete four-field atomic identity.</param>
/// <param name="PhysicalShardId">The requested physical shard identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionPlacementAliases.BindRequest)]
public sealed record BindAtomicPartitionPlacementRequest(
    [property: Orleans.Id(AtomicPartitionPlacementRequestFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionPlacementRequestFieldIds.ExpectedRevision)] long ExpectedRevision,
    [property: Orleans.Id(AtomicPartitionPlacementRequestFieldIds.Partition)] PartitionRef Partition,
    [property: Orleans.Id(AtomicPartitionPlacementRequestFieldIds.PhysicalShardId)] Guid PhysicalShardId);
