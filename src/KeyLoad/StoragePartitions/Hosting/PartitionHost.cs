using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
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

    private PartitionHost(PartitionHostOwnership ownership) => this.ownership = ownership;

    internal static async Task<PartitionHost> OpenAsync(ServerRuntimeOptions runtimeOptions,
        IAuthorizationPolicy authorization, CommandAdmissionGovernor admission, TimeProvider clock,
        ILogger<PartitionHost> logger, IGrainFactory grains, PartitionHostOwnership? ownership = null)
    {
        var host = new PartitionHost(ownership ?? new());
        await host.InitializeAsync(runtimeOptions, authorization, admission, clock, logger, grains);
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
            Coordinator = runtimeOptions.Node.Value.StorageMode == StorageAccessMode.SharedVolume
                ? new LocalStorageCommitCoordinator(Database) : new SiloCommandCoordinator(Database, grains);
            ownership.Host.BeginConstructor();
            AnnMaintenance = new(DirectoryPath, Database, runtimeOptions, clock);
            ownership.Host.ConstructorReturned();
        }
        catch (Exception error)
        {
            await CleanupFailedConstructionAsync(error);
            throw;
        }
    }

    private void OpenDatabase(ServerRuntimeOptions runtimeOptions, IAuthorizationPolicy authorization, TimeProvider clock)
    {
        stores = new(runtimeOptions.Node, DirectoryPath, runtimeOptions.StorageExecution, runtimeOptions.PointCache,
            clock, null, ownership);
        ownership.Host.BeginOpen();
        ownership.Host.BeginConstructor();
        CacheMemory = new(runtimeOptions.Core.CacheMemory);
        ownership.Host.ConstructorReturned();
        ownership.Host.BeginConstructor();
        var opened = PartitionDatabaseComposition.Open(stores, runtimeOptions, authorization, clock, CacheMemory);
        ownership.Host.ConstructorReturned();
        Database = opened.Database;
        rowVisibility = opened.Visibility;
    }

    private void OpenSearch(ServerRuntimeOptions runtimeOptions, TimeProvider clock)
    {
        PartitionDatabaseComposition.BootstrapFreshNode(stores, Database, runtimeOptions.Node.Value);
        ownership.Host.BeginConstructor();
        var search = TextHostSearch.Open(Database, DirectoryPath, runtimeOptions, clock);
        ownership.Host.ConstructorReturned();
        TextProjection = search.Projection;
        TextMaintenance = search.Explicit;
        OnlineTextMaintenance = search.Online;
    }

    private async Task CleanupFailedConstructionAsync(Exception error)
    {
        await PartitionHostConstructionCleanup.RunAsync(error, OnlineTextMaintenance, TextMaintenance, AnnMaintenance,
            TextProjection, CacheMemory, rowVisibility, stores);
        ownership.Host.ReleaseReturned();
    }

    /// <summary>Private physical node directory; it is not an atomic partition identity.</summary>
    public string DirectoryPath
    {
        get; private set;
    } = null!;
    internal void SweepMaintenance() => stores.Canonical.SweepMaintenance();

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
    /// <summary>Single node-owned retention ledger borrowed by derived cache families.</summary>
    internal CacheMemoryBudget CacheMemory
    {
        get; private set;
    } = null!;
    private RowVisibilityCache? rowVisibility;
    internal AnnMaintenanceService AnnMaintenance
    {
        get; private set;
    } = null!;
    internal TextIncrementalMaintenanceService TextMaintenance
    {
        get; private set;
    } = null!;
    internal TextOnlineMaintenanceService OnlineTextMaintenance
    {
        get; private set;
    } = null!;
    /// <summary>Routes commands through execution Orleans scheduling to silo-local storage services.</summary>
    public ICommitCoordinator Coordinator { get; private set; } = null!;

    private void DisposeRowVisibility(List<Exception> failures)
        => PartitionHostConstructionCleanup.DisposeRowVisibility(rowVisibility, failures);

    /// <summary>Joins derived storage users before releasing the canonical store, once.</summary>
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
        await ServerFailureObserver.ObserveAsync(() => PartitionHostDisposal.RunAsync(this, DisposeRowVisibility, failures), failures);
        try
        {
            stores.Dispose();
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        ownership.Host.ReleaseReturned();
    }

}
