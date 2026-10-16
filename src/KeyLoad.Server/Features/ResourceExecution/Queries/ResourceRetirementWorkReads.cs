using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementWorkReads(ResourceOwnerInventoryNativeClient client)
{
    private const int Empty = 0;

    internal async Task<ResourceRetirementWorkJoinEvidenceV1> ExecuteAsync(ResourceRetirementOwnerFenceV1 fence,
        RegisteredPhysicalOwnerV1 owner, string voter, ResourceRetirementWorkOwnerReceiptV1? acknowledged,
        string principalId, long epoch, DateTimeOffset expiry, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, int remainingVoters, CancellationToken token)
    {
        work.Check();
        await client.RequireRetirementSourceAsync(principalId, epoch, work, metadata, token).ConfigureAwait(false);
        var bytes = metadata.RemainingBytes / remainingVoters;
        var records = metadata.RemainingRecords / remainingVoters;
        if (bytes <= Empty || records <= Empty)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        var query = new ResourceRetirementReadRequestV1(ResourceRetirementNativeProtocol.Version,
            ResourceRetirementReadOperation.ActualOwnerWorkJoin, fence.Control.Request, fence.Control, Guid.Empty,
            Empty, expiry, owner, string.Empty, string.Empty, Empty, bytes, records, work.MaximumResultBytes)
        {
            WorkFence = fence,
            AcknowledgedWork = acknowledged
        };
        work.CheckResult(query);
        var evidence = await client.ReadRetirementWorkAsync(owner, voter, query, principalId, epoch, work, metadata, token)
            .ConfigureAwait(false);
        work.CheckResult(evidence);
        return evidence;

    }
}
