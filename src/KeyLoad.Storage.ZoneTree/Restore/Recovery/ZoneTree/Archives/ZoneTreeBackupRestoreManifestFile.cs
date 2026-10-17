namespace KeyLoad.Storage.ZoneTree;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.BackupFile)]
internal sealed record ZoneTreeBackupRestoreManifestFile(
    [property: global::Orleans.Id(0)] string Name,
    [property: global::Orleans.Id(1)] long Length,
    [property: global::Orleans.Id(2)] string Checksum);
