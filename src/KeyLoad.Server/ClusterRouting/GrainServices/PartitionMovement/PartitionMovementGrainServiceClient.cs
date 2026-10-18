using KeyLoad.Clustering;
using Microsoft.Extensions.Options;
using Orleans.Runtime.Services;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Addresses the actual native silo after authenticated native discovery.</summary>
internal sealed partial class PartitionMovementGrainServiceClient : GrainServiceClient<IPartitionMovementGrainService>
{
    private readonly PartitionMovementNativeDiscovery discovery;

    internal PartitionMovementGrainServiceClient(IServiceProvider services, PartitionHost partition,
        IOptions<NodeOptions> options, IOptions<OrleansMembershipOptions> membership, ISiloStatusOracle oracle)
        : base(services)
        => discovery = new(partition, options, membership, oracle, DiscoverAsync);

    internal Task<SiloAddress> ResolveAsync(RegisteredPhysicalOwnerV1 owner, int index, CancellationToken token)
        => discovery.ResolveAsync(owner, index, token);

    internal Task RequireCallerAsync(string voter, string address, CancellationToken token)
        => discovery.RequireCallerAsync(voter, address, token);

    internal Task RequireCallerAsync(RegisteredPhysicalOwnerV1 expectedOwner, string voter,
        string address, CancellationToken token)
        => discovery.RequireCallerAsync(expectedOwner, voter, address, token);

    internal Task<PartitionMovementGrainServiceDiscovery> DiscoverAsync(SiloAddress address, Guid nonce, CancellationToken token)
        => GetGrainService(address).DiscoverAsync(nonce, token);
}
