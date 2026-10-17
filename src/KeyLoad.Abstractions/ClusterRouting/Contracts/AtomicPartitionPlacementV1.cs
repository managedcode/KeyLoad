using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Assigns one complete atomic partition to a physical shard.</summary>
/// <param name="Version">The assignment row format version.</param>
/// <param name="Partition">The complete four-field atomic identity.</param>
/// <param name="PhysicalShardId">The assigned physical shard identity.</param>
/// <param name="Revision">The positive revision of this assignment.</param>
/// <param name="Incarnation">The committed physical owner incarnation.</param>
/// <param name="VoterIds">The exact ordered committed voter identities.</param>
/// <param name="PlacementEpoch">The committed physical placement epoch.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionPlacementAliases.Row)]
public sealed record AtomicPartitionPlacementV1(
    [property: Orleans.Id(AtomicPartitionPlacementRowFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionPlacementRowFieldIds.Partition)] PartitionRef Partition,
    [property: Orleans.Id(AtomicPartitionPlacementRowFieldIds.PhysicalShardId)] Guid PhysicalShardId,
    [property: Orleans.Id(AtomicPartitionPlacementRowFieldIds.Revision)] long Revision,
    [property: Orleans.Id(AtomicPartitionPlacementRowFieldIds.Incarnation)] Guid Incarnation,
    [property: Orleans.Id(AtomicPartitionPlacementRowFieldIds.VoterIds)] ImmutableArray<string> VoterIds,
    [property: Orleans.Id(AtomicPartitionPlacementRowFieldIds.PlacementEpoch)] long PlacementEpoch);
