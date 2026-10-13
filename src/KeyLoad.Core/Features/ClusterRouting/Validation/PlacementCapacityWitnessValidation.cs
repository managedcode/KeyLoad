using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PlacementCapacityWitnessValidation
{
    private const int PhysicalOwnerGroups = 2;
    private const int OriginalCohortCount = 6;
    private const int MembersPerOwner = 3;
    private const long MinimumSequence = 1;
    private const long EmptyPosition = 0;

    internal static void Require(TenantPlacementPlanClaims claims, DateTimeOffset now)
    {
        var originals = claims.OriginalCapacityWitnesses;
        if (originals.IsDefault || originals.Length != OriginalCohortCount
            || originals.Select(item => item.OriginalQueryNonce).Distinct().Count() != OriginalCohortCount
            || originals.Select(item => item.OriginalRequestId).Distinct().Count() != OriginalCohortCount
            || originals.Select(item => item.VoterId).Distinct(StringComparer.Ordinal).Count() != OriginalCohortCount
            || originals.Select(item => item.OriginalSiloAddress).Distinct(StringComparer.Ordinal).Count() != OriginalCohortCount
            || originals.GroupBy(item => item.PhysicalShardId).Any(group => group.Count() != MembersPerOwner)
            || originals.Select(item => item.PhysicalShardId).Distinct().Count() != PhysicalOwnerGroups
            || originals.Any(item => item.OriginalRequestId == Guid.Empty || item.OriginalQueryNonce == Guid.Empty
                || item.PhysicalShardId == Guid.Empty || item.Incarnation == Guid.Empty
                || string.IsNullOrWhiteSpace(item.VoterId) || string.IsNullOrWhiteSpace(item.OriginalSiloAddress)
                || item.OriginalSampleSequence < MinimumSequence || item.OriginalStorePosition < EmptyPosition
                || item.OriginalAppliedPosition < EmptyPosition || item.OriginalSampledAt > now
                || item.OriginalSampleDeadline <= now || item.OriginalSampleDeadline <= item.OriginalSampledAt
                || !claims.Plan.CapacityDigests.Contains(item.OriginalSnapshotDigest, StringComparer.Ordinal))
            || claims.Plan.SampledAt != originals.Min(item => item.OriginalSampledAt)
            || claims.Plan.ExpiresAt > originals.Min(item => item.OriginalSampleDeadline))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ExpiredPlan); }
    }
}
