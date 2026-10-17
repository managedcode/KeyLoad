using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Complete bounded catalog census from the actual owning native archive view, without user payload or credentials.</summary>
/// <param name="Version">Current capture format.</param>
/// <param name="CaptureId">Stable identity of this real capture invocation.</param>
/// <param name="Owner">Actual configured physical owner admitted against persisted catalog.</param>
/// <param name="Catalog">Actual complete native physical catalog.</param>
/// <param name="Directory">Actual explicit placement directory, if present.</param>
/// <param name="StorePosition">Exact archived physical position.</param>
/// <param name="AppliedIndex">Actual physical materialized replica cut.</param>
/// <param name="Partitions">Complete native monotonic roster at the same view.</param>
/// <param name="RegisteredOwners">Actual registered physical owner directory, if present.</param>
/// <param name="SourceNodeId">Actual source archive native node identity.</param>
/// <param name="CanonicalDigest">Length-framed SHA256 of every canonical key/value at that view.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterBackupOwnerCut.SerializerAlias)]
public sealed record ClusterBackupOwnerCut(
    [property: Orleans.Id(ClusterBackupOwnerCut.VersionField)] int Version,
    [property: Orleans.Id(ClusterBackupOwnerCut.CaptureField)] Guid CaptureId,
    [property: Orleans.Id(ClusterBackupOwnerCut.OwnerField)] PhysicalShardRecord Owner,
    [property: Orleans.Id(ClusterBackupOwnerCut.CatalogField)] PhysicalShardCatalog Catalog,
    [property: Orleans.Id(ClusterBackupOwnerCut.DirectoryField)] AtomicPartitionPlacementDirectoryV1? Directory,
    [property: Orleans.Id(ClusterBackupOwnerCut.PositionField)] long StorePosition,
    [property: Orleans.Id(ClusterBackupOwnerCut.AppliedField)] long AppliedIndex,
    [property: Orleans.Id(ClusterBackupOwnerCut.PartitionsField)] ImmutableArray<ClusterBackupPartitionCut> Partitions,
    [property: Orleans.Id(ClusterBackupOwnerCut.DigestField)] string CanonicalDigest,
    [property: Orleans.Id(ClusterBackupOwnerCut.RegisteredOwnersField)] PhysicalOwnerDirectoryV1? RegisteredOwners,
    [property: Orleans.Id(ClusterBackupOwnerCut.NodeField)] Guid SourceNodeId)
{
    internal const string SerializerAlias = "keyload.backup.cluster-owner-cut.v1";
    /// <summary>The current generated-native owner capture version.</summary>
    public const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int CaptureField = 1;
    private const int OwnerField = 2;
    private const int CatalogField = 3;
    private const int DirectoryField = 4;
    private const int PositionField = 5;
    private const int AppliedField = 6;
    private const int PartitionsField = 7;
    private const int DigestField = 8;
    private const int RegisteredOwnersField = 9;
    private const int NodeField = 10;
}
