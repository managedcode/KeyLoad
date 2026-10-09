namespace KeyLoad;

/// <summary>Identifies one genuine complete immutable native owner archive; it is not a replication authority token.</summary>
/// <param name="Version">Current receipt version.</param>
/// <param name="ArchiveId">Native owner-local immutable archive identity, without a filesystem path.</param>
/// <param name="Cut">Exact original catalog, owner and canonical cut metadata.</param>
/// <param name="ManifestDigest">SHA-256 of the complete fourth native catalog envelope, including its original inner archive-manifest binding.</param>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterBackupOwnerReceipt.SerializerAlias)]
public sealed record ClusterBackupOwnerReceipt(
    [property: Orleans.Id(ClusterBackupOwnerReceipt.VersionField)] int Version,
    [property: Orleans.Id(ClusterBackupOwnerReceipt.ArchiveField)] string ArchiveId,
    [property: Orleans.Id(ClusterBackupOwnerReceipt.CutField)] ClusterBackupOwnerCut Cut,
    [property: Orleans.Id(ClusterBackupOwnerReceipt.DigestField)] string ManifestDigest)
{
    internal const string SerializerAlias = "keyload.backup.cluster-owner-receipt.v1";
    /// <summary>The current receipt version.</summary>
    public const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int ArchiveField = 1;
    private const int CutField = 2;
    private const int DigestField = 3;
}
