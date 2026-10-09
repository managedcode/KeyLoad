using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Joins a real first-Prepare cancellation barrier and separately observes its own stored outcome.</summary>
internal sealed class PartitionMovementParentUnpreparedCancellation(PartitionMovementReceiver receiver,
    PartitionMovementParentCheckpointOwner checkpoints, TimeProvider clock, IOptions<GrainRoutingOptions> routing)
{
    internal async Task<PartitionMoveParentState> ExecuteAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var first = RequireUnprepared(request, state);
        if (first.Cancellation is null)
        {
            var body = Body(principalId, request, state, first, PartitionMoveCheckpointAction.CancelUnprepared, null);
            await checkpoints.SubmitAsync(state, body, first.OriginalPhase!, work, cancellationToken).ConfigureAwait(false);
            state = await ReadAsync(principalId, request, first.OriginalPhaseCommandId, work,
                cancellationToken).ConfigureAwait(false);
            first = RequireUnprepared(request, state);
        }
        var cancellation = first.Cancellation
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var ownOutcome = state.CancellationOutcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (ownOutcome.Error is { } failure)
        { throw Errors.Fail(failure, ownOutcome.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        var actual = ownOutcome.Get<PartitionMovePhaseResult>();
        if (actual.Stage != PartitionMovePeerStage.ControlCheckpoint
            || state.CurrentReadCut < cancellation.CancellationReceipt.AppliedPosition
            || !NativeSerialization.Serialize(actual.Journal).AsSpan().SequenceEqual(NativeSerialization.Serialize(cancellation.CancellationReceipt)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var observed = Body(principalId, request, state, first, PartitionMoveCheckpointAction.ObserveCancellation, ownOutcome);
        await checkpoints.SubmitAsync(state, observed, first.OriginalPhase!, work, cancellationToken).ConfigureAwait(false);
        var terminal = await ReadAsync(principalId, request, first.OriginalPhaseCommandId, work,
            cancellationToken).ConfigureAwait(false);
        if (terminal.Header is not { TerminalResult.Phase: PartitionMovePhase.Aborted, TerminalObservationReceipt: not null }
            || terminal.Pending is not null || terminal.Interrupted?.OriginalPhaseCommandId != first.OriginalPhaseCommandId
            || terminal.Interrupted.OriginalResult is not null || terminal.Interrupted.Cancellation is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return terminal;
    }

    private Task<PartitionMoveParentState> ReadAsync(string principalId, PartitionMoveRequest request,
        Guid phaseId, ReadExecutionBudget work, CancellationToken cancellationToken)
        => receiver.ReadParentStateAsync(principalId, request, phaseId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken);

    private static PartitionMoveParentPhase RequireUnprepared(PartitionMoveRequest request, PartitionMoveParentState state)
    {
        if (request.Mode != PartitionMoveMode.Abort || state.Control is not null || state.Interrupted is not null
            || state.Header is not { RetainedPhaseCount: PartitionMoveProtocol.SequenceStep, TerminalResult: null }
            || state.Pending is not
            {
                Stage: PartitionMovePeerStage.ControlPrepare, OriginalPhase: not null,
                OriginalResult: null, OriginalGrant: null
            } original)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return original;
    }

    private static PartitionMoveCheckpointBody Body(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, PartitionMoveParentPhase first, PartitionMoveCheckpointAction action,
        OperationResult? actualCancellationOutcome)
        => new(PartitionMoveProtocol.Version, action, principalId, request, state.Header!.Generation,
            first.OriginalPhaseCommandId, first.OriginalPhase, null, actualCancellationOutcome, null, null,
            OriginalExpiresAt: first.OriginalExpiresAt, OriginalRequestNonce: first.OriginalRequestNonce,
            OriginalCaptureReleaseNonce: first.OriginalCaptureReleaseNonce);
}
