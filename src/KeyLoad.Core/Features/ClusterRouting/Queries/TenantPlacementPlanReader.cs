using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal TenantPlacementPlanResultV1 CreateTenantPlacementPlan(string principalId,
        TenantPlacementPlanRequest request, ImmutableArray<OwnerCapacitySnapshotV1> verifiedCapacity,
        string currentCohort, DateTimeOffset originalExpiry, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant grant, CancellationToken token)
        => Store.Read<TenantPlacementPlanResultV1>(view =>
        {
            var charged = work.CreateView(view, grant);
            var now = EvaluationClock.GetUtcNow();
            var principal = Principal(charged, principalId, now);
            RequirePlacementAdministrator(principal);
            var policy = TenantPlacementPolicyStorage.ReadPolicy(charged, request.TenantId,
                request.DatabaseId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
            TenantPlacementPolicyValidation.Require(policy, ErrorCode.Corruption);
            if (!policy.Enabled)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, TenantPlacementPolicyProtocol.ExpiredPlan); }
            if (originalExpiry <= now)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ExpiredPlan); }
            var cohort = PlacementCapacityCohort.Require(verifiedCapacity, currentCohort, now);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(charged)
                ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
            PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            var plan = BuildPlacementPlan(charged, request, policy, cohort, catalog.DefaultShard,
                currentCohort, originalExpiry, token);
            var claims = new TenantPlacementPlanClaims(TenantPlacementPolicyProtocol.Version,
                TenantPlacementPolicyProtocol.PlanPurpose, Store.Identity.Incarnation,
                principal.Id, principal.PolicyEpoch, catalog.DefaultShard, plan,
                cohort.OrderBy(item => item.VoterId, StringComparer.Ordinal).Select(item => new PlacementCapacityWitnessV1(
                    item.RequestId, item.Nonce, item.Owner.PhysicalShardId, item.Owner.Incarnation,
                    item.VoterId, item.SiloAddress, item.Resource.Sequence, item.Resource.SampledAt,
                    item.Resource.ExpiresAt, item.StorePosition, item.AppliedPosition, JsonData.Fingerprint(item))).ToImmutableArray());
            work.Check();
            return new(plan, Sign(claims));
        });

    private static TenantPlacementPlanV1 BuildPlacementPlan(IKeyValueView view,
        TenantPlacementPlanRequest request, TenantPlacementPolicyV1 policy,
        ImmutableArray<OwnerCapacitySnapshotV1> cohort, PhysicalShardRecord control,
        string currentCohort, DateTimeOffset originalExpiry, CancellationToken token)
    {
        var selection = RequirePlacementPlanSelection(view, request, cohort, control, token);
        var total = selection.Sum(item => item.Bytes);
        var owners = cohort.GroupBy(item => item.Owner.PhysicalShardId).Select(group => group.First().Owner).ToArray();
        var dedicated = owners.Where(owner => owner.PhysicalShardId != control.PhysicalShardId
            && PlacementOwnerIsTenantExclusive(view, owner, cohort, request, control, token));
        var candidates = total >= policy.PromotionBytes ? dedicated
            : total <= policy.DemotionBytes ? owners.Where(owner => owner.PhysicalShardId == control.PhysicalShardId)
            : owners.Where(owner => selection.All(item => item.Owner == owner.PhysicalShardId));
        var target = Features.ClusterRouting.PlacementRendezvousChoice.Choose(policy,
            candidates.Where(owner => Features.ClusterRouting.PlacementCapacityEligibility.Eligible(policy,
                owner, cohort, selection.Where(item => item.Owner != owner.PhysicalShardId).Sum(item => item.Bytes)))
                .Select(owner => owner.PhysicalShardId));
        var revision = selection.First().DirectoryRevision;
        var moves = ImmutableArray.CreateBuilder<PartitionMoveRequest>();
        foreach (var part in selection.OrderBy(item => JsonData.Fingerprint(item.Partition), StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (part.Owner == target)
            { continue; }
            moves.Add(new(Guid.NewGuid(), part.Partition, target, checked(revision + moves.Count), PartitionMoveMode.Transfer));
        }
        return new(TenantPlacementPolicyProtocol.Version, Guid.NewGuid(), request.TenantId, request.DatabaseId,
            policy.Revision, revision, currentCohort, cohort.Min(item => item.Resource.SampledAt),
            new[] { originalExpiry, cohort.Min(item => item.Resource.ExpiresAt) }.Min(),
            moves.ToImmutable(), cohort.Select(item => JsonData.Fingerprint(item)).Order(StringComparer.Ordinal).ToImmutableArray(),
            request.ControlPartition, request.Partitions);
    }
}
