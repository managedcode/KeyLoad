using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Shares the server's validated options with the borrowed native silo container.</summary>
internal sealed record ServerRuntimeOptions(
    IOptions<NodeOptions> Node,
    IOptions<ReplicaConfiguration> ReplicaConfiguration,
    IOptions<DueCoordinationOptions> DueCoordination,
    IOptions<NativeDurableJobOptions> DurableJobs,
    IOptions<ReplicaExecutionOptions> ReplicaExecution,
    IOptions<OfflineRecoveryExecutionOptions> OfflineRecovery,
    IOptions<PeerDiscoveryOptions> PeerDiscovery,
    IOptions<ReplicaTransportOptions> ReplicaTransport,
    IOptions<ReplicaPeerOptions> Peer,
    IOptions<ReplicaReplayLimits> Replay,
    IOptions<CommandAdmissionLimits> CommandAdmission,
    IOptions<HttpAdmissionLimits> HttpAdmission,
    IOptions<ZoneTreeStorageExecutionOptions> StorageExecution,
    IOptions<ZoneTreePointCacheExecutionOptions> PointCache,
    IOptions<RequestProbeExecutionOptions> RequestProbeExecution,
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
        _ = DurableJobs.Value;
        _ = ReplicaExecution.Value;
        _ = OfflineRecovery.Value;
        _ = PeerDiscovery.Value;
        _ = ReplicaTransport.Value;
        _ = Replay.Value;
        _ = Peer.Value;
        _ = CommandAdmission.Value;
        _ = HttpAdmission.Value;
        _ = StorageExecution.Value;
        _ = PointCache.Value;
        _ = RequestProbeExecution.Value;
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
        services.AddSingleton(DurableJobs);
        services.AddSingleton(ReplicaExecution);
        services.AddSingleton(OfflineRecovery);
        services.AddSingleton(PeerDiscovery);
        services.AddSingleton(ReplicaTransport);
        services.AddSingleton(Peer);
        services.AddSingleton(Replay);
        services.AddSingleton(CommandAdmission);
        services.AddSingleton(HttpAdmission);
        services.AddSingleton(StorageExecution);
        services.AddSingleton(PointCache);
        services.AddSingleton(RequestProbeExecution);
        services.AddSingleton(Membership);
        services.AddSingleton(GrainRouting);
        services.AddSingleton(ServerExecution);
        Core.RegisterBorrowed(services);
    }
}
