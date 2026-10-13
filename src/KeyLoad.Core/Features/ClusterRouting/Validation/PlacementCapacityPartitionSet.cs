using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PlacementCapacityPartitionSet
{
    internal static void Require(TenantPlacementPlanV1 plan, ImmutableArray<OwnerCapacitySnapshotV1> fresh)
    {
        var actual = fresh.SelectMany(item => item.CompleteRoster).Select(item => item.Partition)
            .Where(part => part.TenantId == plan.TenantId && part.DatabaseId == plan.DatabaseId
                && part != plan.ControlPartition).Distinct().ToArray();
        if (actual.Length != plan.OriginalPartitions.Length || actual.Except(plan.OriginalPartitions).Any())
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
    }
}
