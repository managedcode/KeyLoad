using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementParentPageContinuation
{
    private readonly PartitionMovementParentNativeStep steps;
    private readonly PartitionMovementParentStateReader states;
    private readonly PartitionMovementParentTransferPageReader pageReader;

    internal PartitionMovementParentPageContinuation(PartitionMovementParentNativeStep steps, PartitionMovementParentStateReader states, PartitionMovementParentTransferPageReader pageReader)
    {
        this.steps = steps;
        this.states = states;
        this.pageReader = pageReader;
    }

    internal async Task<PartitionMoveParentState> TransferPagesNextAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, PartitionMovementParentPhaseRole role,
        ReadExecutionBudget work, Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken)
    {
        if (role == PartitionMovementParentPhaseRole.StagePage)
        {
            return await steps.AcknowledgeAsync(principalId, request, state,
                PartitionMovementParentPhaseRole.StageAcknowledge, state.LastIssued!, work,
                cancellationToken).ConfigureAwait(false);
        }
        if (role == PartitionMovementParentPhaseRole.Install)
        {
            return await steps.AcknowledgeAsync(principalId, request, state,
                PartitionMovementParentPhaseRole.InstallAcknowledge, state.LastIssued!, work,
                cancellationToken).ConfigureAwait(false);
        }
        var ordinal = PartitionMoveProtocol.EmptyCount;
        PartitionMoveParentPhase? installed = null;
        if (role == PartitionMovementParentPhaseRole.StageAcknowledge)
        {
            state = await states.ReadAcknowledgedEffectAsync(principalId, request, state,
                PartitionMovePeerStage.StagePage, work, cancellationToken).ConfigureAwait(false);
            ordinal = checked(state.Selected!.PageOrdinal + PartitionMoveProtocol.SequenceStep);
        }
        else if (role == PartitionMovementParentPhaseRole.InstallAcknowledge)
        {
            state = await states.ReadAcknowledgedEffectAsync(principalId, request, state,
                PartitionMovePeerStage.Install, work, cancellationToken).ConfigureAwait(false);
            installed = state.Selected!;
            ordinal = checked(installed.PageOrdinal + PartitionMoveProtocol.SequenceStep);
        }
        else if (role != PartitionMovementParentPhaseRole.AdvanceCaptured)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        state = await ReadOriginalCaptureAsync(principalId, request, state, work,
            cancellationToken).ConfigureAwait(false);
        var captured = state.Selected!;
        var descriptor = captured.OriginalDescriptor!;
        if (installed is not null)
        {
            if (PartitionMovementParentAuthority.RequireObserved(installed, PartitionMovePeerStage.Install).InstalledReceipt is not null)
            {
                return await steps.LocalAsync(principalId, request, state,
                    PartitionMovementParentPhaseRole.AdvanceInstalled,
                    PartitionMovementParentNativePhases.Advance(state.Header!, PartitionMovementParentAuthority.RequireControl(state), captured, installed),
                    work, cancellationToken).ConfigureAwait(false);
            }
            return await InstallNextAsync(principalId, request, state, ordinal, work,
                phaseObservation, cancellationToken).ConfigureAwait(false);
        }
        var total = descriptor.Families.Sum(static family => family.PageCount);
        if (ordinal < total)
        {
            return await StageNextAsync(principalId, request, state, ordinal, work,
                phaseObservation, cancellationToken).ConfigureAwait(false);
        }
        if (ordinal != total)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        return await InstallNextAsync(principalId, request, state, PartitionMoveProtocol.EmptyCount,
            work, phaseObservation, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<PartitionMoveParentState> ReadOriginalCaptureAsync(string principalId,
        PartitionMoveRequest request, PartitionMoveParentState state, ReadExecutionBudget work,
        CancellationToken cancellationToken)
    {
        if (PartitionMovementParentAuthority.RequireHeader(principalId, request, state).OriginalCapturePhaseCommandId is not { } originalId
            || originalId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var current = await states.ReadCurrentAsync(principalId, request, originalId, work,
            cancellationToken).ConfigureAwait(false);
        var captured = current.Selected;
        if (captured is null || captured.OriginalPhaseCommandId != originalId
            || captured.OriginalDescriptor is null || captured.OriginalFence is null
            || captured.OriginalCaptureWitness is null || captured.CaptureProofCheckpointReceipt is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        _ = PartitionMovementParentAuthority.RequireObserved(captured, PartitionMovePeerStage.Capture);
        return current;
    }

    private Task<PartitionMoveParentState> InstallNextAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, int ordinal, ReadExecutionBudget work,
        Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken)
        => steps.EffectAsync(principalId, request, state, PartitionMovementParentPhaseRole.InstallGrant,
            PartitionMovementParentPhaseRole.Install,
            PartitionMovementParentNativePhases.Install(state.Header!, PartitionMovementParentAuthority.RequireControl(state),
                state.Selected!.OriginalFence!, state.Selected.OriginalDescriptor!, ordinal),
            state.Header!.DestinationOwner, work, phaseObservation, cancellationToken);

    private async Task<PartitionMoveParentState> StageNextAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, int ordinal, ReadExecutionBudget work,
        Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken)
    {
        var page = await pageReader.ReadOriginalPageAsync(principalId, request, state, ordinal, work,
            phaseObservation, cancellationToken).ConfigureAwait(false);
        var intended = PartitionMovementParentNativePhases.StagePage(state.Header!, PartitionMovementParentAuthority.RequireControl(state),
            state.Selected!.OriginalFence!, state.Selected.OriginalDescriptor!, page);
        return await steps.EffectAsync(principalId, request, state, PartitionMovementParentPhaseRole.StageGrant,
            PartitionMovementParentPhaseRole.StagePage, intended, state.Header!.DestinationOwner, work,
            phaseObservation, cancellationToken).ConfigureAwait(false);
    }
}
