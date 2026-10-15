using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;

namespace KeyLoad.Server;

internal static class PartitionDatabaseComposition
{
    internal static void ValidateHostInputs(NodeOptions options, IAuthorizationPolicy authorization,
        CommandAdmissionGovernor admission, TimeProvider clock, global::Microsoft.Extensions.Logging.ILogger<ReplicaConsensus> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        options.Validate();
    }

    internal static (DatabaseEngine Database, RowVisibilityCache? Visibility) Open(PartitionStores stores,
        ReplicaConfiguration configuration, ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization,
        TimeProvider clock, CacheMemoryBudget memory)
    {
        var core = runtimeOptions.Core;
        RuntimeJournalStorePreparation.Prepare(stores);
        var options = runtimeOptions.Node.Value;
        var physicalOwner = options.PhysicalGroupTrust.RemoteDocumentReads
            ? new PhysicalShardRecord(options.PhysicalShardId, configuration.Incarnation,
                configuration.VoterIds, PhysicalShardCatalogStartupProtocol.InitialPlacementEpoch) : null;
        var database = new DatabaseEngine(stores.Canonical, authorization, core.DatabaseLimits,
            core.DueWork, core.EventSource, core.Messaging, core.GraphExecution, core.ChangeFeedExecution, core.BlobExecution, core.NativeClaimsExecution, core.TimeSeriesExecution, core.MovementCheckpoints,
            new Features.ClusterRouting.PartitionMovementCheckpointVerifier(runtimeOptions.Node, runtimeOptions.ReplicaConfiguration, runtimeOptions.GrainRouting), clock, physicalOwner);
        database.ConfigureRuntimeJournal(core.RuntimeJournal);
        return (database, database.ConfigureRowVisibility(memory));
    }

    internal static void BootstrapFreshNode(PartitionStores stores, DatabaseEngine database, NodeOptions options)
    {
        const int EmptyPosition = 0;

        if (stores.Canonical.Position == EmptyPosition)
        {
            database.Bootstrap(new(PartitionStoreProtocol.AdministratorId, PartitionStoreProtocol.AdministratorTenant,
                [new(PartitionStoreProtocol.Wildcard, PartitionStoreProtocol.Wildcard, Capability.All)],
                [PartitionStoreProtocol.Wildcard])
            {
                ClusterAdministrator = true
            },
                DatabaseEngine.Credential(PartitionStoreProtocol.AdministratorId, PartitionStoreProtocol.AdministratorId,
                    options.AdminKey));
        }
    }
}
