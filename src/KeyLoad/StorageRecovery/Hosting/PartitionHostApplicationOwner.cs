using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.Server;

/// <summary>Silo-local original physical opening and shutdown; native service initialization publishes the one owner.</summary>
internal sealed class PartitionHostApplicationOwner(ServerRuntimeOptions options, IAuthorizationPolicy authorization,
    CommandAdmissionGovernor admission, TimeProvider clock, ILogger<PartitionHost> logger, IGrainFactory grains, OrleansNode? node = null,
    ServerConnectionRegistry? connections = null, IServiceScopeFactory? scopeFactory = null)
    : IPartitionStoragePublication, ILocalDatabaseRuntime, IAsyncDisposable
{
    private const string NativeBorrowersUnjoined = "The original native storage borrowers have not joined; retain the physical owner.";
    private const string Unpublished = "The original physical host opening has not completed successfully.";
    private readonly Lock lifecycle = new();
    private PartitionHostOwnership ownership = new();
    private readonly TaskCompletionSource<Task?> openingAnnounced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PartitionStorageSession? session;
    private IGrainContext? sharedOwner;
    private Task? opening;
    private Task? shutdown;
    private bool published;
    private bool stopping;
    private bool connectionsJoined = connections is null;

    /// <inheritdoc />
    public Task? Opening => Volatile.Read(ref opening);

    internal Task? Closing => Volatile.Read(ref shutdown);

    internal bool CanReleaseRoot => Volatile.Read(ref connectionsJoined)
        && (session?.CanReleaseRoot ?? ownership.CanReleaseRoot)
        && (node?.HasJoinedRequestWork ?? true) && ownership.CanReleaseRoot;

    internal PartitionStorageSession Session
    {
        get
        {
            lock (lifecycle)
            {
                ObjectDisposedException.ThrowIf(stopping, this);
                return published && session is not null ? session : throw new InvalidOperationException(Unpublished);
            }
        }
    }

    /// <inheritdoc />
    public async Task WaitForOpenAsync(CancellationToken cancellationToken)
    {
        var original = await openingAnnounced.Task.WaitAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ObjectDisposedException(nameof(PartitionHostApplicationOwner));
        await original.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            if (!published || !ReferenceEquals(original, opening))
            {
                throw new InvalidOperationException(Unpublished);
            }
        }
    }

    internal PartitionHost Borrow()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            return published && session is not null ? session.Host : throw new InvalidOperationException(Unpublished);
        }
    }

    internal Task OpenAsync()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            if (options.Node.Value.StorageMode == StorageAccessMode.SharedVolume)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, Unpublished);
            }
            opening ??= OpenCoreAsync();
            openingAnnounced.TrySetResult(opening);
            return opening;
        }
    }

    private async Task OpenCoreAsync()
    {
        var physical = new PartitionStorageSession(ownership);
        lock (lifecycle)
        {
            session = physical;
        }
        await physical.OpenAsync(options, authorization, admission, clock, logger, grains, scopeFactory).ConfigureAwait(false);
        lock (lifecycle)
        {
            published = true;
        }
    }

    public async Task<ISharedDatabaseSession> OpenSharedAsync(IGrainContext owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        Task original;
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            if (options.Node.Value.StorageMode != StorageAccessMode.SharedVolume
                || sharedOwner is not null && !ReferenceEquals(sharedOwner, owner))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, Unpublished);
            }
            sharedOwner = owner;
            opening ??= OpenCoreAsync();
            original = opening;
            openingAnnounced.TrySetResult(original);
        }
        try
        {
            await original.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Session;
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            if (session is { } physical)
            {
                await ServerFailureObserver.ObserveAsync(() => CloseSharedAsync(physical), failures).ConfigureAwait(false);
            }
            else if (ownership.CanReleaseRoot)
            {
                ResetShared();
            }
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    public async Task CloseSharedAsync(ISharedDatabaseSession expected)
    {
        PartitionStorageSession physical;
        lock (lifecycle)
        {
            if (!ReferenceEquals(session, expected) || session is null || sharedOwner is null)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, Unpublished);
            }
            physical = session;
        }
        await physical.DisposeAsync().ConfigureAwait(false);
        if (!physical.CanReleaseRoot)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, NativeBorrowersUnjoined);
        }
        ResetShared();
    }

    private void ResetShared()
    {
        lock (lifecycle)
        {
            published = false;
            sharedOwner = null;
            session = null;
            opening = null;
            ownership = new();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (lifecycle)
        {
            stopping = true;
            if (opening is null)
            {
                openingAnnounced.TrySetResult(null);
            }
            return new(shutdown ??= CloseCoreAsync());
        }
    }

    private async Task CloseCoreAsync()
    {
        // Publish the retained close before taking any borrowed node or storage locks.
        await Task.Yield();
        var failures = new List<Exception>();
        if (opening is { } original)
        {
            await ServerFailureObserver.ObserveAsync(() => original, failures).ConfigureAwait(false);
        }
        var joined = await OrleansNodeHostShutdown.JoinApplicationAsync(connections, node, failures).ConfigureAwait(false);
        Volatile.Write(ref connectionsJoined, joined);
        if (session is not null)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
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

}
