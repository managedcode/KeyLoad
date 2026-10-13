using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int SingleDirectoryRevision = 1;
    private const long EmptyPartitionBytes = 0;
    private sealed record CapacitySelection(PartitionRef Partition, Guid Owner, long DirectoryRevision,
        long RowRevision, long Bytes);

    private static ImmutableArray<CapacitySelection> RequirePlacementPlanSelection(IKeyValueView view,
        TenantPlacementPlanRequest request, ImmutableArray<OwnerCapacitySnapshotV1> cohort,
        PhysicalShardRecord control, CancellationToken token)
    {
        if (request.Partitions.IsDefaultOrEmpty || request.Partitions.Length > TenantPlacementPolicyProtocol.MaximumMoves
            || request.Partitions.Distinct().Count() != request.Partitions.Length)
        { throw Errors.Fail(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid); }
        foreach (var partition in request.Partitions)
        {
            ValidatePartition(partition);
            if (partition.TenantId != request.TenantId || partition.DatabaseId != request.DatabaseId)
            { throw Errors.Fail(ErrorCode.PermissionDenied, TenantPlacementPolicyProtocol.ScopeMismatch); }
        }
        RequirePlacementControlPartition(view, request.ControlPartition, request.TenantId, request.DatabaseId, control);
        if (request.Partitions.Contains(request.ControlPartition))
        { throw Errors.Fail(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid); }
        var roster = cohort.SelectMany(item => item.CompleteRoster).Select(item => item.Partition)
            .Where(part => part.TenantId == request.TenantId && part.DatabaseId == request.DatabaseId
                && part != request.ControlPartition).Distinct().ToArray();
        if (roster.Length != request.Partitions.Length || roster.Except(request.Partitions).Any())
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
        var result = ImmutableArray.CreateBuilder<CapacitySelection>(request.Partitions.Length);
        foreach (var part in request.Partitions)
        {
            token.ThrowIfCancellationRequested();
            var current = ResolveRegisteredPlacement(view, part, control);
            var owner = cohort.FirstOrDefault(item => item.Owner.PhysicalShardId == current.PhysicalShardId
                && item.Owner.Incarnation == current.Incarnation)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch);
            var bytes = owner.Partitions.SingleOrDefault(item => item.Partition == part)
                ?? throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch);
            if (bytes.Bytes < EmptyPartitionBytes)
            { throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed); }
            result.Add(new(part, current.PhysicalShardId, current.DirectoryRevision, current.Revision, bytes.Bytes));
        }
        if (result.Select(item => item.DirectoryRevision).Distinct().Count() != SingleDirectoryRevision)
        { throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale); }
        return result.MoveToImmutable();
    }

    private static bool PlacementOwnerIsTenantExclusive(IKeyValueView view, PhysicalShardRecord owner,
        ImmutableArray<OwnerCapacitySnapshotV1> cohort, TenantPlacementPlanRequest request,
        PhysicalShardRecord control, CancellationToken token)
    {
        foreach (var part in cohort.SelectMany(item => item.CompleteRoster).Select(item => item.Partition).Distinct())
        {
            token.ThrowIfCancellationRequested();
            var current = ResolveRegisteredPlacement(view, part, control);
            if (current.PhysicalShardId == owner.PhysicalShardId
                && (part.TenantId != request.TenantId || part.DatabaseId != request.DatabaseId))
            { return false; }
        }
        return true;
    }
}
