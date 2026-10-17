using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;
using CoreAttempt = KeyLoad.Core.Features.ResourceExecution.ResourceRetirementAttemptValidation;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementOwnerReservations(ResourceRetirementPreparationReads preparationReads,
    ResourceRetirementAttemptSource attempts, ResourceRetirementAttemptRecovery recovery,
    ResourceRetirementAttemptReads reads, ResourceRetirementPreparedOperations operations,
    ResourceRetirementPhaseExecution phases, ResourceRetirementPreparedOperationProof proof,
    IOptions<DatabaseLimits> limits, TimeProvider clock)
{
    private const int FirstVoter = 0;

    internal async Task<ResourceRetirementOwnerReservationV1> EnsureAsync(ResourceRetirementPreparationStateV1 state,
        RegisteredPhysicalOwnerV1 source, RegisteredPhysicalOwnerV1 receiver, string principalId, long epoch,
        DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var status = await preparationReads.StatusAsync(state.Control, receiver, receiver.Owner.VoterIds[FirstVoter],
            principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        if (status is not null)
        {
            ResourceRetirementPreparationProof.RequireReservation(status.Reservation, state.Control, receiver);
            return status.Reservation;
        }
        if (state.Reservations.Any(value => ResourceRetirementValidation.Same(value.Receiver, receiver)))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        var context = CoreAttempt.Owner(state.Control, receiver);
        var previous = state.OwnerAttempts.SingleOrDefault(value => ResourceRetirementAttemptPointers.Belongs(value, state.Control, receiver));
        var (attempt, prepared, fresh) = await ResumeAsync(context, previous, source, receiver, principalId, epoch,
            expiry, work, metadata, token).ConfigureAwait(false);
        if (fresh)
        {
            // This invocation just received the actual source5 ACK for a fresh GUID; issue9 exactly once.
            var original = await reads.IssueOwnerAsync(context, attempt, receiver, principalId, epoch,
                expiry, work, metadata, token).ConfigureAwait(false);
            prepared = await operations.PersistAsync(context, attempt, original, receiver, principalId, epoch,
                expiry, work, metadata, token).ConfigureAwait(false);
        }
        return await SubmitAsync(prepared ?? throw Errors.Fail(ErrorCode.RecoveryRequired,
            ResourceRetirementNativeProtocol.Unavailable), context, attempt, receiver, principalId, epoch,
            expiry, work, metadata, token).ConfigureAwait(false);
    }

    private async Task<(ResourceRetirementAttemptV1 Attempt, ResourceRetirementPreparedOperationV1? Prepared, bool Fresh)>
        ResumeAsync(ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1? previous,
            RegisteredPhysicalOwnerV1 source, RegisteredPhysicalOwnerV1 receiver, string principalId, long epoch,
            DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        ResourceRetirementPreparedOperationV1? closed = null;
        if (previous is not null)
        {
            CoreAttempt.Require(previous, context, limits.Value);
            if (previous.Stage == ResourceRetirementAttemptStage.Completed)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            }
            var prepared = await recovery.ReadAsync(context, previous, source, receiver, principalId, epoch,
                expiry, work, metadata, token).ConfigureAwait(false);
            if (prepared?.Stage == ResourceRetirementPreparedOperationStage.Closed)
            {
                closed = prepared;
            }
            else if (previous.ExpiresAt <= clock.GetUtcNow())
            {
                closed = await operations.CloseAsync(context, previous, receiver, principalId, epoch,
                    expiry, work, metadata, token).ConfigureAwait(false);
            }
            else
            {
                return (previous, RequireResumable(prepared, previous), false);
            }
        }
        var attempt = await attempts.PrepareAsync(context, previous, closed, source, principalId, epoch,
            expiry, work, metadata, token).ConfigureAwait(false);
        return (attempt, null, true);
    }

    private async Task<ResourceRetirementOwnerReservationV1> SubmitAsync(ResourceRetirementPreparedOperationV1 prepared,
        ResourceRetirementAttemptContextV1 context, ResourceRetirementAttemptV1 attempt, RegisteredPhysicalOwnerV1 receiver,
        string principalId, long epoch, DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        proof.RequirePersisted(prepared, context, attempt);
        expiry = expiry <= attempt.ExpiresAt ? expiry : attempt.ExpiresAt;
        work.ConstrainLifetime(expiry);
        var result = await phases.SubmitOriginalAsync(prepared, receiver, principalId, epoch, expiry, work, metadata, token)
            .ConfigureAwait(false);
        var reservation = result.OwnerReservation ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementPreparationProof.RequireReservation(reservation, context.Preparation!, receiver);
        if (result.State is not null || result.Fence is not null || result.PreparedOperation is not null
            || result.PreparationState is not null || reservation.OriginalFingerprint != prepared.Fingerprint
            || !ResourceRetirementReceiptValidation.Same(reservation.Attempt, CoreAttempt.Prepared(attempt))
            || !ResourceRetirementReceiptValidation.Same(result.Receipt, reservation.Receipt))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return reservation;
    }
    private static ResourceRetirementPreparedOperationV1 RequireResumable(
        ResourceRetirementPreparedOperationV1? prepared, ResourceRetirementAttemptV1 attempt)
    {
        if (prepared is null || attempt.Stage != ResourceRetirementAttemptStage.Prepared)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        }
        return prepared;
    }

}
