using Orleans.Runtime.Services;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PhysicalOwnerProbeNativeClient(IServiceProvider services)
    : GrainServiceClient<IPartitionMovementGrainService>(services)
{
    internal Task<PartitionMovementGrainServiceReply> ProbeOwnerAsync(SiloAddress address,
        PartitionMovementGrainServiceRequest request, CancellationToken token)
        => GetGrainService(address).ProbeOwnerAsync(request, token);
}
