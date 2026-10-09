using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private PartitionMoveParentHeader PromoteObservedMoveParentGrant(IAtomicTransaction transaction,
        PrincipalRecord principal, PartitionMoveCheckpointBody body, PartitionMoveParentPhase original,
        OperationResult actual, PartitionMoveParentHeader header, PartitionMoveJournalReceipt receipt)
    {
        var grant = RequireMoveParentGrantPromotion(principal, body, original, actual);
        var issued = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(original.OriginalPhase!.Body.Span);
        var expected = issued.Phase with { GrantId = grant.GrantId, Resources = grant.Resources };
        var next = new PartitionMoveParentPhase(PartitionMoveProtocol.Version, header.MoveId, header.Partition,
            issued.PhaseCommandId, expected.Stage, expected.PageOrdinal,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(expected.Body.Span)), expected,
            body.NextOriginalAuthorization, null, null, null, receipt, null, grant, grant.ReceiverOwner, issued.ExpiresAt,
            null, body.NextOriginalRequestNonce, null,
            PartitionMoveOriginalDispatchIdentity.Digest(issued.PhaseCommandId, expected, grant, issued.ExpiresAt),
            null, body.NextOriginalCaptureReleaseNonce, OriginalIssuancePolicyEpoch: grant.OperatorPolicyEpoch,
            CleanupGeneration: expected.Stage == PartitionMovePeerStage.Retire ? header.CleanupGeneration : PartitionMoveProtocol.EmptyCount);
        if (PartitionMoveParentStorage.Phase(transaction, header.Partition, header.MoveId, issued.PhaseCommandId,
            Limits.MaxBatchBytes) is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        // This is reconciliation of the already issued nested grant, even when its original permission has expired.
        // The dispatcher still rejects effect execution after issued.ExpiresAt; this path never renews it.
        return SaveMoveParentAdmission(transaction, header, header, next);
    }
    private static PartitionMovePhaseGrant RequireMoveParentGrantPromotion(PrincipalRecord principal,
        PartitionMoveCheckpointBody body, PartitionMoveParentPhase original, OperationResult actual)
    {
        if (original.Stage != PartitionMovePeerStage.ControlAuthorize || original.OriginalPhase is null || actual.Error is not null)
        { throw Errors.Fail(ErrorCode.Conflict, PartitionMoveProtocol.Conflict); }
        var issued = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(original.OriginalPhase.Body.Span);
        var grant = actual.Get<PartitionMovePhaseResult>().Grant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var expected = issued.Phase with { GrantId = grant.GrantId, Resources = grant.Resources };
        if (grant.OperatorPrincipalId != principal.Id || grant.OperatorPolicyEpoch != original.OriginalIssuancePolicyEpoch
            || body.NextOriginalPhaseCommandId != issued.PhaseCommandId || body.NextOriginalExpiresAt != issued.ExpiresAt
            || grant.PhaseCommandId != issued.PhaseCommandId || grant.ExpiresAt != issued.ExpiresAt
            || body.NextOriginalAuthorization is null
            || !NativeSerialization.Serialize(actual.Get<PartitionMovePhaseResult>().Journal).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(body.NextOriginalAuthorization))
            || !NativeSerialization.Serialize(expected).AsSpan().SequenceEqual(NativeSerialization.Serialize(body.NextOriginalPhase))
            || body.NextOriginalRequestNonce == Guid.Empty
            || (expected.Stage == PartitionMovePeerStage.Capture) != (body.NextOriginalCaptureReleaseNonce != Guid.Empty)
            || expected.Stage == PartitionMovePeerStage.Capture && body.NextOriginalRequestNonce == body.NextOriginalCaptureReleaseNonce)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
        return grant;
    }

}
