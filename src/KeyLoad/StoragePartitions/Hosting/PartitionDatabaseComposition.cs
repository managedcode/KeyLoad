using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Server;

internal static class PartitionDatabaseComposition
{
    internal static void ValidateHostInputs(NodeOptions options, IAuthorizationPolicy authorization,
        CommandAdmissionGovernor admission, TimeProvider clock, global::Microsoft.Extensions.Logging.ILogger<PartitionHost> logger, IGrainFactory grains)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(grains);
        options.Validate();
    }

    internal static (DatabaseEngine Database, RowVisibilityCache? Visibility) Open(PartitionStores stores,
        ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization,
        TimeProvider clock, CacheMemoryBudget memory)
    {
        var core = runtimeOptions.Core;
        RuntimeJournalStorePreparation.Prepare(stores);
        var database = new DatabaseEngine(stores.Canonical, authorization, core.DatabaseLimits,
            core.DueWork, core.EventSource, core.Messaging, core.GraphExecution, core.ChangeFeedExecution, core.RuntimeClaimsExecution, core.TimeSeriesExecution, clock);
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
