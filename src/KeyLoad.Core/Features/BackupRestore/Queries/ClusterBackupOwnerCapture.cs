using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const long ClusterBackupNoAppliedIndex = 0;
    private const string ClusterBackupConfiguredOwnerRequired = "Cluster capture requires its actual configured physical owner.";
    private const string ClusterBackupActiveMovement = "Cluster capture cannot omit unsettled partition movement authority.";

    /// <summary>Reads complete native catalog metadata inside the actual provider-owned archive gate.</summary>
    /// <param name="view">Borrowed same-cut view supplied only by the native capture owner.</param>
    /// <param name="principalId">Actual authenticated principal, revalidated against persisted policy inside this gate.</param>
    /// <param name="captureId">Stable identity of this capture invocation.</param>
    /// <param name="position">Actual gated provider position.</param>
    /// <param name="identity">Actual gated provider identity.</param>
    /// <param name="work">Original bounded operation work and cancellation.</param>
    /// <returns>Independently owned current-format owner/cut metadata.</returns>
    public ClusterBackupOwnerCut CaptureClusterBackupOwner(IKeyValueView view, string principalId, Guid captureId,
        long position, StoreIdentity identity, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (captureId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, ClusterBackupConfiguredOwnerRequired); }
        var admitted = work.CreateView(view);
        var catalog = ReadPhysicalShardCatalog(admitted, principalId);
        if (configuredPhysicalOwner is null || identity.Incarnation != configuredPhysicalOwner.Incarnation
            || identity.NodeId != Store.Identity.NodeId || position != Store.Position
            || !PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredPhysicalOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, ClusterBackupConfiguredOwnerRequired); }
        var bytes = admitted.ReadOwnedValue(KeySpace.AppliedBytes);
        var applied = bytes is null ? ClusterBackupNoAppliedIndex : NativeSerialization.Deserialize<long>(bytes);
        if (applied <= ClusterBackupNoAppliedIndex)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, ClusterBackupConfiguredOwnerRequired); }
        var entries = ClusterBackupRosterCapture.Read(admitted, identity.Incarnation, position, applied, Limits);
        var census = ClusterBackupCanonicalCensus.Require(admitted, entries, Limits, work.Cancellation);
        var partitions = ImmutableArray.CreateBuilder<ClusterBackupPartitionCut>(entries.Count);
        foreach (var entry in entries)
        {
            work.Check();
            var partition = entry.Value.Entry.Partition;
            if (admitted.ReadOwnedValue(PartitionMoveParentKeys.Active(partition)) is not null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, ClusterBackupActiveMovement); }
            var placement = ResolveRegisteredPlacement(admitted, partition, catalog.DefaultShard);
            var scope = census.Partitions[partition];
            partitions.Add(new(ClusterBackupOwnerCut.CurrentVersion, entry.Value.Entry, entry.Value.Digest, placement, applied, scope.Count, scope.Digest));
        }
        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(admitted);
        var registered = PhysicalOwnerDirectorySerialization.Read(admitted);
        var result = new ClusterBackupOwnerCut(ClusterBackupOwnerCut.CurrentVersion, captureId, configuredPhysicalOwner,
            catalog, directory, position, applied, partitions.ToImmutable(), census.Digest, registered, identity.NodeId);
        work.CheckResult(result);
        work.Check();
        return result;
    }
}
