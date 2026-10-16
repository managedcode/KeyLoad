using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementPreparing
{
    private readonly ResourceRetirementReads reads;
    private readonly ResourceRetirementPhaseExecution phases;
    private readonly IOptions<DatabaseLimits> limits;
    private readonly ResourceRetirementPreparationReads preparationReads;
    private readonly ResourceRetirementOwnerReservations reservations;
    private readonly ResourceRetirementPreparationReadiness readiness;

    internal ResourceRetirementPreparing(ResourceRetirementReads reads, ResourceRetirementPhaseExecution phases,
        IOptions<DatabaseLimits> limits, TimeProvider clock)
    {
        this.reads = reads;
        this.phases = phases;
        this.limits = limits;
        preparationReads = new(reads);
        var proof = new ResourceRetirementPreparedOperationProof(limits);
        var attempts = new ResourceRetirementAttemptSource(phases, proof, limits);
        var attemptReads = new ResourceRetirementAttemptReads(reads);
        var recovery = new ResourceRetirementAttemptRecovery(attemptReads, proof);
        var operations = new ResourceRetirementPreparedOperations(phases, proof);
        reservations = new(preparationReads, attempts, recovery, attemptReads, operations, phases, proof, limits, clock);
        readiness = new(preparationReads);
    }

    internal async Task<ResourceRetirementStateV1> ExecuteAsync(ResourceRetirementRequestV1 request,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var state = await LoadAsync(request, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        if (state.BegunControl is not null)
        {
            return await ReadBegunAsync(state, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        }
        state = await ReserveAllAsync(state, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        var current = await preparationReads.StateAsync(request, source, principalId, epoch, expiry, work, metadata, token)
            .ConfigureAwait(false) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        RequireState(current, request, source);
        if (!ResourceRetirementReceiptValidation.Same(current.Control, state.Control))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.Unavailable);
        }
        if (current.BegunControl is not null)
        {
            return await ReadBegunAsync(current, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        }
        var statuses = await readiness.ReadAsync(current, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        var body = new ResourceRetirementBeginBodyV1(request) { Preparation = current.Control, OwnerStatuses = statuses };
        var result = await phases.ExecuteAsync(request, ResourceRetirementPhase.Begin, body, source,
            source.Owner.PhysicalShardId, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        var retired = result.State ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementReceiptValidation.RequireState(retired, request, source);
        if (result.Fence is not null || result.PreparedOperation is not null || result.PreparationState is not null
            || result.OwnerReservation is not null || retired.Control.Lifetime != current.Control.Lifetime
            || !ResourceRetirementReceiptValidation.Same(result.Receipt, retired.Control.BeginReceipt))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return retired;
    }

    private async Task<ResourceRetirementStateV1> ReadBegunAsync(ResourceRetirementPreparationStateV1 preparation,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var request = preparation.Control.Request;
        var existing = (await reads.ExecuteAsync(ResourceRetirementReadOperation.State, request, null, source,
            Guid.Empty, 0, expiry, principalId, epoch, work, metadata, token).ConfigureAwait(false)).State
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementReceiptValidation.RequireState(existing, request, source);
        if (!ResourceRetirementReceiptValidation.Same(existing.Control, preparation.BegunControl))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return existing;
    }

    private async Task<ResourceRetirementPreparationStateV1> LoadAsync(ResourceRetirementRequestV1 request,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var state = await preparationReads.StateAsync(request, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        if (state is null)
        {
            var result = await phases.ExecuteAsync(request, ResourceRetirementPhase.PrepareRetirement,
                new ResourceRetirementPrepareBodyV1(request), source, source.Owner.PhysicalShardId,
                principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
            state = RequirePreparationResult(result);
            if (!ResourceRetirementReceiptValidation.Same(result.Receipt, state.Control.PrepareReceipt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
        }
        RequireState(state, request, source);
        return state;
    }

    private async Task<ResourceRetirementPreparationStateV1> ReserveAllAsync(ResourceRetirementPreparationStateV1 state,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        foreach (var receiver in state.Control.Directory.Owners.OrderByDescending(value =>
            PhysicalOwnerEntryValidation.SameOwner(value.Owner, source.Owner)))
        {
            var reservation = await reservations.EnsureAsync(state, source, receiver, principalId, epoch, expiry, work, metadata, token)
                .ConfigureAwait(false);
            var recorded = state.Reservations.SingleOrDefault(value => ResourceRetirementValidation.Same(value.Receiver, receiver));
            if (recorded is not null)
            {
                if (!ResourceRetirementReceiptValidation.Same(recorded, reservation))
                {
                    throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
                }
                continue;
            }
            var result = await phases.ExecuteAsync(state.Control.Request, ResourceRetirementPhase.RecordReservation,
                new ResourceRetirementRecordReservationBodyV1(state.Control, reservation), source,
                receiver.Owner.PhysicalShardId, principalId, epoch, expiry, work, metadata, token, reservation.Attempt!.AttemptId)
                .ConfigureAwait(false);
            var next = RequirePreparationResult(result);
            RequireState(next, state.Control.Request, source);
            ResourceRetirementPreparationProof.RequireReceipt(result.Receipt, state.Control, source.Owner,
                ResourceRetirementPhase.RecordReservation, ResourceRetirementIdentity.AttemptCommandId(state.Control.Request,
                    ResourceRetirementPhase.RecordReservation, reservation.Attempt.AttemptId, receiver.Owner.PhysicalShardId));
            if (!ResourceRetirementReceiptValidation.Same(next.Control, state.Control)
                || !next.Reservations.Any(value => ResourceRetirementReceiptValidation.Same(value, reservation)))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            state = next;
        }
        return state;
    }

    private void RequireState(ResourceRetirementPreparationStateV1 state, ResourceRetirementRequestV1 request,
        RegisteredPhysicalOwnerV1 source)
    {
        ResourceRetirementPreparationStatuses.RequireState(state, request, source);
        ResourceRetirementAttemptPointers.Require(state, limits.Value);
    }

    private static ResourceRetirementPreparationStateV1 RequirePreparationResult(ResourceRetirementPhaseResultV1 result)
    {
        if (result.State is not null || result.Fence is not null || result.PreparedOperation is not null
            || result.OwnerReservation is not null || result.PreparationState is not { } preparation)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return preparation;
    }
}
