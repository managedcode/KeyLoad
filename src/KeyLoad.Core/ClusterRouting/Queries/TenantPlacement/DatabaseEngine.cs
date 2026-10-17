using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal PlacementIntentV1 ReadPlacementIntent(string principalId, string tenantId, string databaseId,
        Guid intentId, ReadExecutionBudget work)
        => Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            RequirePlacementAdministrator(principal);
            var value = PlacementIntentStorage.Read(charged, tenantId, databaseId, intentId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
            PlacementIntentValidation.Require(value, ErrorCode.Corruption);
            if (value.PrincipalId != principal.Id || value.PolicyEpoch != principal.PolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ScopeMismatch);
            }
            var catalog = PhysicalShardCatalogRecordSerialization.Read(charged)
                ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
            RequirePlacementControlPartition(charged, value.Plan.ControlPartition,
                value.Plan.TenantId, value.Plan.DatabaseId, catalog.DefaultShard);
            work.MeasureResult(value);
            work.Check();
            return value;
        });

    internal void ValidatePlacementIntentApply(string principalId, PlacementIntentV1 intent,
        System.Collections.Immutable.ImmutableArray<OwnerCapacitySnapshotV1> fresh, string cohort,
        ReadExecutionBudget work, CancellationToken token)
        => Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            RequirePlacementAdministrator(principal);
            if (intent.PrincipalId != principal.Id || intent.PolicyEpoch != principal.PolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ScopeMismatch);
            }
            var policy = TenantPlacementPolicyStorage.ReadPolicy(charged, intent.Plan.TenantId,
                intent.Plan.DatabaseId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
            if (!policy.Enabled || policy.Revision != intent.Plan.PolicyRevision)
            {
                throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale);
            }
            PlacementCapacityCohort.Require(fresh, cohort, EvaluationClock.GetUtcNow());
            PlacementCapacityEnrollment.Require(charged, intent.ControlOwner,
                System.Collections.Immutable.ImmutableArray.CreateRange(fresh.Select(item => item.Owner)));
            PlacementCapacityPartitionSet.Require(intent.Plan, fresh);
            var next = intent.Plan.Moves[intent.CompletedMoves];
            var current = ResolveRegisteredPlacement(charged, next.Partition, intent.ControlOwner);
            var header = PartitionMoveParentStorage.Header(charged, next.Partition, next.MoveId, Limits.MaxBatchBytes);
            if (header is not null)
            {
                PartitionMoveParentValidation.RequireReplayScope(header, principalId, next);
            }
            if (header is null && (current.DirectoryRevision != checked(intent.Plan.DirectoryRevision + intent.CompletedMoves)
                || current.Revision != next.ExpectedPlacementRevision))
            {
                throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale);
            }
            var destination = fresh.First(item => item.Owner.PhysicalShardId == next.DestinationPhysicalShardId).Owner;
            if (destination.PhysicalShardId != intent.ControlOwner.PhysicalShardId
                && !PlacementOwnerIsTenantExclusive(charged, destination, fresh,
                    new(intent.Plan.TenantId, intent.Plan.DatabaseId, intent.Plan.OriginalPartitions,
                        intent.Plan.ControlPartition), intent.ControlOwner, token))
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, TenantPlacementPolicyProtocol.ScopeMismatch);
            }
            var source = fresh.First(item => item.Owner.PhysicalShardId == current.PhysicalShardId);
            var bytes = source.Partitions.Single(item => item.Partition == next.Partition).Bytes;
            if (!Features.ClusterRouting.PlacementCapacityEligibility.Eligible(policy, destination, fresh, bytes))
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, TenantPlacementPolicyProtocol.ScopeMismatch);
            }
            work.Check();
            return true;
        });
}
