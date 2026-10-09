using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementParentStateReader
{
    private readonly PartitionMovementReceiver receiver;
    private readonly TimeProvider clock;
    private readonly IOptions<GrainRoutingOptions> routing;
    private readonly PartitionMovementParentPhaseObserver observer;
    private readonly PartitionMovementParentReceiverProofRunner proofs;
    private readonly PartitionMovementParentExpiredRetireCancellation expiredRetireCancellation;

    internal PartitionMovementParentStateReader(PartitionMovementReceiver receiver, TimeProvider clock, IOptions<GrainRoutingOptions> routing, PartitionMovementParentPhaseObserver observer, PartitionMovementParentReceiverProofRunner proofs, PartitionMovementParentExpiredRetireCancellation expiredRetireCancellation)
    {
        this.receiver = receiver;
        this.clock = clock;
        this.routing = routing;
        this.observer = observer;
        this.proofs = proofs;
        this.expiredRetireCancellation = expiredRetireCancellation;
    }

    internal Task<PartitionMoveParentState> ReadCurrentAsync(string principalId, PartitionMoveRequest request,
        Guid actualIssuedPhaseId, ReadExecutionBudget work, CancellationToken cancellationToken)
        => receiver.ReadParentStateAsync(principalId, request, actualIssuedPhaseId,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken);

    internal Task<PartitionMoveParentState> ReconcileTransferPendingAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var pending = state.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        return pending.Stage == PartitionMovePeerStage.Retire && pending.OriginalResult is null
            && pending.OriginalExpiresAt <= clock.GetUtcNow()
            ? expiredRetireCancellation.ExecuteAsync(principalId, request, state, work, cancellationToken)
            : ObservePendingAsync(principalId, request, state, work, cancellationToken);
    }

    internal async Task<PartitionMoveParentState> ObservePendingAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var pending = state.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var id = pending.OriginalPhaseCommandId;
        state = await ReadCurrentAsync(principalId, request, id, work, cancellationToken).ConfigureAwait(false);
        pending = state.Pending
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (pending.OriginalPhaseCommandId != id)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        if (!PartitionMoveGrantValidation.IsLocalControl(pending.Stage))
        {
            state = await proofs.ObserveExistingAsync(principalId, state, work, cancellationToken).ConfigureAwait(false);
        }
        if (pending.Stage == PartitionMovePeerStage.ControlAuthorize)
        { await observer.ObserveAuthorizationAsync(principalId, request, state, work, cancellationToken).ConfigureAwait(false); }
        else if (pending.Stage == PartitionMovePeerStage.Capture)
        { await observer.ObserveCaptureAsync(principalId, request, state, work, cancellationToken).ConfigureAwait(false); }
        else
        { await observer.ObserveAsync(principalId, request, state, work, cancellationToken).ConfigureAwait(false); }
        // Only observation is permitted here. A resumed unknown effect never becomes a first dispatch.
        return await ReadCurrentAsync(principalId, request, id, work, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<PartitionMoveParentState> ReadAcknowledgedEffectAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, PartitionMovePeerStage expected,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var acknowledged = state.LastIssued
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var grant = PartitionMovementParentAuthority.RequireObserved(acknowledged, PartitionMovePeerStage.ControlAcknowledge).Grant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (grant.Stage != expected || grant.Settlement is null || grant.AbortDisposition is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var actual = await ReadCurrentAsync(principalId, request, grant.PhaseCommandId, work,
            cancellationToken).ConfigureAwait(false);
        var original = actual.Selected
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var effect = PartitionMovementParentAuthority.RequireObserved(original, expected);
        if (original.OriginalPhaseCommandId != grant.PhaseCommandId
            || original.OriginalGrant?.GrantId != grant.GrantId
            || !NativeSerialization.Serialize(effect.Journal).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(grant.Settlement)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return actual;
    }

    internal async Task<PartitionMoveParentState> ReadActualGrantEffectAsync(string principalId,
        PartitionMoveRequest request, Guid actualGrantId, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        if (actualGrantId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var state = await ReadCurrentAsync(principalId, request, actualGrantId, work,
            cancellationToken).ConfigureAwait(false);
        var authorization = state.Selected
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var grant = PartitionMovementParentAuthority.RequireObserved(authorization, PartitionMovePeerStage.ControlAuthorize).Grant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (grant.GrantId != actualGrantId || grant.Settlement is null || grant.AbortDisposition is not null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        state = await ReadCurrentAsync(principalId, request, grant.PhaseCommandId, work,
            cancellationToken).ConfigureAwait(false);
        var actual = state.Selected
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (!NativeSerialization.Serialize(PartitionMovementParentAuthority.RequireObserved(actual, grant.Stage).Journal).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(grant.Settlement)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        return state;
    }
}
