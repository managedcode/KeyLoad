namespace KeyLoad;

/// <summary>One exact immutable roster occurrence and current physical placement at its owning archived cut.</summary>
/// <param name="Version">Current cut record version.</param>
/// <param name="Roster">Original native first-seen row, unchanged by capture.</param>
/// <param name="RosterDigest">SHA256 of the exact stored row bytes.</param>
/// <param name="Placement">Actual current same-view physical owner witness.</param>
/// <param name="CanonicalRecordCount">Actual same-cut scoped native record count, including legal empty scopes.</param>
/// <param name="CanonicalDigest">SHA256 of the exact same-cut scoped native records.</param>
/// <param name="AppliedIndex">Actual physical apply cut shared by this logical partition.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterBackupPartitionCut.SerializerAlias)]
public sealed record ClusterBackupPartitionCut(
    [property: Orleans.Id(ClusterBackupPartitionCut.VersionField)] int Version,
    [property: Orleans.Id(ClusterBackupPartitionCut.RosterField)] AtomicPartitionCatalogEntryV1 Roster,
    [property: Orleans.Id(ClusterBackupPartitionCut.DigestField)] string RosterDigest,
    [property: Orleans.Id(ClusterBackupPartitionCut.PlacementField)] AtomicPartitionPlacementResolution Placement,
    [property: Orleans.Id(ClusterBackupPartitionCut.AppliedField)] long AppliedIndex,
    [property: Orleans.Id(ClusterBackupPartitionCut.CountField)] long CanonicalRecordCount,
    [property: Orleans.Id(ClusterBackupPartitionCut.CanonicalDigestField)] string CanonicalDigest)
{
    internal const string SerializerAlias = "keyload.backup.cluster-partition-cut.v1";
    private const int VersionField = 0;
    private const int RosterField = 1;
    private const int DigestField = 2;
    private const int PlacementField = 3;
    private const int AppliedField = 4;
    private const int CountField = 5;
    private const int CanonicalDigestField = 6;
}
