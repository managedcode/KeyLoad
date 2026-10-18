using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Query.Features.Search;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.Server;

/// <summary>Physical storage owner, independent of migrating Orleans request and database activations.</summary>
internal sealed class PartitionHost : IAsyncDisposable
{
    private PartitionStores stores = null!;
    private readonly PartitionHostOwnership ownership;
    private readonly Lock lifecycle = new();
    private Task? shutdown;

    internal Features.Messaging.RemoteTransferPeerVerificationOwner? TransferVerification
    {
        get; private set;
    } = null!;

    private void RequireTransferVerifierClosed()
        => PartitionHostDisposal.RequireTransferVerifierClosed(TransferVerification);

    private PartitionHost(PartitionHostOwnership ownership) => this.ownership = ownership;

    internal static async Task<PartitionHost> OpenAsync(ServerRuntimeOptions runtimeOptions,
        IAuthorizationPolicy authorization, CommandAdmissionGovernor admission, TimeProvider clock,
        ILogger<PartitionHost> logger, IGrainFactory grains, PartitionHostOwnership? ownership = null)
    {
        var host = new PartitionHost(ownership ?? new());
        await host.InitializeAsync(runtimeOptions, authorization, admission, clock, logger, grains).ConfigureAwait(false);
        return host;
    }

    private async Task InitializeAsync(ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization,
        CommandAdmissionGovernor admission, TimeProvider clock, ILogger<PartitionHost> logger, IGrainFactory grains)
    {
        var options = runtimeOptions.Node.Value;
        PartitionDatabaseComposition.ValidateHostInputs(options, authorization, admission, clock, logger, grains);
        DirectoryPath = Path.GetFullPath(options.DataDirectory);

        try
        {
            OpenDatabase(runtimeOptions, authorization, clock);
            OpenSearch(runtimeOptions, clock);
            Coordinator = new(Database, grains);
            ownership.Host.BeginNativeConstructor();
            AnnMaintenance = new(DirectoryPath, Database, runtimeOptions, clock);
            ownership.Host.NativeConstructorReturned();
        }
        catch (Exception error)
        {
            await CleanupFailedConstructionAsync(error).ConfigureAwait(false);
            throw;
        }
    }

    private void OpenDatabase(ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization, TimeProvider clock)
    {
        stores = new(runtimeOptions.Node, DirectoryPath, runtimeOptions.StorageExecution, runtimeOptions.PointCache,
            clock, null, ownership);
        ownership.Host.BeginOpen();
        ownership.Host.BeginNativeConstructor();
        CacheMemory = new(runtimeOptions.Core.CacheMemory);
        ownership.Host.NativeConstructorReturned();
        ownership.Host.BeginNativeConstructor();
        var opened = PartitionDatabaseComposition.Open(stores, runtimeOptions, authorization, clock, CacheMemory);
        ownership.Host.NativeConstructorReturned();
        Database = opened.Database;
        rowVisibility = opened.Visibility;
        ownership.Host.BeginNativeConstructor();
        TransferVerification = Features.Messaging.RemoteTransferPeerVerificationOwner.Create(Database, runtimeOptions);
        ownership.Host.NativeConstructorReturned();
    }

    private void OpenSearch(ServerRuntimeOptions runtimeOptions, TimeProvider clock)
    {
        new BlobStorageOperations(Database).NormalizeRestoredStore();
        PartitionDatabaseComposition.BootstrapFreshNode(stores, Database, runtimeOptions.Node.Value);
        ownership.Host.BeginNativeConstructor();
        var search = NativeTextHostSearch.Open(Database, DirectoryPath, runtimeOptions, clock);
        ownership.Host.NativeConstructorReturned();
        TextProjection = search.Projection;
        TextMaintenance = search.Explicit;
        OnlineTextMaintenance = search.Online;
    }

    private async Task CleanupFailedConstructionAsync(Exception error)
    {
        await PartitionHostConstructionCleanup.RunAsync(error, OnlineTextMaintenance, TextMaintenance, AnnMaintenance,
            TextProjection, CacheMemory,
            TransferVerification, rowVisibility, stores, RequireTransferVerifierClosed).ConfigureAwait(false);
        ownership.Host.ReleaseReturned();
    }

    /// <summary>Private physical node directory; it is not an atomic partition identity.</summary>
    public string DirectoryPath
    {
        get; private set;
    } = null!;
    internal bool CurrentCapabilityValidated
        => stores.Canonical.Identity.MinimumReaderContract == KeyLoad.Storage.StoreReaderContract.RuntimeJournal;

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
    /// <summary>Routes commands through native Orleans scheduling to silo-local storage services.</summary>
    public SiloCommandCoordinator Coordinator { get; private set; } = null!;

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
        await PartitionHostDisposal.RunAsync(this, () => stores.Dispose(), DisposeRowVisibility).ConfigureAwait(false);
        ownership.Host.ReleaseReturned();
    }

}
