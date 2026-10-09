using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns one newly admitted effect and joins actual durable observation before returning.</summary>
internal sealed class PartitionMovementParentPhaseRunner(PartitionMovementReceiver receiver,
    PartitionMovementClient client, PartitionMovementParentCheckpointOwner checkpoints,
    PartitionMovementParentPhaseObserver observer, PartitionMovementParentReceiverProofRunner proofs,
    TimeProvider clock, IOptions<GrainRoutingOptions> routing)
{
    internal async Task<PartitionMoveParentState> AdmitAndExecuteAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState before, PartitionMovementParentPhaseRole role,
        PartitionMovePhaseCommand intended, PartitionMoveJournalReceipt? authorization,
        DateTimeOffset originalExpiry, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        if (before.Pending is not null && (request.Mode != PartitionMoveMode.Abort
                || intended.Stage != PartitionMovePeerStage.ControlBeginAbort || before.Interrupted is not null)
            || intended.Stage == PartitionMovePeerStage.Capture)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var cleanupGeneration = role is PartitionMovementParentPhaseRole.RetireGrant
            or PartitionMovementParentPhaseRole.Retire or PartitionMovementParentPhaseRole.RetireAcknowledge
            ? before.Header?.CleanupGeneration
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority)
            : PartitionMoveProtocol.EmptyCount;
        var phaseId = PartitionMovementParentPhaseIds.For(request, principalId, role, intended.PageOrdinal,
            cleanupGeneration);
        var body = PartitionMovementParentPhaseAdmission.Create(principalId, request, before,
            phaseId, intended, authorization, originalExpiry);
        var admitted = await checkpoints.SubmitAsync(before, body, intended, work, cancellationToken).ConfigureAwait(false);
        work.Check();
        var current = await receiver.ReadParentStateAsync(principalId, request, phaseId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
        var original = RequireFirstAdmission(current, phaseId, intended, admitted.Journal);
        return await ExecuteFirstAdmittedAsync(principalId, request, current, original, work,
            cancellationToken).ConfigureAwait(false);
    }

    internal Task<PartitionMoveParentState> ExecutePromotedAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var original = PartitionMovementParentPromotionValidation.Require(state);
        if (original.Stage == PartitionMovePeerStage.Capture)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return ExecuteFirstAdmittedAsync(principalId, request, state, original, work, cancellationToken);
    }

    private async Task<PartitionMoveParentState> ExecuteFirstAdmittedAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState current, PartitionMoveParentPhase original,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        cancellationToken.ThrowIfCancellationRequested();
        PartitionMoveReceiverSourceWitness? dispatch = null;
        if (!PartitionMoveGrantValidation.IsLocalControl(original.Stage))
        {
            current = await proofs.IssueFirstAsync(principalId, current, work, cancellationToken).ConfigureAwait(false);
            original = current.Pending
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof);
            dispatch = await receiver.ReadReceiverSourcePendingAsync(principalId, current.Header!.OriginalTransferRequest,
                original.OriginalPhaseCommandId, PartitionMovementParentDeadline.Expiry(work, clock, routing),
                work, cancellationToken).ConfigureAwait(false);
        }
        var phaseId = original.OriginalPhaseCommandId;
        var intended = original.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var reply = PartitionMoveGrantValidation.IsLocalControl(original.Stage)
                ? await receiver.ApplyParentPhaseAsync(phaseId,
                    PartitionMovementParentCheckpointOwner.OriginalEnvelope(original, release: false),
                    cancellationToken).ConfigureAwait(false)
                : (await client.DispatchParentOriginalAsync(original, dispatch!, work, cancellationToken).ConfigureAwait(false)).Reply;
            if (reply.Error is { } code)
            { throw Errors.Fail(code, reply.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        }, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (original.Stage == PartitionMovePeerStage.ControlAuthorize)
            { await observer.ObserveAuthorizationAsync(principalId, request, current, work, cancellationToken).ConfigureAwait(false); }
            else
            { await observer.ObserveAsync(principalId, request, current, work, cancellationToken).ConfigureAwait(false); }
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        work.Check();
        var settled = await receiver.ReadParentStateAsync(principalId, request, phaseId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
        var actual = settled.Selected
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (actual.OriginalPhaseCommandId != phaseId
            || actual.OriginalResult is null || actual.ObservationCheckpointReceipt is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        if (actual.OriginalResult.Error is { } error)
        { throw Errors.Fail(error, actual.OriginalResult.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
        if (original.Stage == PartitionMovePeerStage.ControlAuthorize)
        {
            var issued = NativeSerialization.Deserialize<PartitionMoveAuthorizeBody>(intended.Body.Span);
            if (settled.Pending is not { } promoted || promoted.OriginalPhaseCommandId != issued.PhaseCommandId
                || promoted.OriginalExpiresAt != issued.ExpiresAt || promoted.OriginalResult is not null)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        }
        else if (settled.Pending is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return settled;
    }

    internal static PartitionMoveParentPhase RequireFirstAdmission(PartitionMoveParentState current,
        Guid phaseId, PartitionMovePhaseCommand intended, PartitionMoveJournalReceipt actualAdmission)
    {
        var original = current.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (original.OriginalPhaseCommandId != phaseId || original.OriginalResult is not null
            || original.AdmissionCheckpointReceipt.CommandId != actualAdmission.CommandId
            || original.AdmissionCheckpointReceipt.AppliedPosition != actualAdmission.AppliedPosition
            || original.OriginalPhase is null
            || !NativeSerialization.Serialize(original.OriginalPhase).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(intended)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.InvalidProof); }
        return original;
    }
}
