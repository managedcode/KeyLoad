using KeyLoad.Server.Features.ResourceExecution;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementGrainServiceClient
{
    internal Task<ResourceRetirementNativeReply> ExecuteResourceRetirementPacketAsync(SiloAddress destination,
        ResourceRetirementNativeRequest request, CancellationToken token)
        => GetGrainService(destination).ExecuteResourceRetirementAsync(request, token);
}
