using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementParentCaptureRunner(PartitionMovementReceiver receiver,
    PartitionMovementClient client, PartitionMovementParentCheckpointOwner checkpoints,
    PartitionMovementParentPhaseObserver observer, PartitionMovementParentReceiverProofRunner proofs,
    TimeProvider clock, IOptions<GrainRoutingOptions> routing)
{
    internal async Task<PartitionMoveParentState> AdmitAndCaptureAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState before, PartitionMovePhaseCommand intended,
        PartitionMoveJournalReceipt authorization, DateTimeOffset originalExpiry,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        if (before.Pending is not null || intended.Stage != PartitionMovePeerStage.Capture)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var phaseId = PartitionMovementParentPhaseIds.For(request, principalId,
            PartitionMovementParentPhaseRole.Capture, intended.PageOrdinal);
        var admission = PartitionMovementParentPhaseAdmission.Create(principalId, request, before,
            phaseId, intended, authorization, originalExpiry);
        var admitted = await checkpoints.SubmitAsync(before, admission, intended, work,
            cancellationToken).ConfigureAwait(false);
        var current = await receiver.ReadParentStateAsync(principalId, request, phaseId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
        _ = PartitionMovementParentPhaseRunner.RequireFirstAdmission(current, phaseId, intended, admitted.Journal);
        return await ExecuteFirstAdmittedAsync(principalId, request, current, work,
            cancellationToken).ConfigureAwait(false);
    }

    internal Task<PartitionMoveParentState> ExecutePromotedAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var original = PartitionMovementParentPromotionValidation.Require(state);
        if (original.Stage != PartitionMovePeerStage.Capture)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return ExecuteFirstAdmittedAsync(principalId, request, state, work, cancellationToken);
    }

    private async Task<PartitionMoveParentState> ExecuteFirstAdmittedAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState current,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        cancellationToken.ThrowIfCancellationRequested();
        current = await proofs.IssueFirstAsync(principalId, current, work, cancellationToken).ConfigureAwait(false);
        var original = current.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var dispatch = await receiver.ReadReceiverSourcePendingAsync(principalId, current.Header!.OriginalTransferRequest,
            original.OriginalPhaseCommandId, PartitionMovementParentDeadline.Expiry(work, clock, routing),
            work, cancellationToken).ConfigureAwait(false);
        var phaseId = original.OriginalPhaseCommandId;
        var intended = original.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var captured = await client.CaptureParentOriginalAsync(original, dispatch, work, cancellationToken).ConfigureAwait(false);
        if (captured.Reply.Error is { } code)
        { throw Errors.Fail(code, captured.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var handle = GrainNativePayload.Read<GrainValue>(captured.Reply.Payload).Value as PartitionMovementCaptureHandle
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
        var witness = captured.CaptureWitness
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        var captureRequest = NativeSerialization.Deserialize<PartitionMoveCaptureRequest>(intended.Body.Span);
        var proofBody = new PartitionMoveCheckpointBody(PartitionMoveProtocol.Version,
            PartitionMoveCheckpointAction.Observe, principalId, request, current.Header!.Generation,
            phaseId, null, null, null, handle.Descriptor, captureRequest.Fence,
            OriginalExpiresAt: original.OriginalExpiresAt, OriginalCaptureWitness: witness,
            OriginalRequestNonce: original.OriginalRequestNonce,
            OriginalCaptureReleaseNonce: original.OriginalCaptureReleaseNonce);
        var proofAck = await checkpoints.SubmitAsync(current, proofBody, intended, work,
            cancellationToken).ConfigureAwait(false);
        var proofState = await receiver.ReadParentStateAsync(principalId, request, phaseId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
        var proofPending = proofState.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (proofPending.CaptureProofCheckpointReceipt is not { } retainedAck
            || retainedAck.CommandId != proofAck.Journal.CommandId
            || retainedAck.AppliedPosition != proofAck.Journal.AppliedPosition
            || !NativeSerialization.Serialize(proofPending.OriginalDescriptor).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(handle.Descriptor))
            || !NativeSerialization.Serialize(proofPending.OriginalCaptureWitness).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(witness))
            || !NativeSerialization.Serialize(proofPending.OriginalFence).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(captureRequest.Fence)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        return await SettleFirstCaptureAsync(principalId, request, proofState, proofPending, handle,
            captured.VoterId, work, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PartitionMoveParentState> SettleFirstCaptureAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState proofState, PartitionMoveParentPhase proofPending,
        PartitionMovementCaptureHandle handle, string voterId, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var phaseId = proofPending.OriginalPhaseCommandId;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var releaseDispatch = await receiver.ReadReceiverSourcePendingAsync(principalId,
                proofState.Header!.OriginalTransferRequest, phaseId,
                PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
            var released = await client.ReleaseParentCaptureAsync(proofPending, handle.HandleId,
                voterId, releaseDispatch, work, cancellationToken).ConfigureAwait(false);
            if (released.Reply.Error is { } error)
            { throw Errors.Fail(error, released.Reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await observer.ObserveCaptureAsync(principalId, request, proofState, work,
                cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        var settled = await receiver.ReadParentStateAsync(principalId, request, phaseId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
        var actual = settled.Selected;
        if (settled.Pending is not null || actual?.OriginalResult is null
            || actual.ObservationCheckpointReceipt is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        if (actual.OriginalResult.Error is { } errorCode)
        { throw Errors.Fail(errorCode, actual.OriginalResult.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        return settled;
    }
}
