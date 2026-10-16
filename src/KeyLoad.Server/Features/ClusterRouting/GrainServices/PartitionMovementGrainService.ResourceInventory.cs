using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementGrainService
{
    private ResourceOwnerInventoryNativeRuntime? resourceInventory;

    public async Task<ResourceOwnerInventoryNativeReply> ReadResourceInventoryAsync(
        ResourceOwnerInventoryNativeRequest request, CancellationToken cancellationToken)
    {
        var active = resourceInventory
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryNativeProtocol.Unavailable);
        ResourceOwnerInventoryNativeReply? original = null;
        await global::ManagedCode.Orleans.Graph.Extensions.RequestContextHelper.RunWithCurrentCallerAsync(
            typeof(PartitionMovementGrainService).FullName!, nameof(IPartitionMovementGrainService.ReadResourceInventoryAsync),
            async () => { original = await active.ReadAsync(request, cancellationToken).ConfigureAwait(true); });
        return original ?? throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.Unavailable);
    }
}
