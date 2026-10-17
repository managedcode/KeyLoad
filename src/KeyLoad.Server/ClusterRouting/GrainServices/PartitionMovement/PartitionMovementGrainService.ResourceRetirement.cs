using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementGrainService
{
    public async Task<ResourceRetirementNativeReply> ExecuteResourceRetirementAsync(
        ResourceRetirementNativeRequest request, CancellationToken cancellationToken)
    {
        var active = resourceInventory
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementNativeReply? original = null;
        await global::ManagedCode.Orleans.Graph.Extensions.RequestContextHelper.RunWithCurrentCallerAsync(
            typeof(PartitionMovementGrainService).FullName!, nameof(IPartitionMovementGrainService.ExecuteResourceRetirementAsync),
            async () => { original = await active.ExecuteRetirementAsync(request, cancellationToken).ConfigureAwait(true); });
        return original ?? throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementNativeProtocol.Unavailable);
    }
}
