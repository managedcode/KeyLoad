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
        var bytes = view.ReadOwnedValue(KeySpace.AppliedBytes);
        var applied = bytes is null ? ResourceOwnerInventoryReadValidation.EmptyRevision
            : NativeSerialization.Deserialize<long>(bytes);
        if (applied < ResourceOwnerInventoryReadValidation.EmptyRevision)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryReadValidation.Malformed);
        }
        return new(ResourceOwnerInventoryReadValidation.Version, identity.NodeId, identity.Incarnation,
            identity.ReadGeneration, catalog.DefaultShard, enrollment, catalog.Revision,
            directory?.Revision ?? ResourceOwnerInventoryReadValidation.EmptyRevision,
            ResourceOwnerInventoryReadValidation.EmptyRevision, Store.Position, applied);
    }

    private static ResourceOwnerInventoryEntryV1 ResolveInventoryEntry(ResourceOwnerInventoryControlV1 control,
        ResourceOwnerInventoryOwnerCutV1 cut, PartitionRef partition)
    {
        var placement = ResolveRegisteredPlacement(partition, cut.ObservedOwner);
        var route = control.Directory.Owners.SingleOrDefault(entry => entry.Owner.PhysicalShardId == placement.PhysicalShardId);
        if (route is null || route.Owner.Incarnation != placement.Incarnation
            || !route.Owner.VoterIds.SequenceEqual(placement.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceOwnerInventoryReadValidation.Missing);
        }
        return new(control.Lifetime, partition, cut.ObservedOwner, placement, route);
    }
}
