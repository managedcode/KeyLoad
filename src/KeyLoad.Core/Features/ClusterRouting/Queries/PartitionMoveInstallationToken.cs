using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private CommitToken MoveInstallationToken(IKeyValueView view,
        PartitionMovePhaseCommand phase, long position)
    {
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        var actual = catalog.DefaultShard;
        if (position <= PartitionMoveProtocol.EmptyCount || actual.Incarnation != Store.Identity.Incarnation
            || !PhysicalOwnerEntryValidation.SameOwner(actual, phase.DestinationOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return new(actual.Incarnation, phase.Partition.AtomicPartitionId, position, actual.PlacementEpoch);
    }
}
