using Orleans.Runtime.Services;

namespace KeyLoad.Clustering;

/// <summary>Borrows actual directed silo RPC without an independent cancellation wait or network owner.</summary>
internal sealed class ReplicaNativeDiscoveryClient(IServiceProvider services)
    : GrainServiceClient<IPartitionReplicaGrainService>(services)
{
    internal Task<ReplicaNativeDiscoveryReply> DiscoverAsync(SiloAddress address,
        ReplicaNativeDiscoveryRequest request, CancellationToken token)
        => GetGrainService(address).DiscoverAsync(request, token);
}
