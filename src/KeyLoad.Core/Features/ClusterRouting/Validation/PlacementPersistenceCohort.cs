using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PlacementPersistenceCohort
{
    internal static void Require(TenantPlacementPlanClaims original,
        ImmutableArray<OwnerCapacitySnapshotV1> fresh, DateTimeOffset evaluatedAt)
    {
        PlacementCapacityWitnessValidation.Require(original, evaluatedAt);
        var capacity = PlacementCapacityCohort.Require(fresh, original.Plan.CohortFingerprint, evaluatedAt);
        PlacementCapacityPartitionSet.Require(original.Plan, capacity);
        foreach (var sample in capacity)
        {
            var witness = original.OriginalCapacityWitnesses.FirstOrDefault(item => item.VoterId == sample.VoterId);
            if (witness is null || sample.SiloAddress != witness.OriginalSiloAddress
                || sample.Owner.Incarnation != witness.Incarnation
                || sample.Owner.PhysicalShardId != witness.PhysicalShardId)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort);
            }
        }
    }
}
