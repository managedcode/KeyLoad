using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed partial class ResourceOwnerInventoryNativeClient
{
    private readonly ResourceRetirementNativeExchange retirement;

    internal Task RequireRetirementSourceAsync(string principalId, long policyEpoch,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => retirement.RequireSourceAsync(principalId, policyEpoch, work, metadata, token);

    internal Task<ResourceRetirementReadReplyV1> IssueOwnerReservationOperationAsync(RegisteredPhysicalOwnerV1 destination,
        ResourceRetirementReadRequestV1 query, string sourcePrincipalId, long sourcePolicyEpoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        if (query.Operation != ResourceRetirementReadOperation.IssueOwnerReservation)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementNativeProtocol.InvalidProof);
        }
        return ReadRetirementAsync(destination, query, sourcePrincipalId, sourcePolicyEpoch, work, metadata, token);
    }

    internal Task<ResourceRetirementPhaseResultV1> ExecuteRetirementPhaseAsync(RegisteredPhysicalOwnerV1 destination,
        Guid commandId, ResourceRetirementNativePhaseRequestV1 request, string sourcePrincipalId,
        long sourcePolicyEpoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => ExecuteAsync(request.ExpiresAt, NativeRequestWorkKind.CommandCapability, ResourceRetirementNativeProtocol.Unavailable,
            caller => retirement.PhaseAsync(destination, commandId, request, sourcePrincipalId, sourcePolicyEpoch, work, metadata, caller), token);

    internal Task<ResourceRetirementReadReplyV1> ReadRetirementToVoterAsync(RegisteredPhysicalOwnerV1 destination,
        string voter, ResourceRetirementReadRequestV1 query, string sourcePrincipalId, long sourcePolicyEpoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata,
        CancellationToken token)
        => ExecuteAsync(query.ExpiresAt, NativeRequestWorkKind.ReadCapability, ResourceRetirementNativeProtocol.Unavailable,
            caller => retirement.ReadToVoterAsync(destination, voter, query, sourcePrincipalId, sourcePolicyEpoch, work, metadata, caller), token);

    internal Task<ResourceRetirementWorkJoinEvidenceV1> ReadRetirementWorkAsync(RegisteredPhysicalOwnerV1 destination,
        string voter, ResourceRetirementReadRequestV1 query, string principalId, long epoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => ExecuteAsync(query.ExpiresAt, NativeRequestWorkKind.ReadCapability, ResourceRetirementNativeProtocol.Unavailable,
            caller => retirement.ReadWorkAsync(destination, voter, query, principalId, epoch, work, metadata, caller), token);

    internal Task<ResourceRetirementReadReplyV1> ReadRetirementAsync(RegisteredPhysicalOwnerV1 destination,
        ResourceRetirementReadRequestV1 query, string sourcePrincipalId, long sourcePolicyEpoch, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => ExecuteAsync(query.ExpiresAt, NativeRequestWorkKind.ReadCapability, ResourceRetirementNativeProtocol.Unavailable,
            caller => retirement.ReadAsync(destination, query, sourcePrincipalId, sourcePolicyEpoch, work, metadata, caller), token);
}
