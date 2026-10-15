using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Replication;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.Server;

/// <summary>Physical storage owner, independent of migrating Orleans request and database activations.</summary>
internal sealed class PartitionHost : IAsyncDisposable
{
    private PartitionStores stores = null!;
    private readonly PartitionHostOwnership ownership;
    internal Features.ClusterRouting.RequestCqrsCanonicalApplyBridge ApplyProbe
    {
        get; private set;
    } = null!;
    private DurableReplicaLog log = null!;
    private readonly Lock lifecycle = new();
    private Task? shutdown;

    internal Features.Messaging.RemoteTransferPeerVerificationOwner? TransferVerification
    {
        get; private set;
    } = null!;

    private void RequireTransferVerifierClosed()
    {
        if (TransferVerification is { IsDisposed: false })
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, Core.Features.Messaging.RemoteTransferPeerProtocol.Unavailable);
        }
    }

    private PartitionHost(PartitionHostOwnership ownership) => this.ownership = ownership;

    internal static async Task<PartitionHost> OpenAsync(ServerRuntimeOptions runtimeOptions,
        IAuthorizationPolicy authorization, CommandAdmissionGovernor admission, TimeProvider clock,
        ILogger<ReplicaConsensus> logger, PartitionHostOwnership? ownership = null)
    {
        var host = new PartitionHost(ownership ?? new());
        await host.InitializeAsync(runtimeOptions, authorization, admission, clock, logger).ConfigureAwait(false);
        return host;
    }

    private async Task InitializeAsync(ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization,
        CommandAdmissionGovernor admission, TimeProvider clock, ILogger<ReplicaConsensus> logger)
    {
        var options = runtimeOptions.Node.Value;
        PartitionDatabaseComposition.ValidateHostInputs(options, authorization, admission, clock, logger);
        DirectoryPath = Path.GetFullPath(options.DataDirectory);
        Configuration = runtimeOptions.ReplicaConfiguration.Value;
        ApplyProbe = new(Configuration.LocalId);

        try
        {
            OpenDatabase(runtimeOptions, authorization, clock);
            var snapshots = OpenReplicationAndSearch(runtimeOptions, clock);
            OpenExecutionOwners(runtimeOptions, admission, clock, logger, snapshots);
        }
        catch (Exception error)
        {
            await CleanupFailedConstructionAsync(error).ConfigureAwait(false);
            throw;
        }
    }

    private void OpenDatabase(ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization, TimeProvider clock)
    {
        var options = runtimeOptions.Node.Value;
        stores = new(runtimeOptions.Node, DirectoryPath, runtimeOptions.StorageExecution, runtimeOptions.PointCache,
            clock, options.RequestCqrsProbe.Enabled || options.MovementFrameObservation.Enabled ? ApplyProbe.StorageObserver : null, ownership);
        ownership.Host.BeginOpen();
        ownership.Host.BeginNativeConstructor();
        CacheMemory = new(runtimeOptions.Core.CacheMemory);
        ownership.Host.NativeConstructorReturned();
        ownership.Host.BeginNativeConstructor();
        var opened = PartitionDatabaseComposition.Open(stores, Configuration, runtimeOptions, authorization, clock, CacheMemory);
        ownership.Host.NativeConstructorReturned();
        Database = opened.Database;
        rowVisibility = opened.Visibility;
        ownership.Host.BeginNativeConstructor();
        TransferVerification = Features.Messaging.RemoteTransferPeerVerificationOwner.Create(Database, runtimeOptions);
        ownership.Host.NativeConstructorReturned();
    }

    private ReplicaSnapshotStore OpenReplicationAndSearch(ServerRuntimeOptions runtimeOptions, TimeProvider clock)
    {
        var options = runtimeOptions.Node.Value;
        var replicaOptions = runtimeOptions.ReplicaConfiguration;
        var executionOptions = runtimeOptions.ReplicaExecution;
        ownership.Host.BeginNativeConstructor();
        log = new(stores.Replica, replicaOptions,
            faultObserver: options.RequestCqrsProbe.Enabled ? ObserveReplicaLog : null, canonicalDatabase: Database);
        ownership.Host.NativeConstructorReturned();
        var snapshots = new ReplicaSnapshotStore(stores.Canonical, log, replicaOptions, executionOptions);
        snapshots.Recover();
        new BlobStorageOperations(Database).NormalizeRestoredStore();
        PartitionDatabaseComposition.BootstrapFreshNode(stores, Database, options);
        if (options.MovementFrameObservation.Enabled)
        {
            ownership.Host.BeginNativeConstructor();
            ApplyProbe.AttachFrameObservation(new(runtimeOptions.Node, runtimeOptions.ReplicaConfiguration, Database, runtimeOptions.RequestProbeExecution));
            ownership.Host.NativeConstructorReturned();
        }
        ownership.Host.BeginNativeConstructor();
        var search = NativeTextHostSearch.Open(Database, DirectoryPath, runtimeOptions, clock);
        ownership.Host.NativeConstructorReturned();
        TextProjection = search.Projection;
        TextMaintenance = search.Explicit;
        OnlineTextMaintenance = search.Online;
        return snapshots;
    }

    private void OpenExecutionOwners(ServerRuntimeOptions runtimeOptions, CommandAdmissionGovernor admission,
        TimeProvider clock, ILogger<ReplicaConsensus> logger, ReplicaSnapshotStore snapshots)
    {
        var options = runtimeOptions.Node.Value;
        var replicaOptions = runtimeOptions.ReplicaConfiguration;
        var executionOptions = runtimeOptions.ReplicaExecution;
        ownership.Host.BeginNativeConstructor();
        Materializer = new(Database, log, snapshots, executionOptions,
            options.RequestCqrsProbe.Enabled || options.MovementFrameObservation.Enabled ? ApplyProbe.Enter : null);
        ownership.Host.NativeConstructorReturned();
        ownership.Host.BeginNativeConstructor();
        Consensus = new(Materializer, replicaOptions, executionOptions, clock, logger);
        ownership.Host.NativeConstructorReturned();
        ownership.Host.BeginNativeConstructor();
        Coordinator = new(Consensus, Database, admission, clock, executionOptions, runtimeOptions.Core.CommandInbox);
        ownership.Host.NativeConstructorReturned();
        ownership.Host.BeginNativeConstructor();
        AnnMaintenance = new(DirectoryPath, Database, runtimeOptions, clock);
        ownership.Host.NativeConstructorReturned();
    }

    private void ObserveReplicaLog(ReplicaCrashBoundary boundary)
        => PartitionHostReplicaLogObservation.Observe(boundary, log, ApplyProbe);

    private async Task CleanupFailedConstructionAsync(Exception error)
    {
        await PartitionHostConstructionCleanup.RunAsync(error, OnlineTextMaintenance, TextMaintenance, AnnMaintenance,
            Coordinator, Consensus, Materializer, TextProjection, log, CacheMemory,
            TransferVerification, ApplyProbe, rowVisibility, stores, RequireTransferVerifierClosed).ConfigureAwait(false);
        ownership.Host.ReleaseReturned();
    }

    /// <summary>Private physical node directory; it is not an atomic partition identity.</summary>
    public string DirectoryPath
    {
        get; private set;
    } = null!;
    /// <summary>Fixed voter and incarnation scope of this replica group.</summary>
    public ReplicaConfiguration Configuration
    {
        get; private set;
    } = null!;
    internal bool CurrentCapabilityValidated
        => stores.Canonical.Identity.MinimumReaderContract == KeyLoad.Storage.StoreReaderContract.RuntimeJournal
            && stores.Replica.Identity.MinimumReaderContract == KeyLoad.Storage.StoreReaderContract.RuntimeJournal;

    /// <summary>Borrowed canonical engine; only this host disposes its underlying store.</summary>
    public DatabaseEngine Database
    {
        get; private set;
    } = null!;
    /// <summary>Borrowed derived text projection; physical ownership remains in this host.</summary>
    public ITextProjection TextProjection
    {
        get; private set;
    } = null!;
    /// <summary>Single node-owned retention ledger borrowed by derived cache families and movement images.</summary>
    internal CacheMemoryBudget CacheMemory
    {
        get; private set;
    } = null!;
    private RowVisibilityCache? rowVisibility;
    internal NativeAnnMaintenanceService AnnMaintenance
    {
        get; private set;
    } = null!;
    internal NativeTextIncrementalMaintenanceService TextMaintenance
    {
        get; private set;
    } = null!;
    internal NativeTextOnlineMaintenanceService OnlineTextMaintenance
    {
        get; private set;
    } = null!;
    /// <summary>Node-owned ordered apply and checkpoint fencing.</summary>
    public ReplicaMaterializer Materializer
    {
        get; private set;
    } = null!;
    /// <summary>Node-owned protocol endpoint attached by the Orleans Grain Service.</summary>
    public ReplicaConsensus Consensus
    {
        get; private set;
    } = null!;
    /// <summary>Admission starts before silo membership and stops after membership shutdown.</summary>
    public ClusterCoordinator Coordinator
    {
        get; private set;
    } = null!;

    private void DisposeRowVisibility(List<Exception> failures)
        => PartitionHostConstructionCleanup.DisposeRowVisibility(rowVisibility, failures);

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
        await PartitionHostDisposal.RunAsync(this, CloseLogAsync, CloseStoresAsync, DisposeRowVisibility).ConfigureAwait(false);
        ownership.Host.ReleaseReturned();
    }

    private async Task CloseLogAsync(Task applying)
    {
        await applying.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        log.Dispose();
    }

    private async Task CloseStoresAsync(Task closingLog)
    {
        await closingLog.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        RequireTransferVerifierClosed();
        stores.Dispose();
    }
}
