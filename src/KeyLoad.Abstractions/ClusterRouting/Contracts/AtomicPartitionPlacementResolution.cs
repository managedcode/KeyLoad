using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>One same-view placement witness, including both global and per-row revisions.</summary>
/// <param name="Version">The public result version.</param>
/// <param name="Partition">The complete partition identity resolved.</param>
/// <param name="PhysicalShardId">The committed default physical shard.</param>
/// <param name="Incarnation">The committed physical owner incarnation.</param>
/// <param name="VoterIds">The exact ordered committed voter identities.</param>
/// <param name="PlacementEpoch">The committed physical placement epoch.</param>
/// <param name="DirectoryRevision">The current global directory revision from the same read view.</param>
/// <param name="Revision">The explicit row revision, or zero for fallback.</param>
/// <param name="IsFallback">Whether the partition has no explicit row.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionPlacementAliases.Resolution)]
public sealed record AtomicPartitionPlacementResolution(
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.Partition)] PartitionRef Partition,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.PhysicalShardId)] Guid PhysicalShardId,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.Incarnation)] Guid Incarnation,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.VoterIds)] ImmutableArray<string> VoterIds,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.PlacementEpoch)] long PlacementEpoch,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.DirectoryRevision)] long DirectoryRevision,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.Revision)] long Revision,
    [property: Orleans.Id(AtomicPartitionPlacementResolutionFieldIds.IsFallback)] bool IsFallback);
