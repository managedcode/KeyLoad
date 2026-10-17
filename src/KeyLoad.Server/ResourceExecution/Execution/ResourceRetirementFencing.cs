using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementFencing(ResourceRetirementReads reads, ResourceRetirementPhaseExecution phases)
{
    private const int NoPageSize = 0;
    internal async Task<ResourceRetirementStateV1> ExecuteAsync(ResourceRetirementStateV1 state,
        RegisteredPhysicalOwnerV1 source, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        foreach (var owner in state.Control.Directory.Owners.OrderByDescending(value =>
            PhysicalOwnerEntryValidation.SameOwner(value.Owner, source.Owner)))
        {
            var existing = state.OwnerReceipts.FirstOrDefault(value =>
                PhysicalOwnerEntryValidation.SameOwner(value.PhysicalOwner, owner.Owner));
            var fence = await ReadFenceAsync(state.Control, owner, principalId, epoch, expiry, work, metadata, token)
                .ConfigureAwait(false);
            if (fence is null)
            {
                if (existing is not null)
                {
                    throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
                }
                var result = await phases.ExecuteAsync(state.Control.Request, ResourceRetirementPhase.FenceOwner,
                    new ResourceRetirementFenceBodyV1(state.Control, owner), owner, owner.Owner.PhysicalShardId,
                    principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
                fence = result.Fence ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
                if (result.State is not null || result.PreparedOperation is not null || !ResourceRetirementReceiptValidation.Same(result.Receipt, fence.Receipt))
                {
                    throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
                }
            }
            ResourceRetirementReceiptValidation.RequireFence(fence, state.Control, owner);
            if (existing is not null)
            {
                if (!ResourceRetirementReceiptValidation.Same(existing, fence.Receipt))
                {
                    throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
                }
                continue;
            }
            var recorded = await phases.ExecuteAsync(state.Control.Request, ResourceRetirementPhase.RecordOwnerReceipt,
                new ResourceRetirementReceiptBodyV1(state.Control, fence.Receipt), source, owner.Owner.PhysicalShardId,
                principalId, epoch, expiry, work, metadata, token).ConfigureAwait(false);
            state = ResourceRetirementReceiptValidation.RequireSourcePhase(recorded, state.Control, ResourceRetirementPhase.RecordOwnerReceipt,
                source, owner.Owner.PhysicalShardId);
            var retained = state.OwnerReceipts.SingleOrDefault(value =>
                PhysicalOwnerEntryValidation.SameOwner(value.PhysicalOwner, owner.Owner));
            if (retained is null || !ResourceRetirementReceiptValidation.Same(retained, fence.Receipt))
            {
                throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
            }
        }
        return state;
    }

    private async Task<ResourceRetirementOwnerFenceV1?> ReadFenceAsync(ResourceRetirementControlV1 control,
        RegisteredPhysicalOwnerV1 owner, string principalId, long epoch, DateTimeOffset expiry,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
        => (await reads.ExecuteAsync(ResourceRetirementReadOperation.Fence, control.Request, control, owner, Guid.Empty, NoPageSize,
            expiry, principalId, epoch, work, metadata, token).ConfigureAwait(false)).Fence;

}
