using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RemoteTransferRemoteTarget? CaptureRemoteTransferTarget(IKeyValueView view, QueueLaneRef destination)
    {
        if (configuredPhysicalOwner is null)
        { return null; }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredPhysicalOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid); }
        var placement = ResolveRegisteredPlacement(view, destination.Partition, catalog.DefaultShard);
        if (placement.PhysicalShardId == configuredPhysicalOwner.PhysicalShardId)
        {
            RequireLocalResourceOwner(view, destination.Partition);
            return null;
        }
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, configuredPhysicalOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid); }
        var selected = directory.Owners.SingleOrDefault(entry => entry.Owner.PhysicalShardId == placement.PhysicalShardId)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid);
        if (placement.Incarnation != selected.Owner.Incarnation
            || !placement.VoterIds.SequenceEqual(selected.Owner.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid); }
        return new(selected, RemoteTransferPeerProtocol.EmptyEncodedBytes);
    }

    private ResourceDefinition RemoteTransferDestinationResource(IKeyValueView view, QueueLaneRef destination)
    {
        if (CaptureRemoteTransferTarget(view, destination) is null)
        { return Resource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue); }
        RequireNoUnpublishedPartitionMoveTarget(view, destination.Partition);
        var resource = view.GetRecord<ResourceDefinition>(KeySpace.Resource(destination.Partition.TenantId,
            destination.Partition.DatabaseId, destination.Queue))
            ?? throw Errors.Fail(ErrorCode.NotFound, DatabaseEngineResourceIsNotConfiguredDetail);
        if (resource.TransactionDomainId != destination.Partition.TransactionDomainId)
        { throw Errors.Fail(ErrorCode.Conflict, DatabaseEngineResourceBelongsToADifferentTransactionDomainDetail); }
        if (resource.Kind != ResourceKind.WorkQueue)
        { throw Errors.Fail(ErrorCode.Validation, DatabaseEngineResourceHasADifferentKindDetail); }
        return resource;
    }
}
