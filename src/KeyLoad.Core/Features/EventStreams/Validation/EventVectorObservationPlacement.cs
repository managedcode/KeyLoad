using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

internal static class EventVectorObservationPlacement
{
    private const string InvalidPlacement = "The actual event vector source observation placement is inconsistent.";

    internal static void Require(EventVectorSourcePhase admitted, EventVectorSourceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        ArgumentNullException.ThrowIfNull(observation);
        var placement = observation.Placement;
        if (placement is null || observation.SourceOwner is null
            || !PhysicalOwnerEntryValidation.SameOwner(admitted.SourceOwner, observation.SourceOwner)
            || placement.IsFallback || placement.Partition != admitted.Source.Partition
            || placement.PhysicalShardId != admitted.SourceOwner.PhysicalShardId
            || placement.Incarnation != admitted.SourceOwner.Incarnation
            || placement.PlacementEpoch != admitted.SourceOwner.PlacementEpoch
            || placement.Revision != admitted.SourceLogicalPlacementRevision
            || placement.DirectoryRevision != admitted.SourceDirectoryFence
            || !placement.VoterIds.SequenceEqual(admitted.SourceOwner.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, InvalidPlacement); }
    }
}
