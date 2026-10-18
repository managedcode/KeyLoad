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
    IOptions<ReplicaCopyOptions> Copies,
    IOptions<DueCoordinationOptions> DueCoordination,
    IOptions<NativeDurableJobOptions> DurableJobs,
    IOptions<CommandAdmissionLimits> CommandAdmission,
    IOptions<HttpAdmissionLimits> HttpAdmission,
    IOptions<McpMemoryLimits> McpMemory,
    IOptions<McpExecutionOptions> McpExecution,
    IOptions<ZoneTreeStorageExecutionOptions> StorageExecution,
    IOptions<ZoneTreePointCacheExecutionOptions> PointCache,
    IOptions<OrleansMembershipOptions> Membership,
    IOptions<GrainRoutingOptions> GrainRouting,
    IOptions<PhysicalOwnerExecutionOptions> PhysicalOwners,
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
        Node.Value.Validate();
        Copies.Value.Validate();
        _ = DueCoordination.Value;
        _ = DurableJobs.Value;
        _ = CommandAdmission.Value;
        _ = HttpAdmission.Value;
        _ = McpMemory.Value;
        _ = McpExecution.Value;
        _ = StorageExecution.Value;
        _ = PointCache.Value;
        _ = Membership.Value;
        _ = GrainRouting.Value;
        _ = PhysicalOwners.Value;
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
