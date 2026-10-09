using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Runs genuine newly issued native local phases and grant-promoted effects using one parent work owner.</summary>
internal sealed class PartitionMovementParentNativeStep(PartitionMovementParentPhaseRunner phases,
    PartitionMovementParentCaptureRunner captures, TimeProvider clock, IOptions<GrainRoutingOptions> routing,
    IOptions<DatabaseLimits> limits, string actualClusterId)
{
    internal Task<PartitionMoveParentState> LocalAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, PartitionMovementParentPhaseRole role, PartitionMovePhaseCommand intended,
        ReadExecutionBudget work, CancellationToken cancellationToken)
        => phases.AdmitAndExecuteAsync(principalId, request, state, role, intended, null,
            PartitionMovementParentDeadline.Expiry(work, clock, routing), work, cancellationToken);

    internal Task<PartitionMoveParentState> EffectAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, PartitionMovementParentPhaseRole grantRole,
        PartitionMovementParentPhaseRole effectRole, PartitionMovePhaseCommand intended, PhysicalShardRecord receiver,
        ReadExecutionBudget work, CancellationToken cancellationToken)
        => EffectAsync(principalId, request, state, grantRole, effectRole, intended, receiver,
            work, null, cancellationToken);

    internal async Task<PartitionMoveParentState> EffectAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, PartitionMovementParentPhaseRole grantRole,
        PartitionMovementParentPhaseRole effectRole, PartitionMovePhaseCommand intended, PhysicalShardRecord receiver,
        ReadExecutionBudget work, Func<GrainRequestPhase, CancellationToken, ValueTask>? phaseObservation, CancellationToken cancellationToken)
    {
        work.Check();
        var header = state.Header
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var control = state.Control
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (intended.Stage == PartitionMovePeerStage.StagePage)
        {
            if (phaseObservation is not null && intended.PageOrdinal == PartitionMovementProtocol.InitialPhaseOrdinal)
            { await phaseObservation(GrainRequestPhase.ParentStagePreflight, cancellationToken).ConfigureAwait(false); }
            work.Check();
            cancellationToken.ThrowIfCancellationRequested();
            var descriptor = state.Selected?.OriginalDescriptor
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
            PartitionMovementParentCaptureLimits.RequirePlannedStageCapacity(state, intended, receiver,
                descriptor.Resources, actualClusterId, Math.Min(limits.Value.MaxBatchBytes, work.MaximumResultBytes));
        }
        var cleanupGeneration = intended.Stage == PartitionMovePeerStage.Retire
            ? header.CleanupGeneration : PartitionMoveProtocol.EmptyCount;
        var grantId = PartitionMovementParentPhaseIds.For(request, principalId, grantRole,
            intended.PageOrdinal, cleanupGeneration);
        var effectId = PartitionMovementParentPhaseIds.For(request, principalId, effectRole,
            intended.PageOrdinal, cleanupGeneration);
        var expiry = PartitionMovementParentDeadline.Expiry(work, clock, routing);
        var authorize = PartitionMovementParentNativePhases.Authorize(header, control, grantId,
            effectId, intended, receiver, expiry);
        var promoted = await phases.AdmitAndExecuteAsync(principalId, request, state, grantRole,
            authorize, null, expiry, work, cancellationToken).ConfigureAwait(false);
        work.Check();
        // Only the current owning call which joined this actual first authorization may dispatch its promoted effect.
        return intended.Stage == PartitionMovePeerStage.Capture
            ? await captures.ExecutePromotedAsync(principalId, request, promoted, work, cancellationToken).ConfigureAwait(false)
            : await phases.ExecutePromotedAsync(principalId, request, promoted, work, cancellationToken).ConfigureAwait(false);
    }

    internal Task<PartitionMoveParentState> AcknowledgeAsync(string principalId, PartitionMoveRequest request,
        PartitionMoveParentState state, PartitionMovementParentPhaseRole role, PartitionMoveParentPhase original,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        var grant = original.OriginalGrant
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var actual = original.OriginalResult
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (actual.Error is { } error)
        { throw Errors.Fail(error, actual.SafeDetail ?? PartitionMoveProtocol.MissingAuthority); }
        if (original.ObservationCheckpointReceipt is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority); }
        var command = PartitionMovementParentNativePhases.Acknowledge(state.Header!, state.Control!,
            grant, actual.Get<PartitionMovePhaseResult>());
        return LocalAsync(principalId, request, state, role, command, work, cancellationToken);
    }
}
