using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Retains the original post-active catalog and physical-registration bootstrap publications.</summary>
internal sealed class OrleansNodeDatabaseBootstrap
{
    private PhysicalShardCatalogStartup? catalog;
    private PhysicalOwnerRegistrationRuntime? registration;

    internal PhysicalShardCatalogStartup? Catalog => Volatile.Read(ref catalog);
    internal PhysicalOwnerRegistrationRuntime? OwnerRegistration => Volatile.Read(ref registration);

    internal async Task StartAsync(Task registered, OrleansNode node, PartitionHost partition,
        IOptions<NodeOptions> nodeOptions, ServerRuntimeOptions runtimeOptions, IServiceProvider applicationServices,
        TimeProvider runtimeClock, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        await registered.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await NativeReplicaCommandExecution.StartAsync(partition.Coordinator, node.RequestWork, partition.Database, cancellationToken).ConfigureAwait(false);
        var options = nodeOptions.Value;
        if (options.PhysicalGroupTrust.Role == PhysicalGroupTrustRole.Destination
            && !options.PhysicalGroupTrust.RemoteDocumentReads)
        {
            return;
        }
        var original = new PhysicalShardCatalogStartup(node, partition, nodeOptions,
            runtimeClock, runtimeOptions.GrainRouting, runtimeOptions.McpExecution,
            loggerFactory.CreateLogger<PhysicalShardCatalogStartup>());
        Volatile.Write(ref catalog, original);
        await original.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await RuntimeJournalStartup.InitializeConfiguredAsync(applicationServices, partition,
            original.AuthenticateBootstrapAdministratorAsync, options, runtimeOptions, cancellationToken).ConfigureAwait(false);
        PhysicalOwnerRegistrationRuntime.Attach(ref registration, node, partition,
            runtimeOptions, applicationServices, runtimeClock);
    }

    internal void OpenAdmission(Lock lifecycle, ref Task? shutdown, PhysicalShardCatalogStartup current,
        CancellationToken cancellationToken)
        => OrleansNodeCatalogAdmission.Open(lifecycle, ref shutdown, ref catalog, current, cancellationToken);
}
