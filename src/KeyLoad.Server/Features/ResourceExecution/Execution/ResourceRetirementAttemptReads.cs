using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementAttemptReads(ResourceRetirementReads reads)
{
    internal async Task<ResourceRetirementAttemptV1> HistoryAsync(ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 current, RegisteredPhysicalOwnerV1 source, string principalId, long epoch,
        DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        var reply = await reads.ExecuteAsync(ResourceRetirementReadOperation.Attempt,
            ResourceRetirementAttemptContextValidation.Request(context), null, source, Guid.Empty, 0, expiry,
            principalId, epoch, work, metadata, token, attemptId: current.AttemptId, context: context).ConfigureAwait(false);
        if (!ResourceRetirementReceiptValidation.Same(reply.Attempt, current))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.Unavailable);
        }
        return reply.Attempt!;
    }

    internal async Task<ResourceRetirementPreparedOperationV1?> PreparedAsync(ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, RegisteredPhysicalOwnerV1 receiver, string principalId, long epoch,
        DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => (await reads.ExecuteAsync(ResourceRetirementReadOperation.PreparedOperation,
            ResourceRetirementAttemptContextValidation.Request(context), null, receiver, Guid.Empty, 0, expiry,
            principalId, epoch, work, metadata, token, attempt, context: context).ConfigureAwait(false)).PreparedOperation;

    internal async Task<ReplicatedOperation> IssueOwnerAsync(ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 attempt, RegisteredPhysicalOwnerV1 receiver, string principalId, long epoch,
        DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => (await reads.ExecuteAsync(ResourceRetirementReadOperation.IssueOwnerReservation,
            ResourceRetirementAttemptContextValidation.Request(context), null, receiver, Guid.Empty, 0, expiry,
            principalId, epoch, work, metadata, token, attempt, context: context).ConfigureAwait(false)).OriginalOperation!;
}
