using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.OrleansHosting;

namespace KeyLoad.Server;

/// <summary>Silo-local original physical opening and shutdown; execution service initialization publishes the one owner.</summary>
internal sealed class PartitionHostApplicationOwner(ServerRuntimeOptions options, IAuthorizationPolicy authorization,
    TimeProvider clock, ILogger<PartitionHost> logger, IGrainFactory grains, OrleansNode? node = null,
    ServerConnectionRegistry? connections = null, IServiceScopeFactory? scopeFactory = null)
    : IPartitionStoragePublication, ILocalDatabaseRuntime, IAsyncDisposable
{
    private const string RuntimeBorrowersUnjoined = "The original execution storage borrowers have not joined; retain the physical owner.";
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
        var original = await openingAnnounced.Task.WaitAsync(cancellationToken)
            ?? throw new ObjectDisposedException(nameof(PartitionHostApplicationOwner));
        await original.WaitAsync(cancellationToken);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            if (!published || !ReferenceEquals(original, opening))
            {
                throw new InvalidOperationException(Unpublished);
            }
        }
    }

    internal PartitionHost Borrow() => Session.Host;

    /// <inheritdoc />
    public Task<GrainOperationReply> ExecuteAsync(GrainRequestEnvelope envelope, string? actorKey,
        IGrainContext context, CancellationToken cancellationToken)
        => options.Node.Value.StorageMode == StorageAccessMode.SharedVolume
            ? grains.GetGrain<IDatabaseCommitGrain>(DatabaseGrainIdentity.For(envelope.Incarnation))
                .ExecuteSharedAsync(envelope, actorKey, cancellationToken)
            : Session.ExecuteAsync(envelope, actorKey, context, cancellationToken);

    internal Task OpenAsync(IGrainContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            if (options.Node.Value.StorageMode == StorageAccessMode.SharedVolume)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, Unpublished);
            }
            opening ??= OpenOnContext(context);
            openingAnnounced.TrySetResult(opening);
            return opening;
        }
    }

    private Task OpenOnContext(IGrainContext context)
    {
        var owner = new PartitionStorageOwnerContext(context);
        return owner.RunAsync(() => OpenCoreAsync(owner));
    }

    private async Task OpenCoreAsync(PartitionStorageOwnerContext context)
    {
        var physical = new PartitionStorageSession(ownership);
        lock (lifecycle)
        {
            session = physical;
        }
        await physical.OpenAsync(options, authorization, clock, logger, grains, scopeFactory, context);
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
            opening ??= OpenOnContext(owner);
            original = opening;
            openingAnnounced.TrySetResult(original);
        }
        try
        {
            await original;
            cancellationToken.ThrowIfCancellationRequested();
            return Session;
        }
        catch (Exception failure)
        {
            var failures = new List<Exception> { failure };
            if (session is { } physical)
            {
                await ServerFailureObserver.ObserveAsync(() => CloseSharedAsync(physical), failures);
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
        await physical.DisposeAsync();
        if (!physical.CanReleaseRoot)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RuntimeBorrowersUnjoined);
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
            await ServerFailureObserver.ObserveAsync(() => original, failures);
        }
        var joined = await OrleansNodeHostShutdown.JoinApplicationAsync(connections, node, failures);
        Volatile.Write(ref connectionsJoined, joined);
        if (session is not null)
        {
            try
            {
                await session.DisposeAsync();
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

}
