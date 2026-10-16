using KeyLoad.Clustering;
using KeyLoad.Replication;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using KeyLoad.Server.Features.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Validates the server's canonical options before physical ownership.</summary>
internal sealed record ServerRuntimeOptions(
    IOptions<NodeOptions> Node,
    IOptions<ReplicaConfiguration> ReplicaConfiguration,
    IOptions<DueCoordinationOptions> DueCoordination,
    IOptions<NativeDurableJobOptions> DurableJobs,
    IOptions<ReplicaExecutionOptions> ReplicaExecution,
    IOptions<ReplicaTransportOptions> ReplicaTransport,
    IOptions<ReplicaPeerOptions> Peer,
    IOptions<ReplicaReplayLimits> Replay,
    IOptions<CommandAdmissionLimits> CommandAdmission,
    IOptions<HttpAdmissionLimits> HttpAdmission,
    IOptions<McpMemoryLimits> McpMemory,
    IOptions<McpExecutionOptions> McpExecution,
    IOptions<ZoneTreeStorageExecutionOptions> StorageExecution,
    IOptions<ZoneTreePointCacheExecutionOptions> PointCache,
    IOptions<RequestProbeExecutionOptions> RequestProbeExecution,
    IOptions<OrleansMembershipOptions> Membership,
    IOptions<GrainRoutingOptions> GrainRouting,
    IOptions<PhysicalOwnerExecutionOptions> PhysicalOwners,
    IOptions<PartitionMovementExecutionOptions> PartitionMovement,
    IOptions<TenantPlacementExecutionOptions> TenantPlacement,
    IOptions<RemoteDocumentExecutionOptions> RemoteDocuments,
    IOptions<AdminObservationOptions> AdminObservation,
    IOptions<OperationalAnalyticsOptions> OperationalAnalytics,
    IOptions<NativeTextExecutionOptions> NativeText,
    IOptions<NativeAnnExecutionOptions> NativeAnn,
    IOptions<ServerExecutionOptions> ServerExecution,
    IOptions<DatabasePhaseExecutionOptions> DatabasePhaseExecution,
    CoreRuntimeOptions Core)
{
    internal void ValidateBeforePhysicalOwnership()
    {
        _ = Node.Value;
        _ = ReplicaConfiguration.Value;
        _ = DueCoordination.Value;
        _ = DurableJobs.Value;
        _ = ReplicaExecution.Value;
        _ = ReplicaTransport.Value;
        _ = Replay.Value;
        _ = Peer.Value;
        _ = CommandAdmission.Value;
        _ = HttpAdmission.Value;
        _ = McpMemory.Value;
        _ = McpExecution.Value;
        _ = StorageExecution.Value;
        _ = PointCache.Value;
        _ = RequestProbeExecution.Value;
        _ = Membership.Value;
        _ = GrainRouting.Value;
        _ = PhysicalOwners.Value;
        _ = PartitionMovement.Value;
        _ = TenantPlacement.Value;
        _ = RemoteDocuments.Value;
        _ = AdminObservation.Value;
        _ = OperationalAnalytics.Value;
        _ = NativeText.Value;
        _ = NativeAnn.Value;
        _ = ServerExecution.Value;
        _ = DatabasePhaseExecution.Value;
        Core.ValidateBeforePhysicalOwnership();
    }

}
