using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Cli.Features.BackupRestore;

internal static class ClusterRestorePolicies
{
    internal static ClusterRestoreStoragePolicy Capture(ZoneTreeStorageExecutionOptions actual)
    {
        actual.Validate();
        return new(ClusterRestoreStoragePolicy.CurrentVersion,
            actual.MaxFrameBytes,
            actual.MaxSnapshotBytes,
            actual.CheckpointBatchBytes,
            actual.CheckpointBatchRecords,
            actual.MaximumRangeRecords,
            actual.MaximumRangeWorkBytes,
            actual.MaximumReadCutRecords,
            actual.MaximumReadCutExaminedBytes,
            actual.MaximumReadCutElapsed,
            actual.NativeMaintenanceInterval,
            actual.NativeBlockCacheLifetime,
            actual.FileBufferBytes,
            actual.IdentityBufferBytes,
            actual.StreamBufferBytes,
            actual.MaximumBackupManifestBytes,
            actual.MaximumIdentityFileBytes);
    }
}
