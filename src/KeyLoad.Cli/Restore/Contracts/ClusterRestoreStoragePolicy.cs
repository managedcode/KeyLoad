namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Exact first-admitted validated native storage policy; no IOptions owner or defaults reconstructed.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(ClusterRestoreStoragePolicy.SerializerAlias)]
internal sealed record ClusterRestoreStoragePolicy(
    [property: Orleans.Id(ClusterRestoreStoragePolicy.VersionField)] int Version,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.MaxFrameBytesField)] int MaxFrameBytes,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.MaxSnapshotBytesField)] long MaxSnapshotBytes,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.CheckpointBatchBytesField)] int CheckpointBatchBytes,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.CheckpointBatchRecordsField)] int CheckpointBatchRecords,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.MaximumRangeRecordsField)] int MaximumRangeRecords,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.MaximumRangeWorkBytesField)] long MaximumRangeWorkBytes,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.MaximumReadCutRecordsField)] int MaximumReadCutRecords,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.MaximumReadCutExaminedBytesField)] long MaximumReadCutExaminedBytes,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.MaximumReadCutElapsedField)] TimeSpan MaximumReadCutElapsed,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.NativeMaintenanceIntervalField)] TimeSpan NativeMaintenanceInterval,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.NativeBlockCacheLifetimeField)] TimeSpan NativeBlockCacheLifetime,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.FileBufferBytesField)] int FileBufferBytes,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.IdentityBufferBytesField)] int IdentityBufferBytes,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.StreamBufferBytesField)] int StreamBufferBytes,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.MaximumBackupManifestBytesField)] int MaximumBackupManifestBytes,
    [property: Orleans.Id(ClusterRestoreStoragePolicy.MaximumIdentityFileBytesField)] int MaximumIdentityFileBytes)
{
    internal const string SerializerAlias = "keyload.cli.cluster-restore.storage-policy.v1";
    internal const int CurrentVersion = 1;
    private const int VersionField = 0;
    private const int MaxFrameBytesField = 1;
    private const int MaxSnapshotBytesField = 2;
    private const int CheckpointBatchBytesField = 3;
    private const int CheckpointBatchRecordsField = 4;
    private const int MaximumRangeRecordsField = 5;
    private const int MaximumRangeWorkBytesField = 6;
    private const int MaximumReadCutRecordsField = 7;
    private const int MaximumReadCutExaminedBytesField = 8;
    private const int MaximumReadCutElapsedField = 9;
    private const int NativeMaintenanceIntervalField = 10;
    private const int NativeBlockCacheLifetimeField = 11;
    private const int FileBufferBytesField = 12;
    private const int IdentityBufferBytesField = 13;
    private const int StreamBufferBytesField = 14;
    private const int MaximumBackupManifestBytesField = 15;
    private const int MaximumIdentityFileBytesField = 16;
}
