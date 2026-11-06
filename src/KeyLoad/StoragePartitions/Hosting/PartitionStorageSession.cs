using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server;

/// <summary>Retains the original open files, scoped borrowers and physical request ledger until joined close.</summary>
internal sealed class PartitionStorageSession : ISharedDatabaseSession, IAsyncDisposable
{
    private const string ServicesUnavailable = "This physical host has no runtime service scope.";
    private const string BorrowersUnjoined = "Physical storage borrowers have not joined; retain the open files.";
    private PartitionHost? host;
    private readonly PartitionHostOwnership ownership;
    private RequestWorkOwner? work;
    private AsyncServiceScope? scope;
    private PartitionStorageRequestExecution? requests;
    internal PartitionStorageBatchExecution Batches => batches ?? throw new InvalidOperationException(ServicesUnavailable);
    private PartitionStorageBatchExecution? batches;
    private UserAnalyticsRuntime? analytics;
    private readonly Lock lifecycle = new();
    private Task? closing;
    private PartitionStorageOwnerContext? ownerContext;
    private const string OwnerContextRequired = "Physical storage must open and execute on its Orleans owner context.";

    internal PartitionStorageSession(PartitionHostOwnership ownership)
    {
        this.ownership = ownership;
    }

    internal async Task OpenAsync(ServerRuntimeOptions options, IAuthorizationPolicy authorization,
        TimeProvider clock, ILogger<PartitionHost> logger,
        IGrainFactory grains, IServiceScopeFactory? scopeFactory, PartitionStorageOwnerContext context)
    {
        ownerContext = context;
        if (!(ReferenceEquals(context.Current, context.Context) /* KLD0041: Require the current Orleans context to be the physical session owner instance through CLR reference identity. */))
        {
            throw new InvalidOperationException(OwnerContextRequired);
        }
        host = await PartitionHost.OpenAsync(options, authorization, clock, logger, grains, ownership);
        work = new();
        batches = new(host.Database, work, grains);
        if (scopeFactory is not null)
        {
            scope = scopeFactory.CreateAsyncScope();
            Services.GetRequiredService<PartitionStorageScope>().Bind(host, work);
            analytics = Services.GetRequiredService<UserAnalyticsRuntime>();
            analytics.Open(host);
            requests = new(host.Database, work, Services.GetRequiredService<GrainRequestCodec>(), Services,
                Services.GetRequiredService<TimeProvider>(),
                Services.GetRequiredService<ILogger<PartitionStorageGrainService>>());
        }
    }

    internal IServiceProvider Services => scope?.ServiceProvider ?? throw new InvalidOperationException(ServicesUnavailable);
    internal PartitionStorageOwnerContext OwnerContext => ownerContext ?? throw new InvalidOperationException(OwnerContextRequired);
    internal PartitionHost Host => host ?? throw new InvalidOperationException(ServicesUnavailable);
    internal bool CanReleaseRoot => (work?.IsJoined ?? true) && ownership.CanReleaseRoot
        && (analytics?.CanReleasePhysical ?? true);
    public SiloAddress Address => Services.GetRequiredService<ILocalSiloDetails>().SiloAddress;
    public IBackgroundStorageOperations Background => Services.GetRequiredService<IBackgroundStorageOperations>();

    public SiloStorageCut ReadCut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Host.Database.Store.Read(view => new SiloStorageCut(Host.Database.Store.Identity.Incarnation,
            StorageOperationApply.ReadCanonicalSequence(view, null))
        {
            Partitions = StorageReplicaExecution.ReadPartitionPositions(view, cancellationToken)
        });
    }

    public Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope envelope, string? actorKey,
        IGrainContext owner, CancellationToken cancellationToken)
        => (requests ?? throw new InvalidOperationException(ServicesUnavailable))
            .ExecuteAsync(envelope, actorKey, owner, cancellationToken);

    public Task AbortMaintenanceAsync(GrainReadKind kind, Guid sessionId)
        => (requests ?? throw new InvalidOperationException(ServicesUnavailable)).AbortAsync(kind, sessionId);

    public Task<NodeStatus> ReadStatusAsync(CancellationToken cancellationToken)
        => Services.GetRequiredService<INodeAdministration>().StatusAsync(cancellationToken);

    public Task<AdminNodeSnapshot> ReadDashboardAsync(CancellationToken cancellationToken)
        => Services.GetRequiredService<INodeAdministration>().DashboardAsync(cancellationToken);

    public Task<BackupReceipt> CreateBackupAsync(CancellationToken cancellationToken)
        => Services.GetRequiredService<INodeAdministration>().BackupAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(closing ??= ownerContext is { } context
                ? context.RunAsync(CloseAsync) : CloseAsync());
        }
    }

    private async Task CloseAsync()
    {
        await Task.Yield();
        var failures = new List<Exception>();
        if (work is not null)
        {
            try
            {
                await work.DisposeAsync();
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        await CloseScopedBorrowersAsync(failures);
        if (work is { IsJoined: false })
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, BorrowersUnjoined));
            ServerFailureObserver.ThrowIfAny(failures);
        }
        if (host is not null)
        {
            try
            {
                await host.DisposeAsync();
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task CloseScopedBorrowersAsync(List<Exception> failures)
    {
        if (scope is not null)
        {
            if (analytics is not null)
            {
                try
                {
                    await analytics.DisposeAsync();
                }
                catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
                {
                    failures.Add(failure);
                }
                catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
                {
                    failures.Add(failure);
                }
            }
            if (analytics is { CanReleasePhysical: false } || work is { IsJoined: false })
            {
                failures.Add(Errors.Fail(ErrorCode.OwnershipLost, BorrowersUnjoined));
                ServerFailureObserver.ThrowIfAny(failures);
            }
            try
            {
                await scope.Value.DisposeAsync();
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
    }
}
