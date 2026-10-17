using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementAttemptRecovery(ResourceRetirementAttemptReads reads,
    ResourceRetirementPreparedOperationProof proof)
{
    internal async Task<ResourceRetirementPreparedOperationV1?> ReadAsync(ResourceRetirementAttemptContextV1 context,
        ResourceRetirementAttemptV1 current, RegisteredPhysicalOwnerV1 source, RegisteredPhysicalOwnerV1 receiver,
        string principalId, long epoch, DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        _ = await reads.HistoryAsync(context, current, source, principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
        var prepared = await reads.PreparedAsync(context, current, receiver, principalId, epoch, expiry, work, metadata, token)
            .ConfigureAwait(false);
        if (prepared is null)
        {
            if (current.Stage != ResourceRetirementAttemptStage.Prepared)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            }
            return null;
        }
        if (prepared.Stage == ResourceRetirementPreparedOperationStage.Closed)
        {
            proof.RequireClosed(prepared, context, current);
        }
        else
        {
            proof.RequirePersisted(prepared, context, current);
        }
        return prepared;
    }
}
