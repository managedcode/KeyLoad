using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementParentPromotionValidation
{
    internal static PartitionMoveParentPhase Require(PartitionMoveParentState state)
    {
        var pending = state.Pending;
        var observed = state.Selected;
        if (pending?.OriginalPhase is null || pending.OriginalResult is not null
            || PartitionMoveGrantValidation.IsLocalControl(pending.Stage)
            || observed is not
            {
                Stage: PartitionMovePeerStage.ControlAuthorize,
                OriginalResult: not null, ObservationCheckpointReceipt: not null
            }
            || observed.OriginalResult.Error is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var actual = observed.OriginalResult.Get<PartitionMovePhaseResult>();
        var grant = actual.Grant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (grant.PhaseCommandId != pending.OriginalPhaseCommandId || grant.ExpiresAt != pending.OriginalExpiresAt
            || actual.Journal.CommandId != observed.OriginalPhaseCommandId
            || !NativeSerialization.Serialize(grant).AsSpan().SequenceEqual(NativeSerialization.Serialize(pending.OriginalGrant))
            || !NativeSerialization.Serialize(actual.Journal).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(pending.OriginalAuthorization))
            || !NativeSerialization.Serialize(pending.AdmissionCheckpointReceipt).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(observed.ObservationCheckpointReceipt)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        return pending;
    }
}
