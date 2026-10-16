using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementGrainServiceClient
{
    internal Task<ResourceOwnerInventoryNativeReply> ReadResourceInventoryPacketAsync(SiloAddress destination,
        ResourceOwnerInventoryNativeRequest request, CancellationToken token)
        => GetGrainService(destination).ReadResourceInventoryAsync(request, token);
}
