using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireLocalResourceOwner(IKeyValueView view, PartitionRef partition)
    {
        if (configuredPhysicalOwner is null)
        { return; }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.Corruption, Features.ClusterRouting.Contracts.PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, configuredPhysicalOwner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, ForeignPlacementExecution); }
        var placement = ReadPlacementWitness(view, partition);
        if (placement.PhysicalShardId != configuredPhysicalOwner.PhysicalShardId
            || placement.Incarnation != configuredPhysicalOwner.Incarnation
            || !placement.VoterIds.SequenceEqual(configuredPhysicalOwner.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, ForeignPlacementExecution); }
    }
}
