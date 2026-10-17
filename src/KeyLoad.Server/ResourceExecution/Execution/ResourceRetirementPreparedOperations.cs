using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementPreparedOperations(ResourceRetirementPhaseExecution phases,
    ResourceRetirementPreparedOperationProof proof)
{
    internal async Task<ResourceRetirementPreparedOperationV1> PersistAsync(ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, ReplicatedOperation original, RegisteredPhysicalOwnerV1 receiver,
        string principalId, long epoch, DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var result = await ExecuteAsync(context, attempt, ResourceRetirementPreparedOperationAction.Persist, original,
            receiver, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        proof.RequirePersisted(result, context, attempt);
        if (!ResourceRetirementReceiptValidation.Same(result.Original, original))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return result;
    }

    internal async Task<ResourceRetirementPreparedOperationV1> CloseAsync(ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, RegisteredPhysicalOwnerV1 receiver, string principalId, long epoch,
        DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var result = await ExecuteAsync(context, attempt, ResourceRetirementPreparedOperationAction.Close, null,
            receiver, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        proof.RequireClosed(result, context, attempt);
        return result;
    }

    private async Task<ResourceRetirementPreparedOperationV1> ExecuteAsync(ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, ResourceRetirementPreparedOperationAction action, ReplicatedOperation? original,
        RegisteredPhysicalOwnerV1 receiver, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var result = await phases.ExecuteAsync(ResourceRetirementAttemptContextValidation.Request(context),
            ResourceRetirementPhase.PreparedOperation, new ResourceRetirementPreparedOperationBodyV1(context, attempt, action, original),
            receiver, receiver.Owner.PhysicalShardId, principalId, epoch, expiry, work, metadata, token, attempt.AttemptId).ConfigureAwait(false);
        if (result.State is not null || result.Fence is not null || result.PreparationState is not null
            || result.OwnerReservation is not null || result.PreparedOperation is not { } prepared
            || !ResourceRetirementReceiptValidation.Same(result.Receipt, prepared.Receipt))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        return prepared;
    }
}
