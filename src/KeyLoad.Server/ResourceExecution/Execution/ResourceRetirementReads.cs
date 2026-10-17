using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementReads(ResourceOwnerInventoryNativeClient client)
{
    internal async Task<ResourceRetirementReadReplyV1> ExecuteAsync(ResourceRetirementReadOperation operation,
        ResourceRetirementRequestV1? request, ResourceRetirementControlV1? control, RegisteredPhysicalOwnerV1 owner,
        Guid inventoryId, int pageSize, DateTimeOffset expiry, string principalId, long policyEpoch,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token, ResourceRetirementAttemptV1? attempt = null,
        Guid attemptId = default, ResourceRetirementPreparationControlV1? preparation = null,
        ResourceRetirementAttemptContextV1? context = null, string? voter = null,
        ResourceRetirementDropParentLookupV1? parentLookup = null)
    {
        work.Check();
        await client.RequireRetirementSourceAsync(principalId, policyEpoch, work, metadata, token).ConfigureAwait(false);
        var maximumBytes = metadata.RemainingBytes;
        var maximumRecords = metadata.RemainingRecords;
        if (maximumBytes <= ResourceRetirementReadContract.NoReadBytes || maximumRecords <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        var query = new ResourceRetirementReadRequestV1(ResourceRetirementNativeProtocol.Version, operation,
            request, control, inventoryId, pageSize, expiry, owner, string.Empty, string.Empty, ResourceRetirementReadContract.MinimumPolicyEpoch,
            maximumBytes, maximumRecords, work.MaximumResultBytes)
        {
            Attempt = attempt,
            AttemptId = attemptId,
            Preparation = preparation,
            Context = context,
            ParentLookup = parentLookup
        };
        if (NativeSerialization.Measure(query) > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.MeasureResult(query);
        var call = operation == ResourceRetirementReadOperation.IssueOwnerReservation
            ? client.IssueOwnerReservationOperationAsync(owner, query, principalId, policyEpoch, work, metadata, token)
            : voter is null ? client.ReadRetirementAsync(owner, query, principalId, policyEpoch, work, metadata, token)
                : client.ReadRetirementToVoterAsync(owner, voter, query, principalId, policyEpoch, work, metadata, token);
        var reply = await call.ConfigureAwait(false);
        if (reply.ReadBytes < ResourceRetirementReadContract.NoReadBytes || reply.ExaminedRecords < ResourceRetirementReadContract.NoRecords || !HasExpectedShape(operation, reply, attemptId, context))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
        }
        if (NativeSerialization.Measure(reply) > work.MaximumResultBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.MeasureResult(reply);
        return reply;
    }

    private static bool HasExpectedShape(ResourceRetirementReadOperation operation, ResourceRetirementReadReplyV1 reply,
        Guid attemptId, ResourceRetirementAttemptContextV1? context)
    {
        if (reply.WorkJoinWitness is not null || reply.AcknowledgedOwner is not null)
        {
            return false;
        }
        if (operation != ResourceRetirementReadOperation.DropParent && reply.Parent is not null)
        {
            return false;
        }
        var preparationAbsent = reply.Preparation is null && reply.OwnerStatus is null && reply.OriginalOperation is null;
        var retirementAbsent = reply.State is null && reply.Fence is null && reply.InventoryControl is null
            && reply.Attempt is null && reply.PreparedOperation is null;
        return operation switch
        {
            ResourceRetirementReadOperation.DropParent => preparationAbsent && retirementAbsent,
            ResourceRetirementReadOperation.OriginalWorkSeal or ResourceRetirementReadOperation.OriginalD3Seal
                or ResourceRetirementReadOperation.State => preparationAbsent && reply.Fence is null
                && reply.InventoryControl is null && reply.Attempt is null && reply.PreparedOperation is null,
            ResourceRetirementReadOperation.Fence => preparationAbsent && reply.State is null
                && reply.InventoryControl is null && reply.Attempt is null && reply.PreparedOperation is null,
            ResourceRetirementReadOperation.CaptureRetiringInventory => preparationAbsent && reply.State is null
                && reply.Fence is null && reply.InventoryControl is not null && reply.Attempt is null && reply.PreparedOperation is null,
            ResourceRetirementReadOperation.Attempt => preparationAbsent && reply.State is null && reply.Fence is null
                && reply.InventoryControl is null && (reply.Attempt is not null
                    || attemptId == Guid.Empty && context?.Purpose == ResourceRetirementAttemptPurpose.CleanupPage)
                && reply.PreparedOperation is null,
            ResourceRetirementReadOperation.PreparedOperation => preparationAbsent && reply.State is null && reply.Fence is null
                && reply.InventoryControl is null && reply.Attempt is null,
            ResourceRetirementReadOperation.Preparation => retirementAbsent && reply.OwnerStatus is null
                && reply.OriginalOperation is null,
            ResourceRetirementReadOperation.OwnerStatus => retirementAbsent && reply.Preparation is null
                && reply.OriginalOperation is null,
            ResourceRetirementReadOperation.IssueOwnerReservation => retirementAbsent && reply.Preparation is null
                && reply.OwnerStatus is null && reply.OriginalOperation is not null,
            _ => false
        };
    }
}
