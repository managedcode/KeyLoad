using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>One first signed attempt; recovery observes only genuine cancellation, retaining quota on uncertainty.</summary>
internal sealed class PartitionMovementParentExpiredRetireCancellation(PartitionMovementReceiver receiver,
    PartitionMovementClient client, PartitionMovementParentCheckpointOwner checkpoints,
    TimeProvider clock, IOptions<GrainRoutingOptions> routing)
{
    internal async Task<PartitionMoveParentState> ExecuteAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var original = RequireExpiredRetire(state, principalId);
        var cancellationId = PartitionMovementParentPhaseIds.For(request, principalId,
            PartitionMovementParentPhaseRole.RetireCancellation, original.PageOrdinal, original.CleanupGeneration);
        var failures = new List<Exception>();
        if (original.RetireCancellationAttempt is null)
        {
            var expiry = PartitionMovementParentDeadline.Expiry(work, clock, routing);
            var fresh = await receiver.ReadReceiverSourcePendingAsync(principalId, request,
                original.OriginalPhaseCommandId, expiry, work, cancellationToken).ConfigureAwait(false);
            var first = client.CreateRetireCancellationAttempt(original, cancellationId, fresh, expiry, work);
            var body = Body(principalId, request, state, original, PartitionMoveCheckpointAction.AdmitRetireCancellation,
                first, null);
            await checkpoints.SubmitAsync(state, body, original.OriginalPhase!, work, cancellationToken).ConfigureAwait(false);
            state = await ReadAsync(principalId, request, original.OriginalPhaseCommandId, work, cancellationToken).ConfigureAwait(false);
            original = RequireExpiredRetire(state, principalId);
            if (original.RetireCancellationAttempt is null
                || !NativeSerialization.Serialize(original.RetireCancellationAttempt).AsSpan().SequenceEqual(NativeSerialization.Serialize(first)))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            await ServerFailureObserver.ObserveAsync(() => client.SendRetireCancellationFirstAsync(original,
                first, work, cancellationToken), failures).ConfigureAwait(false);
        }
        PartitionMoveParentState? observed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var reply = await client.QueryRetireCancellationAsync(original, cancellationId,
                PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken).ConfigureAwait(false);
            var actual = GrainNativePayload.Read<GrainValue>(reply.Value.Reply.Payload).Value as PartitionMovementRetireCancellationOutcomeResult
                ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMovementProtocol.InvalidProof);
            if (actual.Snapshot.NativeCancellationResult?.Error is { } code)
            { throw Errors.Fail(code, actual.Snapshot.NativeCancellationResult.SafeDetail ?? PartitionMovementProtocol.Unavailable); }
            var cancelled = actual.Snapshot.Cancellation;
            if (cancelled is null || actual.Snapshot.NativeCancellationResult is null
                || cancelled.CancellationCommandId != cancellationId
                || cancelled.OriginalPhaseCommandId != original.OriginalPhaseCommandId)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            var witness = new PartitionMoveRetireCancellationWitness(cancelled, reply.OriginalBytes, reply.Signature);
            var body = Body(principalId, request, state, original, PartitionMoveCheckpointAction.ObserveRetireCancellation,
                null, witness);
            await checkpoints.SubmitAsync(state, body, original.OriginalPhase!, work, cancellationToken).ConfigureAwait(false);
            observed = await ReadAsync(principalId, request, original.OriginalPhaseCommandId, work, cancellationToken).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        if (observed?.Header is not { TerminalResult: null } header || observed.Pending is not null
            || observed.LastIssued is not { RetireCancellation: not null, OriginalResult: null } last
            || last.OriginalPhaseCommandId != original.OriginalPhaseCommandId
            || header.CleanupGeneration != checked(original.CleanupGeneration + PartitionMoveProtocol.SequenceStep)
            || observed.Control?.Phase != PartitionMovePhase.Published)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return observed;
    }

    private Task<PartitionMoveParentState> ReadAsync(string principalId, PartitionMoveRequest request,
        Guid originalId, ReadExecutionBudget work, CancellationToken cancellationToken)
        => receiver.ReadParentStateAsync(principalId, request, originalId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken);

    private PartitionMoveParentPhase RequireExpiredRetire(PartitionMoveParentState state, string principalId)
    {
        if (state.Header is not { TerminalResult: null } header || state.Control?.Phase != PartitionMovePhase.Published
            || state.CurrentOperatorPrincipalId != principalId || state.CurrentOperatorPolicyEpoch <= PartitionMoveProtocol.EmptyCount
            || state.Pending is not
            {
                Stage: PartitionMovePeerStage.Retire, OriginalPhase: not null,
                OriginalResult: null, RetireCancellation: null
            } original
            || original.OriginalExpiresAt > clock.GetUtcNow() || header.CleanupGeneration != original.CleanupGeneration)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return original;
    }

    private static PartitionMoveCheckpointBody Body(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, PartitionMoveParentPhase original, PartitionMoveCheckpointAction action,
        PartitionMoveRetireCancellationAttempt? attempt, PartitionMoveRetireCancellationWitness? witness)
        => new(PartitionMoveProtocol.Version, action, principalId, request, state.Header!.Generation,
            original.OriginalPhaseCommandId, null, null, null, null, null,
            OriginalExpiresAt: original.OriginalExpiresAt, OriginalRequestNonce: original.OriginalRequestNonce,
            CleanupGeneration: original.CleanupGeneration, RetireCancellation: witness, RetireCancellationAttempt: attempt);
}
