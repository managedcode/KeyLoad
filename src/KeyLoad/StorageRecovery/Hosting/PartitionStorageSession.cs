using KeyLoad.Clustering;
using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Server;

/// <summary>Retains the original open files, scoped borrowers and physical request ledger until joined close.</summary>
internal sealed class PartitionStorageSession : ISharedDatabaseSession, IAsyncDisposable
{
    private const string ServicesUnavailable = "This physical host has no runtime service scope.";
    private const string BorrowersUnjoined = "Physical storage borrowers have not joined; retain the open files.";
    private PartitionHost? host;
    private readonly PartitionHostOwnership ownership;
    private NativeRequestWorkOwner? work;
    private AsyncServiceScope? scope;
    private PartitionStorageRequestExecution? requests;
    private UserAnalyticsRuntime? analytics;
    private readonly Lock lifecycle = new();
    private Task? closing;

    internal PartitionStorageSession(PartitionHostOwnership ownership)
    {
        this.ownership = ownership;
    }

    internal async Task OpenAsync(ServerRuntimeOptions options, IAuthorizationPolicy authorization,
        CommandAdmissionGovernor admission, TimeProvider clock, ILogger<PartitionHost> logger,
        IGrainFactory grains, IServiceScopeFactory? scopeFactory)
    {
        host = await PartitionHost.OpenAsync(options, authorization, admission, clock, logger, grains, ownership)
            .ConfigureAwait(false);
        work = new(options.GrainRouting);
        if (scopeFactory is not null)
        {
            scope = scopeFactory.CreateAsyncScope();
            Services.GetRequiredService<PartitionStorageScope>().Bind(host, work);
            analytics = Services.GetRequiredService<UserAnalyticsRuntime>();
            analytics.Open(host);
            requests = new(host.Database, work, Services.GetRequiredService<GrainRequestCodec>(), Services,
                Services.GetRequiredService<TimeProvider>(), options.GrainRouting,
                Services.GetRequiredService<ILogger<PartitionStorageGrainService>>());
        }
    }

    internal IServiceProvider Services => scope?.ServiceProvider ?? throw new InvalidOperationException(ServicesUnavailable);
    internal PartitionHost Host => host ?? throw new InvalidOperationException(ServicesUnavailable);
    internal bool CanReleaseRoot => (work?.IsJoined ?? true) && ownership.CanReleaseRoot
        && (analytics?.CanReleasePhysical ?? true);
    public SiloAddress Address => Services.GetRequiredService<ILocalSiloDetails>().SiloAddress;
    public IBackgroundStorageOperations Background => Services.GetRequiredService<IBackgroundStorageOperations>();

    public SiloStorageCut ReadCut(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Host.Database.Store.Read(view => new SiloStorageCut(Host.Database.Store.Identity.Incarnation,
            view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
                ? NativeSerialization.Deserialize<long>(bytes) : default(long)));
    }

    public Task<Result<object>> ApplyAsync(Command<DatabaseCommand> operation, CancellationToken cancellationToken)
        => Host.Coordinator.SubmitVerifiedAsync(operation, cancellationToken);

    public Task<GrainOperationReply> ExecuteAsync(string signedRequest, string? actorKey,
        IGrainContext owner, CancellationToken cancellationToken)
        => (requests ?? throw new InvalidOperationException(ServicesUnavailable))
            .ExecuteAsync(signedRequest, actorKey, owner, cancellationToken);

    public Task AbortMaintenanceAsync(GrainReadKind kind, Guid sessionId)
        => (requests ?? throw new InvalidOperationException(ServicesUnavailable)).AbortAsync(kind, sessionId);

    public Task<NodeStatus> ReadStatusAsync(CancellationToken cancellationToken)
        => Services.GetRequiredService<INodeAdministration>().StatusAsync(cancellationToken);

    public Task<NodeAdmissionStatus> ReadAdmissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Services.GetRequiredService<INodeAdministration>().Admission());
    }

    public Task<AdminNodeSnapshot> ReadDashboardAsync(CancellationToken cancellationToken)
        => Services.GetRequiredService<INodeAdministration>().DashboardAsync(cancellationToken);

    public Task<BackupReceipt> CreateBackupAsync(CancellationToken cancellationToken)
        => Services.GetRequiredService<INodeAdministration>().BackupAsync(cancellationToken);

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            return new(closing ??= CloseAsync());
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
                await work.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        await CloseScopedBorrowersAsync(failures).ConfigureAwait(false);
        if (work is { IsJoined: false })
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, BorrowersUnjoined));
            ServerFailureObserver.ThrowIfAny(failures);
        }
        if (host is not null)
        {
            try
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
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
                    await analytics.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
                {
                    failures.Add(failure);
                }
                catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
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
                await scope.Value.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
    }
}
