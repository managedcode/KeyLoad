namespace KeyLoad.Storage.ZoneTree;

/// <summary>Explicitly verifies or converts one complete epoch-matched legacy snapshot into the current format.</summary>
public static class ZoneTreeSnapshotFormatUpgrade
{
    /// <summary>Validates one complete native3 snapshot without writing output.</summary>
    /// <param name="sourcePath">The stopped native3 source image.</param>
    /// <param name="expectedIncarnation">The required store incarnation.</param>
    /// <param name="options">Optional finite frame/snapshot budgets and existing snapshot observer.</param>
    /// <returns>The verified source cut and record count.</returns>
    public static StorageSnapshot VerifySource(string sourcePath, Guid expectedIncarnation,
        ZoneTreeSnapshotUpgradeOptions? options = null)
        => ZoneTreeSnapshotUpgradeRunner.VerifySource(sourcePath, expectedIncarnation,
            ZoneTreePersistenceFormat.Native5DataEpoch, options);

    /// <summary>Validates a stopped source image against its checksummed store data epoch.</summary>
    public static StorageSnapshot VerifySource(string sourcePath, Guid expectedIncarnation, int sourceDataEpoch,
        ZoneTreeSnapshotUpgradeOptions? options = null)
        => ZoneTreeSnapshotUpgradeRunner.VerifySource(sourcePath, expectedIncarnation, sourceDataEpoch, options);

    /// <summary>Converts one complete native3 image to a new current-format image without changing the source.</summary>
    /// <param name="sourcePath">The stopped native3 source image.</param>
    /// <param name="destinationPath">An absent output file distinct from the source.</param>
    /// <param name="expectedIncarnation">The required store incarnation.</param>
    /// <param name="options">Optional finite frame/snapshot budgets and existing snapshot observer.</param>
    /// <returns>The source cut and record count preserved in the current image.</returns>
    public static StorageSnapshot Upgrade(string sourcePath, string destinationPath, Guid expectedIncarnation,
        ZoneTreeSnapshotUpgradeOptions? options = null)
        => ZoneTreeSnapshotUpgradeRunner.Upgrade(sourcePath, destinationPath, expectedIncarnation,
            ZoneTreePersistenceFormat.Native5DataEpoch, options);

    /// <summary>Converts an epoch-matched stopped source image to a new current-format image.</summary>
    public static StorageSnapshot Upgrade(string sourcePath, string destinationPath, Guid expectedIncarnation,
        int sourceDataEpoch, ZoneTreeSnapshotUpgradeOptions? options = null)
        => ZoneTreeSnapshotUpgradeRunner.Upgrade(sourcePath, destinationPath, expectedIncarnation,
            sourceDataEpoch, options);
}
