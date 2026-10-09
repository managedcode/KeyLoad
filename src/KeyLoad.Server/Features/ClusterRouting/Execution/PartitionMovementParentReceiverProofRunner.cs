using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Joins real source, first-request and receiver proof ACKs before one newly issued parent effect.</summary>
internal sealed class PartitionMovementParentReceiverProofRunner(PartitionMovementReceiver receiver,
    PartitionMovementClient client, PartitionMovementParentCheckpointOwner checkpoints,
    TimeProvider clock, IOptions<GrainRoutingOptions> routing)
{
    private enum ProofStep { Source, Packet, Receiver }

    internal async Task<PartitionMoveParentState> IssueFirstAsync(string principalId, PartitionMoveParentState state,
        ReadExecutionBudget work, CancellationToken cancellationToken)
        => await IssueFirstAsync(principalId, state, work, null, cancellationToken).ConfigureAwait(false);

    internal async Task<PartitionMoveParentState> IssueFirstAsync(string principalId, PartitionMoveParentState state,
        ReadExecutionBudget work, Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation,
        CancellationToken cancellationToken)
    {
        var pending = RequirePending(state);
        if (pending.OriginalGrant is not { RequireReceiverIssuance: true }
            || pending.OriginalReceiverSourceWitness is not null || pending.ReceiverSourceCheckpointReceipt is not null
            || pending.OriginalReceiverIssuePacket is not null || pending.ReceiverIssuePacketCheckpointReceipt is not null
            || pending.OriginalReceiverIssuanceWitness is not null || pending.ReceiverIssuanceCheckpointReceipt is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        var request = state.Header!.OriginalTransferRequest;
        var source = await receiver.ReadReceiverSourcePendingAsync(principalId, request, pending.OriginalPhaseCommandId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
        var sourceBody = ProofBody(principalId, state) with { OriginalReceiverSourceWitness = source };
        var sourceState = await JoinProofAsync(state, sourceBody, ProofStep.Source, work, cancellationToken).ConfigureAwait(false);
        var packet = await receiver.CreateReceiverIssuePacketAsync(principalId, request, pending.OriginalPhaseCommandId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
        var packetBody = ProofBody(principalId, sourceState) with { OriginalReceiverIssuePacket = packet };
        var packetState = await JoinProofAsync(sourceState, packetBody, ProofStep.Packet, work, cancellationToken).ConfigureAwait(false);
        return await JoinFirstReceiverAsync(principalId, packetState, work, phaseObservation,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PartitionMoveParentState> JoinFirstReceiverAsync(string principalId,
        PartitionMoveParentState packetState, ReadExecutionBudget work,
        Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        PartitionMoveParentState? joined = null;
        var acknowledged = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await client.IssueReceiverFirstAsync(RequirePending(packetState), work, cancellationToken).ConfigureAwait(false);
            acknowledged = true;
        }, failures).ConfigureAwait(false);
        if (acknowledged)
        {
            await ServerFailureObserver.ObserveAsync(() => ObserveIssuePhaseAsync(packetState,
                GrainRequestPhase.ParentReceiverIssueAcknowledged, phaseObservation, cancellationToken), failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            joined = await ObserveExistingAsync(principalId, packetState, work, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        if (joined is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => ObserveIssuePhaseAsync(joined,
                GrainRequestPhase.ParentReceiverIssueObserved, phaseObservation, cancellationToken), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        return joined ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
    }

    private static Task ObserveIssuePhaseAsync(PartitionMoveParentState state, GrainRequestPhase phase,
        Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken)
    {
        var pending = RequirePending(state);
        return phaseObservation is not null && pending.Stage == PartitionMovePeerStage.StagePage
            && pending.PageOrdinal == PartitionMovementProtocol.InitialPhaseOrdinal
            ? phaseObservation(phase, cancellationToken).AsTask() : Task.CompletedTask;
    }

    internal async Task<PartitionMoveParentState> ObserveExistingAsync(string principalId, PartitionMoveParentState state,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var pending = RequirePending(state);
        if (pending.OriginalReceiverSourceWitness is null || pending.ReceiverSourceCheckpointReceipt is null
            || pending.OriginalReceiverIssuePacket is null || pending.ReceiverIssuePacketCheckpointReceipt is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        if (pending.OriginalReceiverIssuanceWitness is not null && pending.ReceiverIssuanceCheckpointReceipt is not null)
        { return state; }
        var proof = await client.QueryReceiverIssueAsync(pending,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
        var body = ProofBody(principalId, state) with { OriginalReceiverIssuanceWitness = proof };
        return await JoinProofAsync(state, body, ProofStep.Receiver, work, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PartitionMoveParentState> JoinProofAsync(PartitionMoveParentState before,
        PartitionMoveCheckpointBody body, ProofStep step, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var pending = RequirePending(before);
        var phase = pending.OriginalPhase!;
        var actual = await checkpoints.SubmitAsync(before, body, phase, work, cancellationToken).ConfigureAwait(false);
        var joined = await receiver.ReadParentStateAsync(body.OperatorPrincipalId, body.OriginalTransferRequest,
            pending.OriginalPhaseCommandId, PartitionMovementParentDeadline.Expiry(work, clock, routing),
            work, cancellationToken).ConfigureAwait(false);
        var retained = RequirePending(joined);
        var receipt = step switch
        {
            ProofStep.Source => retained.ReceiverSourceCheckpointReceipt,
            ProofStep.Packet => retained.ReceiverIssuePacketCheckpointReceipt,
            ProofStep.Receiver => retained.ReceiverIssuanceCheckpointReceipt,
            _ => null
        };
        if (retained.OriginalPhaseCommandId != pending.OriginalPhaseCommandId || receipt is null
            || !NativeSerialization.Serialize(receipt).AsSpan().SequenceEqual(NativeSerialization.Serialize(actual.Journal))
            || !SameProof(body, retained, step))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        work.Check();
        return joined;
    }

    private static bool SameProof(PartitionMoveCheckpointBody body, PartitionMoveParentPhase phase, ProofStep step)
        => step switch
        {
            ProofStep.Source => NativeSerialization.Serialize(body.OriginalReceiverSourceWitness).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(phase.OriginalReceiverSourceWitness)),
            ProofStep.Packet => NativeSerialization.Serialize(body.OriginalReceiverIssuePacket).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(phase.OriginalReceiverIssuePacket)),
            ProofStep.Receiver => NativeSerialization.Serialize(body.OriginalReceiverIssuanceWitness).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(phase.OriginalReceiverIssuanceWitness)),
            _ => false
        };

    private static PartitionMoveCheckpointBody ProofBody(string principalId, PartitionMoveParentState state)
    {
        var pending = RequirePending(state);
        return new(PartitionMoveProtocol.Version, PartitionMoveCheckpointAction.Observe, principalId,
            state.Header!.OriginalTransferRequest, state.Header.Generation, pending.OriginalPhaseCommandId,
            null, null, null, null, null, OriginalExpiresAt: pending.OriginalExpiresAt,
            OriginalRequestNonce: pending.OriginalRequestNonce, OriginalCaptureReleaseNonce: pending.OriginalCaptureReleaseNonce);
    }

    private static PartitionMoveParentPhase RequirePending(PartitionMoveParentState state)
    {
        if (state.Header is null || state.Header.TerminalResult is not null || state.Pending is not { } pending
            || pending.OriginalPhase is null || pending.OriginalResult is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        return pending;
    }
}
