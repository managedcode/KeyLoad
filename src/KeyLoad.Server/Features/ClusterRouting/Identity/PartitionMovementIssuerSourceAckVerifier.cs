using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PartitionMovementIssuerSourceAckVerifier
{
    internal static void Require(DatabaseEngine database, string principalId, PartitionMoveParentState state,
        PartitionMoveParentPhase pending, ReadExecutionBudget work)
    {
        var first = pending.OriginalReceiverSourceWitness
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var receipt = pending.ReceiverSourceCheckpointReceipt
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var raw = NativeSerialization.Deserialize<PartitionMovementSourcePendingReply>(first.OriginalReplyBytes.Span);
        var initial = GrainNativePayload.Read<GrainValue>(raw.Reply.Payload).Value as PartitionMovementParentStateResult
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        var originalHeader = initial.State.Header
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var header = state.Header
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var body = new PartitionMoveCheckpointBody(PartitionMoveProtocol.Version, PartitionMoveCheckpointAction.Observe,
            principalId, header.OriginalTransferRequest, originalHeader.Generation, pending.OriginalPhaseCommandId,
            null, null, null, null, null, OriginalExpiresAt: pending.OriginalExpiresAt,
            OriginalRequestNonce: pending.OriginalRequestNonce, OriginalCaptureReleaseNonce: pending.OriginalCaptureReleaseNonce,
            OriginalReceiverSourceWitness: first);
        var original = pending.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var envelope = new PartitionMovePeerEnvelope(PartitionMoveProtocol.Version, header.MoveId, header.Partition,
            header.ControlOwner, header.SourcePlacement, header.DestinationOwner, original.ControlIntentDigest,
            PartitionMovePeerStage.ControlCheckpoint, PartitionMovementProtocol.InitialPhaseOrdinal, pending.OriginalExpiresAt, Guid.NewGuid(), NativeSerialization.Serialize(body));
        var leaf = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        var outcome = database.ResolveVerifiedPartitionMovementOutcome(principalId, envelope,
            receipt.CommandId, work, leaf);
        work.CompleteReadGrant(leaf);
        if (outcome.Error is not null || state.CurrentReadCut < receipt.AppliedPosition
            || !NativeSerialization.Serialize(outcome.Get<PartitionMovePhaseResult>().Journal).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(receipt)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
    }
}
