using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PhysicalShardCatalog ReadInventoryPhysicalCatalog(IKeyValueView view)
    {
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryReadValidation.Missing);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (configuredPhysicalOwner is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryReadValidation.Missing);
        }
        if (!PhysicalOwnerEntryValidation.SameOwner(configuredPhysicalOwner, catalog.DefaultShard)
            || Store.Identity.Incarnation != configuredPhysicalOwner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryReadValidation.Changed);
        }
        return catalog;
    }

    private static long ReadInventoryPlacementRevision(IKeyValueView view)
    {
        var directory = AtomicPartitionPlacementSerialization.ReadDirectory(view);
        if (directory is null)
        {
            return ResourceOwnerInventoryReadValidation.EmptyRevision;
        }
        AtomicPartitionPlacementValidation.ValidateDirectory(directory);
        return directory.Revision;
    }

    private ResourceOwnerInventoryOwnerCutV1 CaptureInventoryOwnerCut(IKeyValueView view,
        ResourceOwnerInventoryControlV1 control, RegisteredPhysicalOwnerV1 observed)
    {
        var catalog = ReadInventoryPhysicalCatalog(view);
        if (!PhysicalOwnerEntryValidation.Valid(observed)
            || !PhysicalOwnerEntryValidation.SameOwner(observed.Owner, catalog.DefaultShard)
            || !control.Directory.Owners.Any(entry => PhysicalOwnerEntryValidation.Same(entry, observed)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryReadValidation.Changed);
        }
        var directory = PhysicalOwnerDirectorySerialization.Read(view);
        if (directory is not null && !PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, catalog.DefaultShard))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryReadValidation.Malformed);
        }
        var identity = Store.Identity;
        var enrollment = ResourceOwnerInventoryCatalog.RequireRevision(view, identity.Incarnation, Limits);
        var bytes = view.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryReadValidation.Missing);
        var applied = NativeSerialization.Deserialize<long>(bytes);
        if (applied < ResourceOwnerInventoryReadValidation.EmptyRevision)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryReadValidation.Malformed);
        }
        return new(ResourceOwnerInventoryReadValidation.Version, identity.NodeId, identity.Incarnation,
            identity.ReadGeneration, catalog.DefaultShard, enrollment, catalog.Revision,
            directory?.Revision ?? ResourceOwnerInventoryReadValidation.EmptyRevision,
            ReadInventoryPlacementRevision(view), Store.Position, applied);
    }

    private ResourceOwnerInventoryEntryV1 ResolveInventoryEntry(IKeyValueView view,
        ResourceOwnerInventoryControlV1 control, ResourceOwnerInventoryOwnerCutV1 cut, PartitionRef partition)
    {
        RequireInventorySettledPartition(view, partition);
        var placement = ResolveRegisteredPlacement(view, partition, cut.ObservedOwner);
        var route = control.Directory.Owners.SingleOrDefault(entry => entry.Owner.PhysicalShardId == placement.PhysicalShardId);
        if (route is null || route.Owner.Incarnation != placement.Incarnation
            || !route.Owner.VoterIds.SequenceEqual(placement.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryReadValidation.Missing);
        }
        return new(control.Lifetime, partition, cut.ObservedOwner, placement, route);
    }

    private void RequireInventorySettledPartition(IKeyValueView view, PartitionRef partition)
    {
        if (view.ReadOwnedValue(PartitionMoveParentKeys.Active(partition)) is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryReadValidation.Missing);
        }
        var control = PartitionMoveControlStorage.Read(view, partition, Limits.MaxBatchBytes);
        if (control is not null && control.Phase is not (PartitionMovePhase.Retired or PartitionMovePhase.Aborted))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryReadValidation.Missing);
        }
        var stage = PartitionMoveTargetStorage.Read<PartitionMoveTargetStage>(view,
            PartitionMoveTargetStorage.Key(partition), Limits.MaxBatchBytes);
        if (stage is not null && (stage.Version != PartitionMoveProtocol.Version || stage.Control is null
            || stage.Control.Partition != partition))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryReadValidation.Malformed);
        }
        var fence = PartitionMoveSourceFenceStorage.Read(view, partition, Limits.MaxBatchBytes);
        if (stage is not null && !stage.Published || fence is not null && control is null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryReadValidation.Missing);
        }
    }
}
