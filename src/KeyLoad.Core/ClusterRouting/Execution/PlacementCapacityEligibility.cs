using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core.Features.ClusterRouting;

internal static class PlacementCapacityEligibility
{
    private const int BasisPoints = 10_000;

    internal static bool Eligible(TenantPlacementPolicyV1 policy, PhysicalShardRecord owner,
        ImmutableArray<OwnerCapacitySnapshotV1> cohort, long incomingBytes)
    {
        var voters = cohort.Where(item => PhysicalOwnerEntryValidation.SameOwner(item.Owner, owner)).ToArray();
        if (voters.Length != owner.VoterIds.Length
            || policy.RequireDistinctHosts && voters.Select(item => item.Resource.HostDomain)
                .Distinct(StringComparer.Ordinal).Count() != voters.Length)
        {
            return false;
        }
        return voters.All(item => Fits(policy, item.Resource, incomingBytes));
    }

    private static bool Fits(TenantPlacementPolicyV1 policy, OwnerResourceSampleV1 value, long incomingBytes)
    {
        var remaining = value.AvailableDiskBytes - incomingBytes;
        return remaining >= policy.MinimumFreeBytes
            && (decimal)remaining * BasisPoints >= (decimal)value.TotalDiskBytes * policy.MinimumFreeBasisPoints
            && (decimal)value.MemoryCurrentBytes * BasisPoints <= (decimal)value.MemoryLimitBytes * policy.MaximumMemoryBasisPoints
            && value.CpuBasisPoints <= policy.MaximumCpuBasisPoints;
    }
}
