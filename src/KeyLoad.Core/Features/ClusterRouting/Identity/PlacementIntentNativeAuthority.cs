using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateVerifiedPlacementIntentPhase(Guid commandId, string principalId,
        PlacementIntentPhaseRequestV1 phase, ReadExecutionBudget work)
    {
        work.Check();
        if (commandId == Guid.Empty || phase.Claim is null || phase.Claim.Nonce == Guid.Empty
            || phase.Claim.ExpiresAt <= EvaluationClock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.OwnershipLost, TenantPlacementPolicyProtocol.ScopeMismatch); }
        var payload = NativeSerialization.Serialize(phase);
        RequireNativeBudget(payload.Length);
        Store.Read(view =>
        {
            var charged = work.CreateView(view);
            var principal = Principal(charged, principalId, EvaluationClock.GetUtcNow());
            RequirePlacementAdministrator(principal);
            var intent = PlacementIntentStorage.Read(charged, phase.TenantId, phase.DatabaseId,
                phase.IntentId, Limits.MaxBatchBytes)
                ?? throw Errors.Fail(ErrorCode.NotFound, TenantPlacementPolicyProtocol.Missing);
            if (intent.PrincipalId != principal.Id || intent.PolicyEpoch != principal.PolicyEpoch
                || intent.Revision != phase.ExpectedRevision
                || !intent.ControlOwner.VoterIds.Contains(phase.Claim.VoterId, StringComparer.Ordinal))
            { throw Errors.Fail(ErrorCode.Conflict, TenantPlacementPolicyProtocol.Stale); }
            return true;
        });
        var identity = System.Text.Json.JsonSerializer.Serialize(phase, JsonDefaults.Options);
        return IssueNativeOperation(new(commandId, OperationKind.PlacementIntentPhase,
            principalId, EvaluationClock.GetUtcNow(), identity), new NativeCommandPayload(payload));
    }
}
