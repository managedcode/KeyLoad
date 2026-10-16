using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Uses the original charged private read lane for durable preparation and exact-voter status.</summary>
internal sealed class ResourceRetirementPreparationReads(ResourceRetirementReads reads)
{
    internal async Task<ResourceRetirementPreparationStateV1?> StateAsync(ResourceRetirementRequestV1 request,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => (await reads.ExecuteAsync(ResourceRetirementReadOperation.Preparation, request, null, source, Guid.Empty, 0,
            expiry, principalId, epoch, work, metadata, token).ConfigureAwait(false)).Preparation;

    internal async Task<ResourceRetirementOwnerStatusV1?> StatusAsync(ResourceRetirementPreparationControlV1 control,
        RegisteredPhysicalOwnerV1 receiver, string voter, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => (await reads.ExecuteAsync(ResourceRetirementReadOperation.OwnerStatus, control.Request, null, receiver,
            Guid.Empty, 0, expiry, principalId, epoch, work, metadata, token, preparation: control, voter: voter)
            .ConfigureAwait(false)).OwnerStatus;

}
