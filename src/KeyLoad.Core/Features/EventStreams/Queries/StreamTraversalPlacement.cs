using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private AtomicPartitionPlacementResolution StreamTraversalPlacement(IKeyValueView view,
        PartitionRef partition, PhysicalShardRecord? configuredOwner)
    {
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, StreamTraversalProtocol.Owner);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var expected = configuredOwner ?? catalog.DefaultShard;
        var registered = ResolveRegisteredPlacementOwner(view, catalog.DefaultShard,
            expected.PhysicalShardId, ErrorCode.OwnershipLost);
        if (expected.Incarnation != Store.Identity.Incarnation
            || !PhysicalOwnerEntryValidation.SameOwner(expected, registered))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StreamTraversalProtocol.Owner);
        }
        var placement = ResolveRegisteredPlacement(view, partition, catalog.DefaultShard);
        if (placement.PhysicalShardId != expected.PhysicalShardId || placement.Incarnation != expected.Incarnation
            || placement.PlacementEpoch != expected.PlacementEpoch
            || !placement.VoterIds.SequenceEqual(expected.VoterIds, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StreamTraversalProtocol.Owner);
        }
        return placement;
    }
}
