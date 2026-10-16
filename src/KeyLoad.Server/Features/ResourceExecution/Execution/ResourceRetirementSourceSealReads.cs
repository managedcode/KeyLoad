using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class ResourceRetirementSourceSealReads
{
    internal static async Task<ReplicatedOperation> ExecuteAsync(ResourceOwnerInventoryNativeClient client,
        ResourceRetirementSealBodyV1 body, RegisteredPhysicalOwnerV1 source, string principalId, long policyEpoch,
        DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        work.Check();
        await client.RequireRetirementSourceAsync(principalId, policyEpoch, work, metadata, token).ConfigureAwait(false);
        var bytes = metadata.RemainingBytes;
        var records = metadata.RemainingRecords;
        if (bytes <= ResourceRetirementReadContract.NoReadBytes || records <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        var query = new ResourceRetirementReadRequestV1(ResourceRetirementNativeProtocol.Version,
            ResourceRetirementReadOperation.IssueSourceSeal, body.Control.Request, body.Control,
            body.Attempt!.OriginalCommandId, ResourceRetirementReadContract.NoRecords, expiry, source,
            string.Empty, string.Empty, ResourceRetirementReadContract.MinimumPolicyEpoch,
            bytes, records, work.MaximumResultBytes)
        {
            Attempt = body.Attempt,
            SourceSeal = body
        };
        if (NativeSerialization.Measure(query) > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.MeasureResult(query);
        var reply = await client.ReadRetirementAsync(source, query, principalId, policyEpoch, work, metadata, token).ConfigureAwait(false);
        work.CheckResult(reply);
        return reply.OriginalOperation
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.InvalidProof);

    }
}
