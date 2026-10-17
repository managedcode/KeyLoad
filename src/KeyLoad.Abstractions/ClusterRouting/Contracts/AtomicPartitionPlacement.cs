using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Bounds and identifies the generated atomic-placement directory.</summary>
/// <param name="Version">The directory format version.</param>
/// <param name="Revision">The monotonic directory revision.</param>
/// <param name="ExplicitAssignmentCount">The number of explicit atomic assignments.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionPlacementAliases.Directory)]
public sealed record AtomicPartitionPlacementDirectoryV1(
    [property: Orleans.Id(AtomicPartitionPlacementDirectoryFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionPlacementDirectoryFieldIds.Revision)] long Revision,
    [property: Orleans.Id(AtomicPartitionPlacementDirectoryFieldIds.ExplicitAssignmentCount)] int ExplicitAssignmentCount);

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

/// <summary>Reads one partition assignment and the committed default-owner tuple from one read view.</summary>
/// <param name="Version">The public request version.</param>
/// <param name="Partition">The complete four-field atomic partition identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(AtomicPartitionPlacementAliases.ReadRequest)]
public sealed record AtomicPartitionPlacementReadRequest(
    [property: Orleans.Id(AtomicPartitionPlacementReadRequestFieldIds.Version)] int Version,
    [property: Orleans.Id(AtomicPartitionPlacementReadRequestFieldIds.Partition)] PartitionRef Partition);

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

