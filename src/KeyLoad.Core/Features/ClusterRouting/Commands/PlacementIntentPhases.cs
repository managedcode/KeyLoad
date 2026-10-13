using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private OperationResult ExecutePlacementIntentPhase(IAtomicTransaction transaction, PrincipalRecord principal,
        ReplicatedOperation operation)
    {
        RequirePlacementAdministrator(principal);
        var request = Payload<PlacementIntentPhaseRequestV1>(operation);
        var original = PlacementIntentStorage.Read(transaction, request.TenantId, request.DatabaseId,
            request.IntentId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
        if (request.OriginalApplyRequest is null
            || request.OriginalApplyRequest.IntentId != original.IntentId
            || request.OriginalApplyRequest.TenantId != original.Plan.TenantId
            || request.OriginalApplyRequest.DatabaseId != original.Plan.DatabaseId
            || request.OriginalApplyRequest.ControlPartition != original.Plan.ControlPartition
            || original.OriginalApplyRequest is { } retainedApply && retainedApply != request.OriginalApplyRequest
            || original.OriginalApplyRequest is null && request.OriginalApplyRequest.ExpectedRevision != original.Revision
            || request.Claim is null || request.Claim.ExpiresAt <= operation.EvaluatedAt
            || request.ControlPartition != original.Plan.ControlPartition || request.Version != TenantPlacementPolicyProtocol.Version || request.ExpectedRevision != original.Revision
            || original.PrincipalId != principal.Id || original.PolicyEpoch != principal.PolicyEpoch || request.OriginalApplyCommandId == Guid.Empty
            || original.OriginalApplyCommandId is { } retained && retained != request.OriginalApplyCommandId)
        { throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale); }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(transaction)
            ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, original.ControlOwner)
            || original.ControlOwner.Incarnation != Store.Identity.Incarnation
            || !original.ControlOwner.VoterIds.Contains(request.Claim.VoterId, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
        RequirePlacementControlPartition(transaction, original.Plan.ControlPartition,
            original.Plan.TenantId, original.Plan.DatabaseId, catalog.DefaultShard);
        var next = request.TargetStatus == PlacementIntentStatus.Applying
            ? ClaimPlacementIntent(original, request, operation.EvaluatedAt)
            : SettlePlacementIntent(transaction, original, request);
        next = next with { Revision = checked(original.Revision + TenantPlacementPolicyProtocol.RevisionStep) };
        PlacementIntentValidation.Require(next, ErrorCode.Corruption);
        PlacementIntentStorage.Write(transaction, next, Limits.MaxBatchBytes);
        return Result(next);
    }

    private static PlacementIntentV1 ClaimPlacementIntent(PlacementIntentV1 original,
        PlacementIntentPhaseRequestV1 request, DateTimeOffset evaluatedAt)
    {
        if (original.Status == PlacementIntentStatus.Completed
            || request.OriginalMoveReceipt is not null || request.Claim.Nonce == Guid.Empty
            || request.Claim.ExpiresAt <= evaluatedAt
            || original.Status == PlacementIntentStatus.Applying && original.Claim is { } live
                && evaluatedAt < live.ExpiresAt && !request.PreviousClaimOwnerAbsent)
        { throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale); }
        return original with
        {
            Status = PlacementIntentStatus.Applying,
            OriginalApplyCommandId = original.OriginalApplyCommandId ?? request.OriginalApplyCommandId,
            OriginalApplyRequest = original.OriginalApplyRequest ?? request.OriginalApplyRequest,
            Claim = request.Claim
        };
    }

    private PlacementIntentV1 SettlePlacementIntent(IKeyValueView view, PlacementIntentV1 original,
        PlacementIntentPhaseRequestV1 request)
    {
        if (original.Status != PlacementIntentStatus.Applying || original.Claim != request.Claim
            || request.TargetStatus is not (PlacementIntentStatus.Paused or PlacementIntentStatus.Completed))
        { throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale); }
        if (request.OriginalMoveReceipt is null)
        {
            if (request.TargetStatus != PlacementIntentStatus.Paused)
            { throw Errors.Fail(ErrorCode.Validation, TenantPlacementPolicyProtocol.Invalid); }
            return original with { Status = PlacementIntentStatus.Paused };
        }
        return AcceptPlacementIntentReceipt(view, original, request);
    }
}
