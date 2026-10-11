using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;

namespace KeyLoad.Server;

internal static class PartitionDatabaseComposition
{
    internal static (DatabaseEngine Database, RowVisibilityCache? Visibility) Open(PartitionStores stores,
        ReplicaConfiguration configuration, ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization,
        TimeProvider clock, CacheMemoryBudget memory)
    {
        var core = runtimeOptions.Core;
        RuntimeJournalStorePreparation.Prepare(stores);
        var options = runtimeOptions.Node.Value;
        var physicalOwner = options.MembershipAuthority.RemoteDocumentReads
            ? new PhysicalShardRecord(options.PhysicalShardId, configuration.Incarnation,
                configuration.VoterIds, PhysicalShardCatalogStartupProtocol.InitialPlacementEpoch) : null;
        var database = new DatabaseEngine(stores.Canonical, authorization, core.DatabaseLimits,
            core.DueWork, core.EventSource, core.Messaging, core.GraphExecution, core.ChangeFeedExecution, core.BlobExecution, core.NativeClaimsExecution, core.TimeSeriesExecution, core.MovementCheckpoints,
            new Features.ClusterRouting.PartitionMovementCheckpointVerifier(runtimeOptions.Node, runtimeOptions.ReplicaConfiguration, runtimeOptions.GrainRouting), clock, physicalOwner);
        database.ConfigureRuntimeJournal(core.RuntimeJournal);
        return (database, database.ConfigureRowVisibility(memory));
    }
}
