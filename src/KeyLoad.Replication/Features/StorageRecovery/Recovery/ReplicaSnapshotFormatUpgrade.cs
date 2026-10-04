using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Replication;

/// <summary>Preflights and converts immutable checkpoint images in a stopped private replica copy.</summary>
public static class ReplicaSnapshotFormatUpgrade
{
    /// <summary>Validates the replica authority and every historical source image without modifying either.</summary>
    /// <param name="canonical">Borrowed current canonical database in the private node copy.</param>
    /// <param name="replica">Borrowed current replica metadata store in the private node copy.</param>
    /// <param name="configuration">Fixed voter and destination scope for this physical node.</param>
    /// <param name="sourceSnapshots">Snapshot directory from the stopped source copy.</param>
    /// <param name="verifySourceImage">Read-only native source-format verifier returning its logical cut.</param>
    /// <returns>An immutable plan containing only copied metadata, cuts and source digests.</returns>
    public static ReplicaSnapshotUpgradePlan Preflight(DatabaseEngine canonical, IAtomicStore replica,
        ReplicaConfiguration configuration, string sourceSnapshots, Func<string, StorageSnapshot> verifySourceImage)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(replica);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(verifySourceImage);
        configuration.Validate();
        var source = Path.GetFullPath(sourceSnapshots);
        var destination = Path.GetFullPath(Path.Combine(configuration.Directory, ReplicaProtocol.SnapshotDirectory));
        ReplicaSnapshotUpgradeInventory.RejectLinks(source);
        ReplicaSnapshotUpgradeInventory.RejectLinks(destination);
        if (ReplicaSnapshotUpgradeExecution.Overlaps(source, destination))
        { throw Errors.Fail(ErrorCode.Conflict, ReplicaSnapshotUpgradeExecution.DestinationConflict); }
        var canonicalBinding = ReplicaSnapshotUpgradeValidation.Bind(canonical.Store);
        var replicaBinding = ReplicaSnapshotUpgradeValidation.Bind(replica);
        ReplicaSnapshotUpgradeValidation.ValidateNodeScopes(canonicalBinding, replicaBinding, configuration);
        var persisted = ReplicaSnapshotUpgradeValidation.ReadHardState(replica, configuration);
        using var log = new DurableReplicaLog(replica, configuration, canonicalDatabase: canonical);
        if (log.State != persisted.State)
        { throw Errors.Fail(ErrorCode.Conflict, ReplicaSnapshotUpgradeValidation.InvalidAuthority); }
        var applied = ReplicaSnapshotUpgradeValidation.ReadAppliedPosition(canonical.Store);
        ReplicaSnapshotUpgradeValidation.ValidateAppliedCut(applied, log.State);
        var inventory = ReplicaSnapshotUpgradeInventory.Read(source, configuration,
            log.State.Snapshot, verifySourceImage);
        ReplicaSnapshotUpgradeValidation.ValidateImageCuts(inventory.Images, log.State, applied);
        ReplicaSnapshotUpgradeValidation.ValidatePointerCut(log, log.State.Snapshot, inventory.Images, applied);
        return new(inventory.Path, inventory.Images.ToImmutableArray(), canonicalBinding,
            replicaBinding, ReplicaSnapshotUpgradeValidation.Bind(configuration), log.State, persisted.Sha256, applied);
    }

    /// <summary>Converts every image in a preflighted set and updates only the existing current descriptor.</summary>
    /// <param name="plan">Read-only preflight plan bound to both copied stores and the source inventory.</param>
    /// <param name="canonical">Borrowed current canonical database in the private node copy.</param>
    /// <param name="replica">Borrowed current replica metadata store in the private node copy.</param>
    /// <param name="configuration">The unchanged fixed voter and destination scope.</param>
    /// <param name="destinationSnapshots">Absent snapshot directory under the destination node copy.</param>
    /// <param name="convertImage">Migration-only source-to-current image converter.</param>
    public static void Upgrade(ReplicaSnapshotUpgradePlan plan, DatabaseEngine canonical, IAtomicStore replica,
        ReplicaConfiguration configuration, string destinationSnapshots,
        Func<string, string, StorageSnapshot> convertImage)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(canonical);
        ArgumentNullException.ThrowIfNull(replica);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(convertImage);
        configuration.Validate();
        var destination = Path.GetFullPath(destinationSnapshots);
        ReplicaSnapshotUpgradeInventory.RejectLinks(destination);
        if (destination != Path.GetFullPath(Path.Combine(configuration.Directory, ReplicaProtocol.SnapshotDirectory))
            || ReplicaSnapshotUpgradeExecution.Overlaps(plan.SourceSnapshots, destination)
            || Directory.Exists(destination) || File.Exists(destination))
        { throw Errors.Fail(ErrorCode.Conflict, ReplicaSnapshotUpgradeExecution.DestinationConflict); }
        ReplicaSnapshotUpgradeValidation.Revalidate(plan, canonical, replica, configuration);
        ReplicaSnapshotUpgradeExecution.ConvertAndPublish(plan, canonical, replica, configuration,
            destination, convertImage);
    }
}
