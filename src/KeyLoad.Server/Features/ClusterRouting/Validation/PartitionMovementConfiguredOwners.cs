using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Allows only the two explicitly configured owners; logical source epoch is not group incarnation.</summary>
internal static class PartitionMovementConfiguredOwners
{
    internal static bool RequirePair(PartitionMovePeerEnvelope original, PhysicalShardRecord control,
        PhysicalShardRecord configuredPeer)
        => MatchSource(original.SourcePlacement, control)
            && PhysicalOwnerEntryValidation.SameOwner(original.DestinationOwner, configuredPeer)
            || MatchSource(original.SourcePlacement, configuredPeer)
            && PhysicalOwnerEntryValidation.SameOwner(original.DestinationOwner, control);

    private static bool MatchSource(AtomicPartitionPlacementResolution placement, PhysicalShardRecord owner)
        => placement.PhysicalShardId == owner.PhysicalShardId && placement.Incarnation == owner.Incarnation
            && placement.VoterIds.SequenceEqual(owner.VoterIds, StringComparer.Ordinal);
}
