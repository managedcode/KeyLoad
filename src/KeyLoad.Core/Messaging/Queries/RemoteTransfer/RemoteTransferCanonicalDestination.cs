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
        {
            return null;
        }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredPhysicalOwner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid);
        }
        var placement = ResolveRegisteredPlacement(destination.Partition, catalog.DefaultShard);
        if (placement.PhysicalShardId == configuredPhysicalOwner.PhysicalShardId)
        {
            return null;
        }
        var directory = PhysicalOwnerDirectorySerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        if (!PhysicalOwnerEntryValidation.SameOwner(directory.ControlOwner, configuredPhysicalOwner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid);
        }
        var selected = directory.Owners.SingleOrDefault(entry => entry.Owner.PhysicalShardId == placement.PhysicalShardId)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid);
        if (placement.Incarnation != selected.Owner.Incarnation
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteTransferPeerProtocol.Invalid);
        }
        return new(selected, RemoteTransferPeerProtocol.EmptyEncodedBytes);
    }

    private ResourceDefinition RemoteTransferDestinationResource(IKeyValueView view, QueueLaneRef destination)
    {
        if (CaptureRemoteTransferTarget(view, destination) is null)
        {
            return Resource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
        }
        return BindResourceDefinition(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
    }
}
