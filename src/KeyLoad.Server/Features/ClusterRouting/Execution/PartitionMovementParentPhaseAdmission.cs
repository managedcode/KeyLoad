using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentPhaseAdmission
{
    internal static PartitionMoveCheckpointBody Create(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, Guid originalPhaseId, PartitionMovePhaseCommand original,
        PartitionMoveJournalReceipt? authorization, DateTimeOffset expiresAt)
    {
        var nonce = PartitionMoveGrantValidation.IsLocalControl(original.Stage) ? Guid.Empty : Guid.NewGuid();
        var releaseNonce = original.Stage == PartitionMovePeerStage.Capture ? Guid.NewGuid() : Guid.Empty;
        return new(PartitionMoveProtocol.Version, PartitionMoveCheckpointAction.Admit, principalId,
            request,
            state.Header?.Generation ?? PartitionMoveProtocol.EmptyCount, originalPhaseId, original, authorization, null, null, null,
            OriginalExpiresAt: expiresAt, OriginalRequestNonce: nonce, OriginalCaptureReleaseNonce: releaseNonce,
            CleanupGeneration: RequireCleanupGeneration(state, original));
    }
    private static long RequireCleanupGeneration(PartitionMoveParentState state, PartitionMovePhaseCommand original)
    {
        var retire = original.Stage == PartitionMovePeerStage.Retire;
        if (original.Stage == PartitionMovePeerStage.ControlAuthorize)
        {
            retire = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(original.Body.Span).Phase.Stage
                == PartitionMovePeerStage.Retire;
        }
        else if (original.Stage == PartitionMovePeerStage.ControlAcknowledge)
        {
            var body = NativeSerialization.Deserialize<PartitionMoveAcknowledgeBody>(original.Body.Span);
            var actual = state.LastIssued
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            var grant = actual.OriginalGrant
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            if (grant.GrantId != body.GrantId || actual.OriginalResult?.Error is not null
                || actual.OriginalResult is null || actual.ObservationCheckpointReceipt is null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            retire = grant.Stage == PartitionMovePeerStage.Retire;
        }
        return retire ? state.Header?.CleanupGeneration
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority) : PartitionMoveProtocol.EmptyCount;
    }

}
