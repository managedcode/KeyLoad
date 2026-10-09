using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementParentRetirementContinuation
{
    private readonly PartitionMovementParentNativeStep steps;
    private readonly PartitionMovementParentStateReader states;
    private readonly PartitionMovementParentPageContinuation pages;

    internal PartitionMovementParentRetirementContinuation(PartitionMovementParentNativeStep steps, PartitionMovementParentStateReader states, PartitionMovementParentPageContinuation pages)
    {
        this.steps = steps;
        this.states = states;
        this.pages = pages;
    }

    internal async Task<PartitionMoveParentState> TransferPublicationNextAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, PartitionMovementParentPhaseRole role,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        switch (role)
        {
            case PartitionMovementParentPhaseRole.AdvanceInstalled:
                return await steps.LocalAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.FinalizePublication,
                    PartitionMovementParentNativePhases.FinalizePublication(state.Header!, PartitionMovementParentAuthority.RequireControl(state)),
                    work, cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.FinalizePublication:
                var finalized = state.LastIssued!;
                _ = PartitionMovementParentAuthority.RequireObserved(finalized, PartitionMovePeerStage.ControlFinalize);
                state = await pages.ReadOriginalCaptureAsync(principalId, request, state, work,
                    cancellationToken).ConfigureAwait(false);
                var publish = PartitionMovementParentNativePhases.Publish(state.Header!, PartitionMovementParentAuthority.RequireControl(state),
                    finalized, state.Selected!.OriginalDescriptor!.Resources);
                return await steps.EffectAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.PublishGrant, PartitionMovementParentPhaseRole.Publish,
                    publish, state.Header!.DestinationOwner, work, cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.Publish:
                return await steps.AcknowledgeAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.PublishAcknowledge, state.LastIssued!, work,
                    cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.PublishAcknowledge:
                return await BeginRetirementAsync(principalId, request, state, work,
                    cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.Retire:
                return await steps.AcknowledgeAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.RetireAcknowledge, state.LastIssued!, work,
                    cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.RetireAcknowledge:
                return await ContinueRetirementAsync(principalId, request, state, work,
                    cancellationToken).ConfigureAwait(false);
            default:
                throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        }
    }

    private async Task<PartitionMoveParentState> BeginRetirementAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        state = await states.ReadAcknowledgedEffectAsync(principalId, request, state,
            PartitionMovePeerStage.PublishWitness, work, cancellationToken).ConfigureAwait(false);
        var published = state.Selected!;
        var original = published.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var body = NativeSerialization.Deserialize<PartitionMovePublishBody>(original.Body.Span);
        var grantId = published.OriginalGrant?.GrantId
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var intended = PartitionMovementParentNativePhases.Cleanup(state.Header!, PartitionMovementParentAuthority.RequireControl(state),
            PartitionMovePeerStage.Retire, PartitionMoveCleanupRole.Source, PartitionMoveProtocol.EmptyCount,
            PartitionMoveProtocol.EmptyCount, grantId, body.Publication);
        return await steps.EffectAsync(principalId, request, state, PartitionMovementParentPhaseRole.RetireGrant,
            PartitionMovementParentPhaseRole.Retire, intended, state.Header!.OriginalSourceOwner!, work,
            cancellationToken).ConfigureAwait(false);
    }

    internal Task<PartitionMoveParentState> RestartCancelledRetireAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        var header = PartitionMovementParentAuthority.RequireHeader(principalId, request, state);
        var last = state.LastIssued
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var original = last.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var cancellation = last.RetireCancellation?.Cancellation
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(original.Body.Span);
        if (state.Pending is not null || last.Stage != PartitionMovePeerStage.Retire
            || last.OriginalResult is not null || state.Control?.Phase != PartitionMovePhase.Published
            || cancellation.OriginalPhaseCommandId != last.OriginalPhaseCommandId
            || cancellation.CleanupGeneration != last.CleanupGeneration
            || header.CleanupGeneration != checked(last.CleanupGeneration + PartitionMoveProtocol.SequenceStep)
            || cancellation.FamilyOrdinal != body.FamilyOrdinal || cancellation.BatchOrdinal != original.PageOrdinal
            || cancellation.OriginalRequestNonce != last.OriginalRequestNonce
            || cancellation.OriginalExpiresAt != last.OriginalExpiresAt)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        // A fresh generation admits a distinct native grant at the exact cancelled cursor.
        // The retained original command, nonce, expiry and cancellation witness remain unchanged.
        var intended = PartitionMovementParentNativePhases.Cleanup(header, PartitionMovementParentAuthority.RequireControl(state),
            PartitionMovePeerStage.Retire, PartitionMoveCleanupRole.Source, body.FamilyOrdinal,
            original.PageOrdinal, body.PrecedingGrantId, body.Publication);
        return steps.EffectAsync(principalId, request, state, PartitionMovementParentPhaseRole.RetireGrant,
            PartitionMovementParentPhaseRole.Retire, intended, header.OriginalSourceOwner!, work, cancellationToken);
    }

    private async Task<PartitionMoveParentState> ContinueRetirementAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        state = await states.ReadAcknowledgedEffectAsync(principalId, request, state,
            PartitionMovePeerStage.Retire, work, cancellationToken).ConfigureAwait(false);
        var original = state.Selected!;
        var cleanup = PartitionMovementParentAuthority.RequireObserved(original, PartitionMovePeerStage.Retire).Cleanup
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (cleanup.Completion is not null)
        {
            return await steps.LocalAsync(principalId, request, state,
                PartitionMovementParentPhaseRole.CompleteRetirement,
                PartitionMovementParentNativePhases.Complete(state.Header!, PartitionMovementParentAuthority.RequireControl(state), original,
                    null, PartitionMovePeerStage.ControlCompleteRetirement), work,
                cancellationToken).ConfigureAwait(false);
        }
        var phase = original.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var body = NativeSerialization.Deserialize<PartitionMoveCleanupBody>(phase.Body.Span);
        var intended = PartitionMovementParentNativePhases.Cleanup(state.Header!, PartitionMovementParentAuthority.RequireControl(state),
            PartitionMovePeerStage.Retire, PartitionMoveCleanupRole.Source, cleanup.NextFamily,
            cleanup.NextBatch, body.PrecedingGrantId, body.Publication);
        return await steps.EffectAsync(principalId, request, state, PartitionMovementParentPhaseRole.RetireGrant,
            PartitionMovementParentPhaseRole.Retire, intended, state.Header!.OriginalSourceOwner!, work,
            cancellationToken).ConfigureAwait(false);
    }
}
