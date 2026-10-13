using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecutePersistPlacementIntent(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation)
    {
        RequirePlacementAdministrator(principal);
        var admission = Payload<PlacementIntentPersistenceAdmissionV1>(operation);
        var request = admission.OriginalRequest;
        if (request.IntentId == Guid.Empty || request.ExpectedRevision != TenantPlacementPolicyProtocol.EmptyRevision)
        { throw Errors.Fail(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid); }
        var claims = Verify<TenantPlacementPlanClaims>(request.SignedPlan, Limits.MaxBatchBytes);
        RequirePlacementPlanClaims(transaction, principal, claims, operation.EvaluatedAt);
        if (admission.OriginalCohortFingerprint != claims.Plan.CohortFingerprint)
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementCapacityProtocol.WrongCohort); }
        PlacementPersistenceCohort.Require(claims, admission.OriginalFreshCapacity, operation.EvaluatedAt);
        PlacementCapacityEnrollment.Require(transaction, claims.ControlOwner,
            System.Collections.Immutable.ImmutableArray.CreateRange(
                admission.OriginalFreshCapacity.Select(item => item.Owner)));
        if (request.ControlPartition != claims.Plan.ControlPartition || claims.Plan.Moves.IsEmpty)
        { throw Errors.Fail(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid); }
        var original = PlacementIntentStorage.Read(transaction, claims.Plan.TenantId, claims.Plan.DatabaseId,
            request.IntentId, Limits.MaxBatchBytes);
        if (original is not null)
        { throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale); }
        var next = new PlacementIntentV1(TenantPlacementPolicyProtocol.Version, request.IntentId,
            principal.Id, principal.PolicyEpoch, claims.Plan, TenantPlacementPolicyProtocol.RevisionStep,
            TenantPlacementPolicyProtocol.NoCompletedMoves, PlacementIntentStatus.Pending, [], OriginalApplyCommandId: null, Claim: null, ControlOwner: claims.ControlOwner, OriginalApplyRequest: null);
        PlacementIntentStorage.Write(transaction, next, Limits.MaxBatchBytes);
        return Result(next);
    }

    private void RequirePlacementPlanClaims(IKeyValueView view, PrincipalRecord principal,
        TenantPlacementPlanClaims claims, DateTimeOffset now)
    {
        TenantPlacementPolicyValidation.RequirePlan(claims.Plan, ErrorCode.Validation);
        PlacementCapacityWitnessValidation.Require(claims, now);
        if (claims.Version != TenantPlacementPolicyProtocol.Version
            || claims.Purpose != TenantPlacementPolicyProtocol.PlanPurpose
            || claims.PrincipalId != principal.Id || claims.PolicyEpoch != principal.PolicyEpoch
            || claims.Incarnation != Store.Identity.Incarnation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ScopeMismatch); }
        if (now < claims.Plan.SampledAt || now >= claims.Plan.ExpiresAt)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, TenantPlacementPolicyProtocol.ExpiredPlan); }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        RequirePlacementControlPartition(view, claims.Plan.ControlPartition, claims.Plan.TenantId,
            claims.Plan.DatabaseId, catalog.DefaultShard);
        if (!PhysicalOwnerEntryValidation.SameOwner(claims.ControlOwner, catalog.DefaultShard))
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
        var policy = TenantPlacementPolicyStorage.ReadPolicy(view, claims.Plan.TenantId,
            claims.Plan.DatabaseId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
        if (!policy.Enabled || policy.Revision != claims.Plan.PolicyRevision)
        { throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale); }
        foreach (var move in claims.Plan.Moves)
        {
            var current = ResolveRegisteredPlacement(view, move.Partition, catalog.DefaultShard);
            if (current.DirectoryRevision != claims.Plan.DirectoryRevision)
            { throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale); }
            _ = ResolveRegisteredPlacementOwner(view, catalog.DefaultShard,
                move.DestinationPhysicalShardId, ErrorCode.OwnershipLost);
        }
    }
}
