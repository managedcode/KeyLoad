namespace KeyLoad.Storage.ZoneTree;

internal static class ZoneTreeSnapshotUpgradeOutput
{
    internal static StorageSnapshot Write(string path, ZoneTreeSnapshotUpgradeSettings settings,
        StorageSnapshot snapshot, Action outputCreated, Action<Action<StorageMutation>> visit)
    {
        using var lease = ZoneTreeSnapshotUpgradeFileLease.CreatePrivateOutput(path, settings.Descriptor.FileBufferBytes);
        try
        {
            outputCreated();
            return ZoneTreeSnapshotUpgradeRunner.WriteOutput(lease.Stream, path, settings, snapshot, visit);
        }
        catch (Exception error)
        {
            lease.RecordPrimary(error);
            throw;
        }
    }
}
