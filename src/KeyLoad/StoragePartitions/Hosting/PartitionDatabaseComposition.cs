using KeyLoad.Core;
using KeyLoad.Core.Authorization;
using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Server;

internal static class PartitionDatabaseComposition
{
    internal static void ValidateHostInputs(NodeOptions options, IAuthorizationPolicy authorization,
        TimeProvider clock, global::Microsoft.Extensions.Logging.ILogger<PartitionHost> logger, IGrainFactory grains)
    {
        ArgumentNullException.ThrowIfNull(options) /* KLD0041: BCL null validation accepts the options NodeOptions reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(authorization) /* KLD0041: BCL null validation accepts the authorization IAuthorizationPolicy reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(clock) /* KLD0041: BCL null validation accepts the clock TimeProvider reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(logger) /* KLD0041: BCL null validation accepts the logger ILogger reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(grains) /* KLD0041: BCL null validation accepts the grains IGrainFactory reference through its object parameter. */;
        options.Validate();
    }

    internal static (DatabaseEngine Database, RowVisibilityCache? Visibility) Open(PartitionStores stores,
        ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization,
        TimeProvider clock, CacheMemoryBudget memory)
    {
        var core = runtimeOptions.Core;
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
                AuthorizationExecution.Credential(PartitionStoreProtocol.AdministratorId, PartitionStoreProtocol.AdministratorId,
                    options.AdminKey));
        }
    }
}
