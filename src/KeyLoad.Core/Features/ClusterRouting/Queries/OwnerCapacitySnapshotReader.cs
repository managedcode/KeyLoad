using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MaximumCapacityQueryPartitions = TenantPlacementPolicyProtocol.MaximumMoves;
    private const int FirstCapacitySequence = 1;
    private const long NoCapacityAppliedPosition = 0;

    internal OwnerCapacitySnapshotV1 ReadOwnerCapacity(string principalId, OwnerCapacityReadRequestV1 request,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant grant, CancellationToken token)
        => Store.Read<OwnerCapacitySnapshotV1>(view =>
        {
            work.Check();
            var charged = work.CreateView(view, grant);
            token.ThrowIfCancellationRequested();
            var now = EvaluationClock.GetUtcNow();
            var principal = Principal(charged, principalId, now);
            RequirePlacementAdministrator(principal);
            if (principal.PolicyEpoch != request.OriginalPolicyEpoch)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ScopeMismatch); }
            RequireCapacityReadRequest(request, now);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(charged)
                ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, request.Owner)
                || request.Owner.Incarnation != Store.Identity.Incarnation)
            { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
            var parts = ImmutableArray.CreateBuilder<PartitionCapacityBytesV1>(request.Partitions.Length);
            foreach (var partition in request.Partitions)
            { parts.Add(new(partition, TenantPlacementByteAccounting.Read(charged, partition, grant, token))); }
            token.ThrowIfCancellationRequested();
            RequireCapacityReadRequest(request, EvaluationClock.GetUtcNow());
            var appliedBytes = charged.ReadOwnedValue(KeySpace.AppliedBytes);
            var applied = appliedBytes is null ? NoCapacityAppliedPosition : NativeSerialization.Deserialize<long>(appliedBytes);
            if (applied < NoCapacityAppliedPosition)
            { throw Errors.Fail(ErrorCode.Corruption, TenantPlacementPolicyProtocol.Malformed); }
            var roster = ClusterBackupRosterCapture.Read(charged, Store.Identity.Incarnation, Store.Position, applied, Limits);
            work.Check();
            return new(TenantPlacementPolicyProtocol.Version, request.RequestId, request.Nonce, catalog.DefaultShard,
                request.VoterId, request.SiloAddress, request.CohortFingerprint, request.Resource,
                Store.Position, applied, parts.MoveToImmutable(), grant.ReadBytes, grant.ExaminedRecords, roster.Values.Select(item => item.Entry).ToImmutableArray());
        });

    private static void RequireCapacityReadRequest(OwnerCapacityReadRequestV1 request, DateTimeOffset now)
    {
        if (request.Version != TenantPlacementPolicyProtocol.Version || request.RequestId == Guid.Empty
            || request.Nonce == Guid.Empty || request.Partitions.IsDefaultOrEmpty
            || request.Partitions.Length > MaximumCapacityQueryPartitions
            || request.Partitions.Distinct().Count() != request.Partitions.Length
            || request.Resource.Sequence < FirstCapacitySequence
            || request.Resource.SampledAt > now || request.Resource.ExpiresAt <= now
            || request.OriginalExpiry <= now
            || !request.Owner.VoterIds.Contains(request.VoterId, StringComparer.Ordinal)
            || string.IsNullOrWhiteSpace(request.SiloAddress) || string.IsNullOrWhiteSpace(request.CohortFingerprint))
        { throw Errors.Fail(ErrorCode.Unauthenticated, TenantPlacementPolicyProtocol.ScopeMismatch); }
    }
}
