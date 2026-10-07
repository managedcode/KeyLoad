using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Query.Features.Search;
using KeyLoad.Replication;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.Server;

/// <summary>Physical storage owner, independent of migrating Orleans request and database activations.</summary>
internal sealed class PartitionHost : IAsyncDisposable
{
    private readonly PartitionStores stores;
    internal Features.ClusterRouting.RequestCqrsCanonicalApplyBridge ApplyProbe { get; }
    private readonly DurableReplicaLog log;
    private readonly Lock lifecycle = new();
    private Task? shutdown;

    /// <summary>Opens the private node directory and recovers durable state before exposing the protocol.</summary>
    /// <param name="runtimeOptions">Shared validated native options for physical ownership and execution.</param>
    /// <param name="authorization">Domain authorization against persisted principals.</param>
    /// <param name="admission">Node data and control admission budgets.</param>
    /// <param name="clock">The system clock shared by protocol and domain evaluation.</param>
    /// <param name="logger">Operational protocol diagnostics without secrets or payloads.</param>
    public PartitionHost(ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization,
        CommandAdmissionGovernor admission, TimeProvider clock, ILogger<ReplicaConsensus> logger)
    {
        const string SearchIndexDirectoryName = "search-indexes";
        const int FailuresCountValidationBoundary = 1;

        var options = runtimeOptions.Node.Value;
        var replicaOptions = runtimeOptions.ReplicaConfiguration;
        var executionOptions = runtimeOptions.ReplicaExecution;
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        options.Validate();
        DirectoryPath = Path.GetFullPath(options.DataDirectory);
        Configuration = replicaOptions.Value;
        ApplyProbe = new(Configuration.LocalId);
        stores = new(runtimeOptions.Node, DirectoryPath, runtimeOptions.StorageExecution, runtimeOptions.PointCache, clock, options.RequestCqrsProbe.Enabled ? ApplyProbe.StorageObserver : null);
        ReplicaMaterializer? applying = null;
        DurableReplicaLog? openedLog = null;
        ITextProjection? openedText = null;
        try
        {
            Database = OpenCanonicalDatabase(runtimeOptions, authorization, clock);
            log = openedLog = new(stores.Replica, replicaOptions, canonicalDatabase: Database);
            var snapshots = new ReplicaSnapshotStore(stores.Canonical, log, replicaOptions, executionOptions);
            snapshots.Recover();
            new BlobStorageOperations(Database).NormalizeRestoredStore();
            BootstrapFreshNode(options);
            TextProjection = openedText = new NativeTextProjection(Path.Combine(DirectoryPath, SearchIndexDirectoryName),
                runtimeOptions.Core.DatabaseLimits, Database.Store.Identity.NodeId, runtimeOptions.NativeText);
            Materializer = applying = new(Database, log, snapshots, executionOptions, options.RequestCqrsProbe.Enabled ? ApplyProbe.Enter : null);
            Consensus = new(Materializer, replicaOptions, executionOptions, clock, logger);
            Coordinator = new(Consensus, Database, admission, clock, executionOptions, runtimeOptions.Core.CommandInbox);
        }
        catch (Exception error)
        {
            var failures = new List<Exception> { error };
            if (applying is not null)
            { ServerFailureObserver.Observe(() => applying.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
            if (openedText is not null)
            { ServerFailureObserver.Observe(openedText.Dispose, failures); }
            if (openedLog is not null)
            { ServerFailureObserver.Observe(() => openedLog.Dispose(), failures); }
            ServerFailureObserver.Observe(() => stores.Dispose(), failures);
            if (failures.Count > FailuresCountValidationBoundary)
            { throw new AggregateException(failures); }
            throw;
        }
    }

    /// <summary>Private physical node directory; it is not an atomic partition identity.</summary>
    public string DirectoryPath { get; }
    /// <summary>Fixed voter and incarnation scope of this replica group.</summary>
    public ReplicaConfiguration Configuration { get; }
    internal bool CurrentCapabilityValidated
        => stores.Canonical.Identity.MinimumReaderContract == KeyLoad.Storage.StoreReaderContract.RuntimeJournal
            && stores.Replica.Identity.MinimumReaderContract == KeyLoad.Storage.StoreReaderContract.RuntimeJournal;

    /// <summary>Borrowed canonical engine; only this host disposes its underlying store.</summary>
    public DatabaseEngine Database { get; }
    /// <summary>Borrowed derived text projection; physical ownership remains in this host.</summary>
    public ITextProjection TextProjection { get; }
    /// <summary>Node-owned ordered apply and checkpoint fencing.</summary>
    public ReplicaMaterializer Materializer { get; }
    /// <summary>Node-owned protocol endpoint attached by the Orleans Grain Service.</summary>
    public ReplicaConsensus Consensus { get; }
    /// <summary>Admission starts before silo membership and stops after membership shutdown.</summary>
    public ClusterCoordinator Coordinator { get; }

    private DatabaseEngine OpenCanonicalDatabase(ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization, TimeProvider clock)
    {
        var core = runtimeOptions.Core;
        RuntimeJournalStorePreparation.Prepare(stores);
        var database = new DatabaseEngine(stores.Canonical, authorization, core.DatabaseLimits,
            core.DueWork, core.EventSource, core.Messaging, core.GraphExecution, core.ChangeFeedExecution, core.BlobExecution, core.NativeClaimsExecution, core.TimeSeriesExecution, clock);
        database.ConfigureRuntimeJournal(core.RuntimeJournal);
        return database;
    }

    private void BootstrapFreshNode(NodeOptions options)
    {
        const int EmptyPosition = 0;

        if (stores.Canonical.Position == EmptyPosition)
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
        ServerFailureObserver.Observe(TextProjection.Dispose, failures);
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
