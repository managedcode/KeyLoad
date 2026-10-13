using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

internal static class PlacementCapacityCohort
{
    private const int PhysicalOwnerGroups = 2;
    private const int CohortSize = 6;
    private const int OwnerVoters = 3;
    private const int NoCapacity = 0;

    internal static ImmutableArray<OwnerCapacitySnapshotV1> Require(
        ImmutableArray<OwnerCapacitySnapshotV1> snapshots, string cohort, DateTimeOffset now)
    {
        if (snapshots.IsDefault || snapshots.Length != CohortSize
            || snapshots.Select(item => item.VoterId).Distinct(StringComparer.Ordinal).Count() != CohortSize
            || snapshots.Select(item => item.SiloAddress).Distinct(StringComparer.Ordinal).Count() != CohortSize
            || snapshots.Select(item => item.Nonce).Distinct().Count() != CohortSize
            || snapshots.Select(item => item.RequestId).Distinct().Count() != CohortSize
            || snapshots.Any(item => item.CohortFingerprint != cohort || item.Nonce == Guid.Empty
                || item.RequestId == Guid.Empty || item.Resource.SampledAt > now || item.Resource.ExpiresAt <= now
                || string.IsNullOrWhiteSpace(item.Resource.HostDomain) || item.Resource.MemoryLimitBytes <= NoCapacity
                || item.Resource.MemoryCurrentBytes < NoCapacity || item.Resource.MemoryCurrentBytes > item.Resource.MemoryLimitBytes
                || item.Resource.Sequence <= NoCapacity || item.Resource.ProcessDomain != item.SiloAddress
                || item.Resource.ExpiresAt <= item.Resource.SampledAt || item.Resource.CpuBasisPoints < NoCapacity
                || item.Resource.TotalDiskBytes <= NoCapacity || item.Resource.AvailableDiskBytes < NoCapacity
                || item.Resource.AvailableDiskBytes > item.Resource.TotalDiskBytes
                || item.StorePosition < NoCapacity || item.AppliedPosition < NoCapacity
                || item.NativeReadBytes < NoCapacity || item.NativeExaminedRecords < NoCapacity
                || item.CompleteRoster.IsDefault))
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
        var groups = snapshots.GroupBy(item => item.Owner.PhysicalShardId).ToArray();
        if (groups.Length != PhysicalOwnerGroups || groups.Any(group => group.Count() != OwnerVoters))
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
        foreach (var group in groups)
        {
            var first = group.First();
            foreach (var next in group)
            {
                if (!PhysicalOwnerEntryValidation.SameOwner(first.Owner, next.Owner)
                    || !first.Owner.VoterIds.Contains(next.VoterId, StringComparer.Ordinal)
                    || next.AppliedPosition != first.AppliedPosition
                    || !Roster(first).SequenceEqual(Roster(next))
                    || !Bytes(first).SequenceEqual(Bytes(next)))
                { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
            }
        }
        return snapshots;
    }

    private static string[] Roster(OwnerCapacitySnapshotV1 value)
        => value.CompleteRoster.Select(item => JsonData.Fingerprint(item.Partition)).Order(StringComparer.Ordinal).ToArray();

    private static string[] Bytes(OwnerCapacitySnapshotV1 value)
        => value.Partitions.Select(item => JsonData.Fingerprint(item)).Order(StringComparer.Ordinal).ToArray();
}
