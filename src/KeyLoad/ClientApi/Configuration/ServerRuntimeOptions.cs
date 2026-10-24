using KeyLoad.Clustering;
using KeyLoad.Server.Search;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Validates the server's canonical options before physical ownership.</summary>
internal sealed record ServerRuntimeOptions(
    IOptions<NodeOptions> Node,
    IOptions<ReplicaCopyOptions> Copies,
    IOptions<DueCoordinationOptions> DueCoordination,
    IOptions<RuntimeDurableJobOptions> DurableJobs,
    IOptions<McpExecutionOptions> McpExecution,
    IOptions<ZoneTreeStorageExecutionOptions> StorageExecution,
    IOptions<ZoneTreePointCacheExecutionOptions> PointCache,
    IOptions<GrainRoutingOptions> GrainRouting,
    IOptions<AdminObservationOptions> AdminObservation,
    IOptions<TextExecutionOptions> RuntimeText,
    IOptions<AnnExecutionOptions> RuntimeAnn,
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
        _ = McpExecution.Value;
        _ = StorageExecution.Value;
        _ = PointCache.Value;
        _ = GrainRouting.Value;
        _ = AdminObservation.Value;
        _ = RuntimeText.Value;
        _ = RuntimeAnn.Value;
        _ = ServerExecution.Value;
        _ = DatabasePhaseExecution.Value;
        Core.ValidateBeforePhysicalOwnership();
    }

}
