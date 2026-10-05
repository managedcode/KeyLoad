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
    IOptions<ReplicaPeerOptions> Peer,
    IOptions<ReplicaReplayLimits> Replay,
    IOptions<CommandAdmissionLimits> CommandAdmission,
    IOptions<HttpAdmissionLimits> HttpAdmission,
    IOptions<OrleansMembershipOptions> Membership,
    IOptions<GrainRoutingOptions> GrainRouting,
    IOptions<ServerExecutionOptions> ServerExecution,
    CoreRuntimeOptions Core)
{
    internal void ValidateBeforePhysicalOwnership()
    {
        _ = Node.Value;
        _ = ReplicaConfiguration.Value;
        _ = DueCoordination.Value;
        _ = ReplicaExecution.Value;
        _ = PeerDiscovery.Value;
        _ = ReplicaTransport.Value;
        _ = Replay.Value;
        _ = Peer.Value;
        _ = CommandAdmission.Value;
        _ = HttpAdmission.Value;
        _ = Membership.Value;
        _ = GrainRouting.Value;
        _ = ServerExecution.Value;
        Core.ValidateBeforePhysicalOwnership();
    }

    internal void RegisterBorrowed(IServiceCollection services)
    {
        services.AddSingleton(Node);
        services.AddSingleton(ReplicaConfiguration);
        services.AddSingleton(DueCoordination);
        services.AddSingleton(ReplicaExecution);
        services.AddSingleton(PeerDiscovery);
        services.AddSingleton(ReplicaTransport);
        services.AddSingleton(Peer);
        services.AddSingleton(Replay);
        services.AddSingleton(CommandAdmission);
        services.AddSingleton(HttpAdmission);
        services.AddSingleton(Membership);
        services.AddSingleton(GrainRouting);
        services.AddSingleton(ServerExecution);
        Core.RegisterBorrowed(services);
    }
}
