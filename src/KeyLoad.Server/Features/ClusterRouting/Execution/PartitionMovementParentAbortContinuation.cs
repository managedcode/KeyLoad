using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementParentAbortContinuation
{
    private readonly PartitionMovementParentNativeStep steps;
    private readonly PartitionMovementParentStateReader states;
    private readonly PartitionMovementParentUnpreparedCancellation unpreparedCancellation;

    internal PartitionMovementParentAbortContinuation(PartitionMovementParentNativeStep steps, PartitionMovementParentStateReader states, PartitionMovementParentUnpreparedCancellation unpreparedCancellation)
    {
        this.steps = steps;
        this.states = states;
        this.unpreparedCancellation = unpreparedCancellation;
    }

    internal async Task<PartitionMoveParentState> AbortNextAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        if (state.Control is null)
        {
            return await unpreparedCancellation.ExecuteAsync(principalId, request, state, work,
                cancellationToken).ConfigureAwait(false);
        }
        var control = PartitionMovementParentAuthority.RequireControl(state);
        if (control.Phase != PartitionMovePhase.Aborting)
        {
            return await steps.LocalAsync(principalId, request, state, PartitionMovementParentPhaseRole.BeginAbort,
                PartitionMovementParentNativePhases.BeginAbort(state.Header!, control), work,
                cancellationToken).ConfigureAwait(false);
        }
        if (state.Pending is not null)
        {
            return await states.ObservePendingAsync(principalId, request, state, work,
                cancellationToken).ConfigureAwait(false);
        }
        var last = state.LastIssued
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        _ = PartitionMovementParentAuthority.RequireObserved(last, last.Stage);
        var role = PartitionMovementParentPhaseRoles.RequireLastRole(state.Header!, last);
        return role switch
        {
            PartitionMovementParentPhaseRole.BeginAbort => await TargetAbortNextAsync(principalId, request,
                state, PartitionMoveProtocol.EmptyCount, PartitionMoveProtocol.EmptyCount, work,
                cancellationToken).ConfigureAwait(false),
            PartitionMovementParentPhaseRole.TargetAbort or PartitionMovementParentPhaseRole.SourceClosure
                or PartitionMovementParentPhaseRole.SourceAbort => await steps.AcknowledgeAsync(principalId,
                    request, state, role == PartitionMovementParentPhaseRole.TargetAbort
                        ? PartitionMovementParentPhaseRole.TargetAbortAcknowledge
                        : role == PartitionMovementParentPhaseRole.SourceClosure
                            ? PartitionMovementParentPhaseRole.SourceClosureAcknowledge
                            : PartitionMovementParentPhaseRole.SourceAbortAcknowledge,
                    last, work, cancellationToken).ConfigureAwait(false),
            PartitionMovementParentPhaseRole.TargetAbortAcknowledge => await ContinueTargetAbortAsync(
                principalId, request, state, work, cancellationToken).ConfigureAwait(false),
            PartitionMovementParentPhaseRole.SourceClosureAcknowledge => await CompleteSourceAbortAsync(
                principalId, request, state, work, cancellationToken).ConfigureAwait(false),
            PartitionMovementParentPhaseRole.SourceAbortAcknowledge => await BeginCancelGrantsAsync(
                principalId, request, state, work, cancellationToken).ConfigureAwait(false),
            PartitionMovementParentPhaseRole.CancelGrants => await ContinueCancelGrantsAsync(principalId,
                request, state, work, cancellationToken).ConfigureAwait(false),
            _ => throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority)
        };
    }

    private Task<PartitionMoveParentState> TargetAbortNextAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, int family, int batch, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var intended = PartitionMovementParentNativePhases.Cleanup(state.Header!, PartitionMovementParentAuthority.RequireControl(state),
            PartitionMovePeerStage.Abort, PartitionMoveCleanupRole.Target, family, batch, Guid.Empty, null);
        return steps.EffectAsync(principalId, request, state, PartitionMovementParentPhaseRole.TargetAbortGrant,
            PartitionMovementParentPhaseRole.TargetAbort, intended, state.Header!.DestinationOwner, work,
            cancellationToken);
    }

    private async Task<PartitionMoveParentState> ContinueTargetAbortAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        state = await states.ReadAcknowledgedEffectAsync(principalId, request, state, PartitionMovePeerStage.Abort,
            work, cancellationToken).ConfigureAwait(false);
        var original = state.Selected!;
        var cleanup = PartitionMovementParentAuthority.RequireObserved(original, PartitionMovePeerStage.Abort).Cleanup
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (cleanup.Role != PartitionMoveCleanupRole.Target)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        if (cleanup.Completion is null)
        {
            return await TargetAbortNextAsync(principalId, request, state, cleanup.NextFamily,
                cleanup.NextBatch, work, cancellationToken).ConfigureAwait(false);
        }
        var grant = original.OriginalGrant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var close = PartitionMovementParentNativePhases.Cleanup(state.Header!, PartitionMovementParentAuthority.RequireControl(state),
            PartitionMovePeerStage.SourceBeginAbort, PartitionMoveCleanupRole.Source,
            PartitionMoveCleanupFamilies.All.Length, PartitionMoveProtocol.EmptyCount, grant.GrantId, null);
        return await steps.EffectAsync(principalId, request, state, PartitionMovementParentPhaseRole.SourceClosureGrant,
            PartitionMovementParentPhaseRole.SourceClosure, close, state.Header!.OriginalSourceOwner!, work,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PartitionMoveParentState> CompleteSourceAbortAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        state = await states.ReadAcknowledgedEffectAsync(principalId, request, state, PartitionMovePeerStage.SourceBeginAbort,
            work, cancellationToken).ConfigureAwait(false);
        var original = state.Selected!.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(original.Body.Span);
        var terminal = PartitionMovementParentNativePhases.Cleanup(state.Header!, PartitionMovementParentAuthority.RequireControl(state),
            PartitionMovePeerStage.Abort, PartitionMoveCleanupRole.Source, PartitionMoveCleanupFamilies.All.Length,
            PartitionMoveProtocol.EmptyCount, body.PrecedingGrantId, null);
        return await steps.EffectAsync(principalId, request, state, PartitionMovementParentPhaseRole.SourceAbortGrant,
            PartitionMovementParentPhaseRole.SourceAbort, terminal, state.Header!.OriginalSourceOwner!, work,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<PartitionMoveParentState> BeginCancelGrantsAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        state = await states.ReadAcknowledgedEffectAsync(principalId, request, state, PartitionMovePeerStage.Abort,
            work, cancellationToken).ConfigureAwait(false);
        var source = state.Selected!;
        var original = source.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(original.Body.Span);
        state = await states.ReadActualGrantEffectAsync(principalId, request, body.PrecedingGrantId,
            work, cancellationToken).ConfigureAwait(false);
        var target = state.Selected!;
        var cancel = PartitionMovementParentNativePhases.Complete(state.Header!, PartitionMovementParentAuthority.RequireControl(state),
            source, target, PartitionMovePeerStage.ControlCancelGrants);
        return await steps.LocalAsync(principalId, request, state, PartitionMovementParentPhaseRole.CancelGrants,
            cancel, work, cancellationToken).ConfigureAwait(false);
    }

    private Task<PartitionMoveParentState> ContinueCancelGrantsAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var last = state.LastIssued!;
        var actual = PartitionMovementParentAuthority.RequireObserved(last, PartitionMovePeerStage.ControlCancelGrants).Cleanup
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var original = last.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var role = actual.Completion is null ? PartitionMovementParentPhaseRole.CancelGrants
            : PartitionMovementParentPhaseRole.FinalizeAbort;
        var intended = original with
        {
            Stage = actual.Completion is null ? PartitionMovePeerStage.ControlCancelGrants
                : PartitionMovePeerStage.ControlFinalizeAbort,
            PageOrdinal = actual.Completion is null ? actual.NextBatch : PartitionMoveProtocol.EmptyCount
        };
        return steps.LocalAsync(principalId, request, state, role, intended, work, cancellationToken);
    }
}
