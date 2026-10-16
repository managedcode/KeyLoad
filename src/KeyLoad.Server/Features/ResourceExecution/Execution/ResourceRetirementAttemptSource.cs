using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;
using CoreAttempt = KeyLoad.Core.Features.ResourceExecution.ResourceRetirementAttemptValidation;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementAttemptSource(ResourceRetirementPhaseExecution phases,
    ResourceRetirementPreparedOperationProof proof, IOptions<DatabaseLimits> limits)
{
    internal async Task<ResourceRetirementAttemptV1> PrepareAsync(ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1? previous, ResourceRetirementPreparedOperationV1? closed,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        ResourceRetirementAttemptContextValidation.Require(context, limits.Value);
        if (previous is not null)
        {
            proof.RequireClosed(closed ?? throw Errors.Fail(ErrorCode.RecoveryRequired,
                ResourceRetirementNativeProtocol.Unavailable), context, previous);
        }
        var attemptId = Guid.NewGuid();
        var body = new ResourceRetirementPrepareAttemptBodyV1(context, attemptId, previous?.Ordinal ?? 0, expiry)
        {
            PreviousClosed = closed
        };
        var result = await phases.ExecuteAsync(ResourceRetirementAttemptContextValidation.Request(context),
            ResourceRetirementPhase.PrepareAttempt, body, source,
            ResourceRetirementAttemptContextValidation.ExecutionOwner(context).PhysicalShardId,
            principalId, epoch, expiry, work, metadata, token, attemptId).ConfigureAwait(false);
        var attempt = ReadResult(result, context, attemptId, source);
        CoreAttempt.Witness(attempt, context, limits.Value);
        if (attempt.AttemptId != attemptId || attempt.Ordinal != checked((previous?.Ordinal ?? 0) + 1)
            || attempt.ExpiresAt != expiry || attempt.PrincipalId != principalId || attempt.PolicyEpoch != epoch
            || !ResourceRetirementReceiptValidation.Same(attempt.PrepareReceipt, result.Receipt))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return attempt;
    }

    private ResourceRetirementAttemptV1 ReadResult(ResourceRetirementPhaseResultV1 result,
        ResourceRetirementAttemptContextV1 context, Guid attemptId, RegisteredPhysicalOwnerV1 source)
    {
        if (result.Fence is not null || result.PreparedOperation is not null || result.OwnerReservation is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        if (context.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
        {
            var attempt = result.CleanupAttempt
                ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            var closed = result with
            {
                CleanupAttempt = null
            };
            if (!ResourceRetirementReceiptValidation.Same(closed, new ResourceRetirementPhaseResultV1(result.Receipt, null, null)))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            return attempt;
        }
        if (context.Purpose == ResourceRetirementAttemptPurpose.SealBarrier)
        {
            var state = result.State ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            ResourceRetirementReceiptValidation.RequireState(state, context.Retirement!.Request, source);
            if (result.PreparationState is not null || !ResourceRetirementReceiptValidation.Same(state.Control, context.Retirement))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
            return state.SealAttempt ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        var preparation = result.PreparationState ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementPreparationStatuses.RequireState(preparation, context.Preparation!.Request, source);
        ResourceRetirementAttemptPointers.Require(preparation, limits.Value);
        if (result.State is not null || !ResourceRetirementReceiptValidation.Same(preparation.Control, context.Preparation))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return preparation.OwnerAttempts.Single(value => value.AttemptId == attemptId);
    }
}
