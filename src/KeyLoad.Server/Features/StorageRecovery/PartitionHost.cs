using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Replication;

namespace KeyLoad.Server;

/// <summary>Physical storage owner, independent of migrating Orleans request and database activations.</summary>
internal sealed class PartitionHost : IAsyncDisposable
{
    private readonly PartitionStores stores;
    private readonly DurableReplicaLog log;
    private readonly object lifecycle = new();
    private Task? shutdown;

    /// <summary>Opens the private node directory and recovers durable state before exposing the protocol.</summary>
    /// <param name="options">Validated fixed-voter and physical node configuration.</param>
    /// <param name="authorization">Domain authorization against persisted principals.</param>
    /// <param name="admission">Node data and control admission budgets.</param>
    /// <param name="clock">The system clock shared by protocol and domain evaluation.</param>
    /// <param name="logger">Operational protocol diagnostics without secrets or payloads.</param>
    public PartitionHost(NodeOptions options, IAuthorizationPolicy authorization, CommandAdmissionGovernor admission,
        TimeProvider clock, ILogger<ReplicaConsensus> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        options.Validate();
        DirectoryPath = Path.GetFullPath(options.DataDirectory);
        Configuration = options.CreateReplicaConfiguration(DirectoryPath);
        stores = new(options, DirectoryPath);
        ReplicaMaterializer? applying = null;
        DurableReplicaLog? openedLog = null;
        try
        {
            Database = new(stores.Canonical, authorization);
            log = openedLog = new(stores.Replica, Configuration, canonicalDatabase: Database);
            var snapshots = new ReplicaSnapshotStore(stores.Canonical, log, Configuration);
            snapshots.Recover();
            new BlobStorageOperations(Database).NormalizeRestoredStore();
            BootstrapFreshNode(options);
            Materializer = applying = new(Database, log, snapshots);
            Consensus = new(Materializer, Configuration, clock, logger);
            Coordinator = new(Consensus, Database, admission, clock);
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            if (applying is not null)
            { ServerFailureObserver.Observe(() => applying.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
            if (openedLog is not null)
            { ServerFailureObserver.Observe(() => openedLog.Dispose(), failures); }
            ServerFailureObserver.Observe(() => stores.Dispose(), failures);
            if (failures.Count > 1)
            { throw new AggregateException(failures); }
            throw;
        }
    }

    /// <summary>Private physical node directory; it is not an atomic partition identity.</summary>
    public string DirectoryPath { get; }
    /// <summary>Fixed voter and incarnation scope of this replica group.</summary>
    public ReplicaConfiguration Configuration { get; }
    /// <summary>Borrowed canonical engine; only this host disposes its underlying store.</summary>
    public DatabaseEngine Database { get; }
    /// <summary>Node-owned ordered apply and checkpoint fencing.</summary>
    public ReplicaMaterializer Materializer { get; }
    /// <summary>Node-owned protocol endpoint attached by the Orleans Grain Service.</summary>
    public ReplicaConsensus Consensus { get; }
    /// <summary>Admission starts before silo membership and stops after membership shutdown.</summary>
    public ClusterCoordinator Coordinator { get; }

    private void BootstrapFreshNode(NodeOptions options)
    {
        if (stores.Canonical.Position == 0)
        {
            Database.Bootstrap(new(PartitionStoreProtocol.AdministratorId, PartitionStoreProtocol.AdministratorTenant,
                [new(PartitionStoreProtocol.Wildcard, PartitionStoreProtocol.Wildcard, Capability.All)],
                [PartitionStoreProtocol.Wildcard])
            { ClusterAdministrator = true },
                DatabaseEngine.Credential(PartitionStoreProtocol.AdministratorId, PartitionStoreProtocol.AdministratorId,
                    options.AdminKey));
        }
        ClusterPrincipalPolicy.Initialize(Database);
    }

    /// <summary>Drains protocol and apply before releasing either physical store, once.</summary>
    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            shutdown ??= DisposeCoreAsync();
            return new(shutdown);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => Coordinator.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => Consensus.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        var applying = Materializer.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => applying, failures).ConfigureAwait(false);
        var closingLog = CloseLogAsync(applying);
        await ServerFailureObserver.ObserveAsync(() => closingLog, failures).ConfigureAwait(false);
        var closingStores = CloseStoresAsync(closingLog);
        await ServerFailureObserver.ObserveAsync(() => closingStores, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task CloseLogAsync(Task applying)
    {
        await applying.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        log.Dispose();
    }

    private async Task CloseStoresAsync(Task closingLog)
    {
        await closingLog.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        stores.Dispose();
    }
}
