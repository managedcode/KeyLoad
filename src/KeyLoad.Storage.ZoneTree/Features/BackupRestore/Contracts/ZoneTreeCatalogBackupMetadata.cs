namespace KeyLoad.Storage.ZoneTree;

[global::Orleans.GenerateSerializer]
[global::Orleans.Alias(ZoneTreeCatalogBackupMetadata.SerializerAlias)]
internal sealed record ZoneTreeCatalogBackupMetadata(
    [property: global::Orleans.Id(ZoneTreeCatalogBackupMetadata.VersionField)] int Version,
    [property: global::Orleans.Id(ZoneTreeCatalogBackupMetadata.PositionField)] long Position,
    [property: global::Orleans.Id(ZoneTreeCatalogBackupMetadata.NodeIdField)] Guid NodeId,
    [property: global::Orleans.Id(ZoneTreeCatalogBackupMetadata.IncarnationField)] Guid Incarnation,
    [property: global::Orleans.Id(ZoneTreeCatalogBackupMetadata.ManifestDigestField)] string ManifestDigest,
    [property: global::Orleans.Id(ZoneTreeCatalogBackupMetadata.MetadataField)] ReadOnlyMemory<byte> Metadata)
{
    internal const string SerializerAlias = "keyload.storage.catalog-backup-metadata.v1";
    internal const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int PositionField = 1;
    private const int NodeIdField = 2;
    private const int IncarnationField = 3;
    private const int ManifestDigestField = 4;
    private const int MetadataField = 5;
}
