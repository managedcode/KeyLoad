using KeyLoad.Orleans;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Shares the server's validated options with the borrowed native silo container.</summary>
internal sealed record ServerRuntimeOptions(
    IOptions<NodeOptions> Node,
    IOptions<ReplicaConfiguration> ReplicaConfiguration,
    IOptions<DueCoordinationOptions> DueCoordination,
    IOptions<ReplicaExecutionOptions> ReplicaExecution,
    IOptions<PeerDiscoveryOptions> PeerDiscovery,
    IOptions<ReplicaTransportOptions> ReplicaTransport,
    IOptions<OrleansMembershipOptions> Membership,
    IOptions<GrainRoutingOptions> GrainRouting,
    IOptions<ServerExecutionOptions> ServerExecution,
    CoreRuntimeOptions Core)
{
    internal void RegisterBorrowed(IServiceCollection services)
    {
        services.AddSingleton(Node);
        services.AddSingleton(ReplicaConfiguration);
        services.AddSingleton(DueCoordination);
        services.AddSingleton(ReplicaExecution);
        services.AddSingleton(PeerDiscovery);
        services.AddSingleton(ReplicaTransport);
        services.AddSingleton(Membership);
        services.AddSingleton(GrainRouting);
        services.AddSingleton(ServerExecution);
        Core.RegisterBorrowed(services);
    }
}
