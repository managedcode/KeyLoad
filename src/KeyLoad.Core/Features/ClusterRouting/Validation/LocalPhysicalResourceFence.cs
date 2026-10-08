using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireLocalResourceOwner(IKeyValueView view, PartitionRef partition)
    {
        if (configuredPhysicalOwner is null)
        { return; }
        var placement = ReadPlacementWitness(view, partition);
        if (placement.PhysicalShardId != configuredPhysicalOwner.PhysicalShardId
            || placement.Incarnation != configuredPhysicalOwner.Incarnation
            || placement.PlacementEpoch != configuredPhysicalOwner.PlacementEpoch
            || !placement.VoterIds.SequenceEqual(configuredPhysicalOwner.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, ForeignPlacementExecution); }
    }
}
