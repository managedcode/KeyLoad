using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Actual persisted store identity and local cut; this contains no cluster authority.</summary>
[GenerateSerializer]
public sealed record SiloStorageCut([property: Id(0)] Guid Incarnation,
    [property: Id(1)] long Position)
{
    /// <summary>A bounded vector when reading the whole database; empty for one partition's cut.</summary>
    [Id(2)] public ImmutableArray<StoragePartitionPosition> Partitions { get; init; } = [];
}
