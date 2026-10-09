using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementParentPhaseObserver(PartitionMovementReceiver receiver,
    PartitionMovementClient client, PartitionMovementParentCheckpointOwner checkpoints,
    TimeProvider clock, IOptions<GrainRoutingOptions> routing)
{
    private const int FirstReceiverVoter = 0;

    internal async Task<PartitionMovePhaseResult> ObserveAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        if (state.Pending?.Stage is PartitionMovePeerStage.Capture or PartitionMovePeerStage.ControlAuthorize)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return await ObserveCoreAsync(principalId, request, state, state.Pending, work, cancellationToken).ConfigureAwait(false);
    }

    internal Task<PartitionMovePhaseResult> ObserveAuthorizationAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        if (state.Pending is not { Stage: PartitionMovePeerStage.ControlAuthorize, OriginalPhase: not null })
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return ObserveCoreAsync(principalId, request, state, state.Pending, work, cancellationToken);
    }

    internal Task<PartitionMovePhaseResult> ObserveCaptureAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        if (state.Pending is not
            {
                Stage: PartitionMovePeerStage.Capture,
                CaptureProofCheckpointReceipt: not null, OriginalCaptureWitness: not null,
                OriginalDescriptor: not null, OriginalFence: not null
            })
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return ObserveCoreAsync(principalId, request, state, state.Pending, work, cancellationToken);
    }

    internal Task<PartitionMovePhaseResult> ObserveInterruptedAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var original = state.Interrupted;
        if (request.Mode != PartitionMoveMode.Abort || original is null
            || state.Header?.InterruptedOriginalPhaseCommandId != original.OriginalPhaseCommandId
            || original.OriginalResult is not null || original.OriginalPhase is null
            || original.Stage == PartitionMovePeerStage.Capture && (original.CaptureProofCheckpointReceipt is null
                || original.OriginalCaptureWitness is null || original.OriginalDescriptor is null || original.OriginalFence is null))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return ObserveCoreAsync(principalId, request, state, original, work, cancellationToken);
    }

    private async Task<PartitionMovePhaseResult> ObserveCoreAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, PartitionMoveParentPhase? selectedOriginal,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var original = selectedOriginal
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var header = state.Header
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (original.OriginalResult is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var envelope = PartitionMovementParentCheckpointOwner.OriginalEnvelope(original, release: false);
        var expiry = PartitionMovementParentDeadline.Expiry(work, clock, routing);
        OperationResult actual;
        PartitionMoveAuthenticatedOutcomeWitness? proof = null;
        if (PartitionMoveGrantValidation.IsLocalControl(original.Stage))
        {
            actual = await receiver.ReadParentOriginalOutcomeAsync(principalId, request, original,
                expiry, work, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var witness = await client.QueryOutcomeAsync(original.OriginalPhaseCommandId, envelope,
                original.OriginalAuthorization, original.OriginalReceiverOwner.VoterIds[FirstReceiverVoter], expiry,
                work, cancellationToken).ConfigureAwait(false);
            actual = witness.Result;
            proof = witness.TransportProof
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
        }
        work.Check();
        var body = new PartitionMoveCheckpointBody(PartitionMoveProtocol.Version,
            PartitionMoveCheckpointAction.Observe, principalId, request, header.Generation,
            original.OriginalPhaseCommandId, null, null, actual,
            original.Stage == PartitionMovePeerStage.Capture ? original.OriginalDescriptor : null,
            original.Stage == PartitionMovePeerStage.Capture ? original.OriginalFence : null,
            OriginalExpiresAt: original.OriginalExpiresAt, OriginalRequestNonce: original.OriginalRequestNonce,
            OriginalOutcomeWitness: proof, OriginalCaptureReleaseNonce: original.OriginalCaptureReleaseNonce,
            OriginalCaptureWitness: original.OriginalCaptureWitness);
        if (original.Stage == PartitionMovePeerStage.ControlAuthorize && actual.Error is null
            && header.InterruptedOriginalPhaseCommandId != original.OriginalPhaseCommandId)
        {
            var issued = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(original.OriginalPhase!.Body.Span);
            var native = actual.Get<PartitionMovePhaseResult>();
            var next = PartitionMovementParentNativePhases.AttachActualGrant(issued.Phase,
                issued.PhaseCommandId, issued.ReceiverOwner, issued.ExpiresAt, native);
            body = body with
            {
                NextOriginalPhaseCommandId = issued.PhaseCommandId,
                NextOriginalPhase = next,
                NextOriginalAuthorization = native.Journal,
                NextOriginalExpiresAt = issued.ExpiresAt,
                NextOriginalRequestNonce = Guid.NewGuid(),
                NextOriginalCaptureReleaseNonce =
                    next.Stage == PartitionMovePeerStage.Capture ? Guid.NewGuid() : Guid.Empty
            };
        }
        return await checkpoints.SubmitAsync(state, body, original.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority),
            work, cancellationToken).ConfigureAwait(false);
    }
}
