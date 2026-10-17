namespace KeyLoad.Storage.ZoneTree;

[global::Orleans.GenerateSerializer, global::Orleans.Alias(ZoneTreeMetadataAliases.BackupManifest)]
internal sealed record ZoneTreeBackupRestoreManifest(
    [property: global::Orleans.Id(0)] int Version,
    [property: global::Orleans.Id(1)] long Position,
    [property: global::Orleans.Id(2)] ZoneTreeBackupRestoreManifestFile[] Files);
