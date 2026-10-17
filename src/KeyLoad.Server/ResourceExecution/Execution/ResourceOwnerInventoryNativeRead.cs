using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Settles authenticated native child work into the original bounded operation ledger.</summary>
internal sealed class ResourceOwnerInventoryNativeRead(ResourceOwnerInventoryNativeClient client)
{
    private const long NoReadBytes = 0;
    private const int NoExaminedRecords = 0;
    private const long UnboundPolicyEpoch = 0;
    internal async Task<ResourceOwnerInventoryReadReplyV1> ExecuteAsync(bool local,
        ResourceOwnerInventoryReadOperation operation, ResourceOwnerInventoryRequestV1 request,
        ResourceOwnerInventoryControlV1? control, RegisteredPhysicalOwnerV1 owner, string? voter,
        ResourceOwnerInventoryCursorV1? continuation, ResourceOwnerInventoryOwnerCutV1? cut,
        string principalId, long policyEpoch, ReadExecutionBudget work, CancellationToken token)
    {
        work.Check();
        var maximumBytes = work.RemainingReadGrantBytes;
        var maximumRecords = work.RemainingReadGrantRecords;
        if (maximumBytes <= NoReadBytes || maximumRecords <= NoExaminedRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        var grant = work.CreateReadGrant(maximumBytes, maximumRecords);
        try
        {
            var query = new ResourceOwnerInventoryReadRequestV1(ResourceOwnerInventoryNativeProtocol.Version,
                operation, request, control, owner, continuation, cut, string.Empty, string.Empty, UnboundPolicyEpoch,
                maximumBytes, maximumRecords, work.MaximumResultBytes);
            work.MeasureResult(query);
            var reply = local
                ? await client.ExecuteLocalAsync(query, principalId, policyEpoch, token).ConfigureAwait(false)
                : await client.SendToVoterAsync(owner, voter ?? throw Errors.Fail(ErrorCode.Validation,
                    ResourceOwnerInventoryNativeProtocol.InvalidProof), query, principalId, policyEpoch, token).ConfigureAwait(false);
            if (reply.ReadBytes < NoReadBytes || reply.ExaminedRecords < NoExaminedRecords || !HasExpectedShape(operation, reply))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.Unavailable);
            }
            work.ImportSettledReadGrant(grant, reply.ReadBytes, reply.ExaminedRecords);
            work.MeasureResult(reply);
            return reply;
        }
        finally
        {
            work.CompleteSettledReadGrant(grant);
        }
    }

    private static bool HasExpectedShape(ResourceOwnerInventoryReadOperation operation,
        ResourceOwnerInventoryReadReplyV1 reply)
        => operation switch
        {
            ResourceOwnerInventoryReadOperation.CaptureControl => reply.Control is not null
                && reply.Page is null && !reply.Validated,
            ResourceOwnerInventoryReadOperation.ReadPage => reply.Control is null
                && reply.Page is not null && !reply.Validated,
            ResourceOwnerInventoryReadOperation.ValidateControl or ResourceOwnerInventoryReadOperation.ValidateOwner
                => reply.Control is null && reply.Page is null && reply.Validated,
            _ => false
        };
}
