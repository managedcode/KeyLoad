using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Plans only from original issued native identities and joins each actual durable observation.</summary>
internal sealed class PartitionMovementParentOrchestrator
{
    private readonly PartitionMovementParentNativeStep steps;
    private readonly PartitionMovementParentStateReader states;
    private readonly PartitionMovementParentCaptureContinuation captures;
    private readonly PartitionMovementParentPageContinuation pages;
    private readonly PartitionMovementParentRetirementContinuation retirement;
    private readonly PartitionMovementParentAbortContinuation abort;

    internal PartitionMovementParentOrchestrator(PartitionMovementReceiver receiver,
        PartitionMovementClient client, TimeProvider clock, IOptions<GrainRoutingOptions> routing, IOptions<DatabaseLimits> limits, string actualClusterId)
    {
        var checkpoints = new PartitionMovementParentCheckpointOwner(receiver, clock, routing);
        var observer = new PartitionMovementParentPhaseObserver(receiver, client, checkpoints, clock, routing);
        var proofs = new PartitionMovementParentReceiverProofRunner(receiver, client, checkpoints, clock, routing);
        var phases = new PartitionMovementParentPhaseRunner(receiver, client, checkpoints,
            observer, proofs, clock, routing);
        var captureRunner = new PartitionMovementParentCaptureRunner(receiver, client, checkpoints,
            observer, proofs, clock, routing);
        steps = new(phases, captureRunner, clock, routing, limits, actualClusterId);
        var unpreparedCancellation = new PartitionMovementParentUnpreparedCancellation(receiver, checkpoints, clock, routing);
        var expiredRetireCancellation = new PartitionMovementParentExpiredRetireCancellation(receiver, client, checkpoints, clock, routing);
        states = new(receiver, clock, routing, observer, proofs, expiredRetireCancellation);
        captures = new(steps, states, limits);
        var pageReader = new PartitionMovementParentTransferPageReader(receiver, client, clock, routing);
        pages = new(steps, states, pageReader);
        retirement = new(steps, states, pages);
        abort = new(steps, states, unpreparedCancellation);
    }

    internal async Task<PartitionMoveResult> ExecuteAsync(string principalId, PartitionMoveRequest request,
        ReadExecutionBudget work, Func<PartitionMovePhase, ValueTask> progress,
        Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken)
    {
        work.Check();
        var state = await states.ReadCurrentAsync(principalId, request, Guid.Empty, work,
            cancellationToken).ConfigureAwait(false);
        if (state.Header is null)
        {
            if (request.Mode != PartitionMoveMode.Transfer)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
            var first = PartitionMovementParentNativePhases.Prepare(principalId, request, state);
            state = await steps.LocalAsync(principalId, request, state,
                PartitionMovementParentPhaseRole.Prepare, first, work, cancellationToken).ConfigureAwait(false);
        }
        while (true)
        {
            work.Check();
            cancellationToken.ThrowIfCancellationRequested();
            var header = PartitionMovementParentAuthority.RequireHeader(principalId, request, state);
            if (PartitionMovementParentAuthority.RequireTerminal(state) is { } terminal)
            { return terminal; }
            if (state.Control is { } actualControl)
            { await progress(actualControl.Phase).ConfigureAwait(false); }
            if (request.Mode == PartitionMoveMode.Abort)
            {
                state = await abort.AbortNextAsync(principalId, request, state, work,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }
            if (state.Pending is not null)
            {
                state = await states.ReconcileTransferPendingAsync(principalId, request, state, work,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }
            var last = state.LastIssued
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            if (last.RetireCancellation is not null)
            {
                state = await retirement.RestartCancelledRetireAsync(principalId, request, state, work,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }
            _ = PartitionMovementParentAuthority.RequireObserved(last, last.Stage);
            var role = PartitionMovementParentPhaseRoles.RequireLastRole(header, last);
            state = await TransferNextAsync(principalId, request, state, role, work,
                phaseObservation, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<PartitionMoveParentState> TransferNextAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, PartitionMovementParentPhaseRole role, ReadExecutionBudget work,
        Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken)
        => role switch
        {
            PartitionMovementParentPhaseRole.Prepare or PartitionMovementParentPhaseRole.Fence
                or PartitionMovementParentPhaseRole.FenceAcknowledge or PartitionMovementParentPhaseRole.AcceptFence
                or PartitionMovementParentPhaseRole.Capture or PartitionMovementParentPhaseRole.CaptureAcknowledge
                => captures.TransferCaptureNextAsync(principalId, request, state, role, work, cancellationToken),
            PartitionMovementParentPhaseRole.AdvanceCaptured or PartitionMovementParentPhaseRole.StagePage
                or PartitionMovementParentPhaseRole.StageAcknowledge or PartitionMovementParentPhaseRole.Install
                or PartitionMovementParentPhaseRole.InstallAcknowledge
                => pages.TransferPagesNextAsync(principalId, request, state, role, work, phaseObservation, cancellationToken),
            PartitionMovementParentPhaseRole.AdvanceInstalled or PartitionMovementParentPhaseRole.FinalizePublication
                or PartitionMovementParentPhaseRole.Publish or PartitionMovementParentPhaseRole.PublishAcknowledge
                or PartitionMovementParentPhaseRole.Retire or PartitionMovementParentPhaseRole.RetireAcknowledge
                => retirement.TransferPublicationNextAsync(principalId, request, state, role, work, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority)
        };
}
