using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
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

    internal Features.Messaging.RemoteTransferPeerVerificationOwner? TransferVerification { get; }

    private void RequireTransferVerifierClosed()
    {
        if (TransferVerification is { IsDisposed: false })
        { throw Errors.Fail(ErrorCode.OwnershipLost, Core.Features.Messaging.RemoteTransferPeerProtocol.Unavailable); }
    }

    /// <summary>Opens the private node directory and recovers durable state before exposing the protocol.</summary>
    /// <param name="runtimeOptions">Shared validated native options for physical ownership and execution.</param>
    /// <param name="authorization">Domain authorization against persisted principals.</param>
    /// <param name="admission">Node data and control admission budgets.</param>
    /// <param name="clock">The system clock shared by protocol and domain evaluation.</param>
    /// <param name="logger">Operational protocol diagnostics without secrets or payloads.</param>
    public PartitionHost(ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization,
        CommandAdmissionGovernor admission, TimeProvider clock, ILogger<ReplicaConsensus> logger)
    {
        var options = runtimeOptions.Node.Value;
        var replicaOptions = runtimeOptions.ReplicaConfiguration;
        var executionOptions = runtimeOptions.ReplicaExecution;
        ValidateConstructorArguments(options, authorization, admission, clock, logger);
        DirectoryPath = Path.GetFullPath(options.DataDirectory);
        Configuration = replicaOptions.Value;
        ApplyProbe = new(Configuration.LocalId);
        stores = new(runtimeOptions.Node, DirectoryPath, runtimeOptions.StorageExecution, runtimeOptions.PointCache, clock, options.RequestCqrsProbe.Enabled || options.MovementFrameObservation.Enabled ? ApplyProbe.StorageObserver : null);
        ReplicaMaterializer? applying = null;
        DurableReplicaLog? openedLog = null;
        ITextProjection? openedText = null;
        NativeTextIncrementalMaintenanceService? openedTextMaintenance = null;
        NativeTextOnlineMaintenanceService? openedOnlineText = null;
        NativeAnnMaintenanceService? openedAnnMaintenance = null;
        ClusterCoordinator? openedCoordinator = null;
        CacheMemoryBudget? openedCacheMemory = null;
        ReplicaConsensus? openedConsensus = null;
        try
        {
            CacheMemory = openedCacheMemory = new(runtimeOptions.Core.CacheMemory);
            Database = OpenCanonicalDatabase(runtimeOptions, authorization, clock);
            TransferVerification = Features.Messaging.RemoteTransferPeerVerificationOwner.Create(Database, runtimeOptions);
            log = openedLog = new(stores.Replica, replicaOptions, canonicalDatabase: Database);
            var snapshots = new ReplicaSnapshotStore(stores.Canonical, log, replicaOptions, executionOptions);
            snapshots.Recover();
            new BlobStorageOperations(Database).NormalizeRestoredStore();
            BootstrapFreshNode(options);
            if (options.MovementFrameObservation.Enabled)
            { ApplyProbe.AttachFrameObservation(new(runtimeOptions.Node, runtimeOptions.ReplicaConfiguration, Database, runtimeOptions.RequestProbeExecution)); }
            var search = NativeTextHostSearch.Open(Database, DirectoryPath, runtimeOptions, clock);
            TextProjection = openedText = search.Projection;
            TextMaintenance = openedTextMaintenance = search.Explicit;
            OnlineTextMaintenance = openedOnlineText = search.Online;
            Materializer = applying = new(Database, log, snapshots, executionOptions, options.RequestCqrsProbe.Enabled || options.MovementFrameObservation.Enabled ? ApplyProbe.Enter : null);
            Consensus = openedConsensus = new(Materializer, replicaOptions, executionOptions, clock, logger);
            Coordinator = openedCoordinator = new(Consensus, Database, admission, clock, executionOptions, runtimeOptions.Core.CommandInbox);
            AnnMaintenance = openedAnnMaintenance = new(DirectoryPath, Database, runtimeOptions, clock);
        }
        catch (Exception error)
        {
            CleanupFailedConstruction(error, openedOnlineText, openedTextMaintenance, openedAnnMaintenance,
                openedCoordinator, openedConsensus, applying, openedText, openedLog, openedCacheMemory);
            throw;
        }
    }

    private void CleanupFailedConstruction(Exception error, NativeTextOnlineMaintenanceService? openedOnlineText,
        NativeTextIncrementalMaintenanceService? openedTextMaintenance, NativeAnnMaintenanceService? openedAnnMaintenance,
        ClusterCoordinator? openedCoordinator, ReplicaConsensus? openedConsensus, ReplicaMaterializer? applying,
        ITextProjection? openedText, DurableReplicaLog? openedLog, CacheMemoryBudget? openedCacheMemory)
    {
        const int FailuresCountValidationBoundary = 1;
        var failures = new List<Exception> { error };
        if (openedOnlineText is not null)
        { ServerFailureObserver.Observe(() => openedOnlineText.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
        if (openedTextMaintenance is not null)
        { ServerFailureObserver.Observe(() => openedTextMaintenance.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
        if (openedAnnMaintenance is not null)
        { ServerFailureObserver.Observe(() => openedAnnMaintenance.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
        if (openedCoordinator is not null)
        { ServerFailureObserver.Observe(() => openedCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
        if (openedConsensus is not null)
        { ServerFailureObserver.Observe(() => openedConsensus.DisposeAsync().AsTask().GetAwaiter().GetResult(), failures); }
        Task? joiningMaterializer = null;
        if (applying is not null)
        {
            joiningMaterializer = applying.DisposeAsync().AsTask();
            ServerFailureObserver.Observe(() => joiningMaterializer.GetAwaiter().GetResult(), failures);
        }
        if (TransferVerification is not null && (joiningMaterializer is null || joiningMaterializer.IsCompletedSuccessfully))
        { ServerFailureObserver.Observe(TransferVerification.Dispose, failures); }
        ServerFailureObserver.Observe(ApplyProbe.CloseFrameObservation, failures);
        if (openedText is not null)
        { ServerFailureObserver.Observe(openedText.Dispose, failures); }
        if (openedLog is not null)
        { ServerFailureObserver.Observe(openedLog.Dispose, failures); }
        if (openedCacheMemory is not null)
        { ServerFailureObserver.Observe(openedCacheMemory.Dispose, failures); }
        ServerFailureObserver.Observe(() => { RequireTransferVerifierClosed(); stores.Dispose(); }, failures);
        if (failures.Count > FailuresCountValidationBoundary)
        { throw new AggregateException(failures); }
    }

    private static void ValidateConstructorArguments(NodeOptions options, IAuthorizationPolicy authorization,
        CommandAdmissionGovernor admission, TimeProvider clock, ILogger<ReplicaConsensus> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        options.Validate();
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
    /// <summary>Single node-owned retention ledger borrowed by derived cache families and movement images.</summary>
    internal CacheMemoryBudget CacheMemory { get; }
    internal NativeAnnMaintenanceService AnnMaintenance { get; }
    internal NativeTextIncrementalMaintenanceService TextMaintenance { get; }
    internal NativeTextOnlineMaintenanceService OnlineTextMaintenance { get; }
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
        var options = runtimeOptions.Node.Value;
        var physicalOwner = options.MembershipAuthority.RemoteDocumentReads
            ? new PhysicalShardRecord(options.PhysicalShardId, Configuration.Incarnation,
                Configuration.VoterIds, PhysicalShardCatalogStartupProtocol.InitialPlacementEpoch) : null;
        var database = new DatabaseEngine(stores.Canonical, authorization, core.DatabaseLimits,
            core.DueWork, core.EventSource, core.Messaging, core.GraphExecution, core.ChangeFeedExecution, core.BlobExecution, core.NativeClaimsExecution, core.TimeSeriesExecution, core.MovementCheckpoints,
            new Features.ClusterRouting.PartitionMovementCheckpointVerifier(runtimeOptions.Node, runtimeOptions.ReplicaConfiguration, runtimeOptions.GrainRouting), clock, physicalOwner);
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
        TransferVerification?.RequireJoined();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => OnlineTextMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => TextMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => AnnMaintenance.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => Coordinator.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => Consensus.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        var applying = Materializer.DisposeAsync().AsTask();
        await ServerFailureObserver.ObserveAsync(() => applying, failures).ConfigureAwait(false);
        if (TransferVerification is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await applying.ConfigureAwait(false);
                TransferVerification.Dispose();
            }, failures).ConfigureAwait(false);
        }
        ServerFailureObserver.Observe(ApplyProbe.CloseFrameObservation, failures);
        var closingLog = CloseLogAsync(applying);
        await ServerFailureObserver.ObserveAsync(() => closingLog, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(TextProjection.Dispose, failures);
        ServerFailureObserver.Observe(CacheMemory.Dispose, failures);
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
        RequireTransferVerifierClosed();
        stores.Dispose();
    }
}
