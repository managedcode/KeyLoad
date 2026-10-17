using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementParentCaptureContinuation
{
    private readonly PartitionMovementParentNativeStep steps;
    private readonly PartitionMovementParentStateReader states;
    private readonly IOptions<DatabaseLimits> limits;

    internal PartitionMovementParentCaptureContinuation(PartitionMovementParentNativeStep steps, PartitionMovementParentStateReader states, IOptions<DatabaseLimits> limits)
    {
        this.steps = steps;
        this.states = states;
        this.limits = limits;
    }

    internal async Task<PartitionMoveParentState> TransferCaptureNextAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, PartitionMovementParentPhaseRole role,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var header = PartitionMovementParentAuthority.RequireHeader(principalId, request, state);
        var control = PartitionMovementParentAuthority.RequireControl(state);
        switch (role)
        {
            case PartitionMovementParentPhaseRole.Prepare:
                return await steps.EffectAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.FenceGrant, PartitionMovementParentPhaseRole.Fence,
                    PartitionMovementParentNativePhases.Fence(header, control), header.OriginalSourceOwner!,
                    work, cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.Fence:
                return await steps.AcknowledgeAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.FenceAcknowledge, state.LastIssued!, work,
                    cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.FenceAcknowledge:
                state = await states.ReadAcknowledgedEffectAsync(principalId, request, state,
                    PartitionMovePeerStage.Fence, work, cancellationToken).ConfigureAwait(false);
                var fence = state.Selected!;
                var actualFence = PartitionMovementParentAuthority.RequireObserved(fence, PartitionMovePeerStage.Fence).Fence
                    ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
                return await steps.LocalAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.AcceptFence,
                    PartitionMovementParentNativePhases.AcceptFence(header, control,
                        fence.OriginalGrant!.GrantId, actualFence), work, cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.AcceptFence:
                return await CaptureFirstAsync(principalId, request, state, work,
                    cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.Capture:
                return await steps.AcknowledgeAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.CaptureAcknowledge, state.LastIssued!, work,
                    cancellationToken).ConfigureAwait(false);
            case PartitionMovementParentPhaseRole.CaptureAcknowledge:
                state = await states.ReadAcknowledgedEffectAsync(principalId, request, state,
                    PartitionMovePeerStage.Capture, work, cancellationToken).ConfigureAwait(false);
                return await steps.LocalAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.AdvanceCaptured,
                    PartitionMovementParentNativePhases.Advance(header, control, state.Selected!, null),
                    work, cancellationToken).ConfigureAwait(false);
            default:
                throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        }
    }

    private Task<PartitionMoveParentState> CaptureFirstAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        var header = PartitionMovementParentAuthority.RequireHeader(principalId, request, state);
        var control = PartitionMovementParentAuthority.RequireControl(state);
        var accepted = state.LastIssued
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        _ = PartitionMovementParentAuthority.RequireObserved(accepted, PartitionMovePeerStage.ControlAcceptFence);
        var phase = accepted.OriginalPhase
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var original = NativeSerialization.Deserialize<PartitionMoveFenceAcceptBody>(phase.Body.Span);
        var capture = PartitionMovementParentCaptureLimits.Create(principalId, original.Fence, limits.Value, work);
        return steps.EffectAsync(principalId, request, state, PartitionMovementParentPhaseRole.CaptureGrant,
            PartitionMovementParentPhaseRole.Capture,
            PartitionMovementParentNativePhases.Capture(header, control, capture), header.OriginalSourceOwner!,
            work, cancellationToken);
    }
}
