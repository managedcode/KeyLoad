using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server;

/// <summary>Silo-local original physical opening and shutdown; native service initialization publishes the one owner.</summary>
internal sealed class PartitionHostApplicationOwner(ServerRuntimeOptions options, IAuthorizationPolicy authorization,
    CommandAdmissionGovernor admission, TimeProvider clock, ILogger<PartitionHost> logger, IGrainFactory grains, OrleansNode? node = null,
    ServerConnectionRegistry? connections = null,
    UserAnalyticsRuntime? userAnalytics = null)
    : IPartitionStoragePublication, IAsyncDisposable
{
    private const string NativeBorrowersUnjoined = "The original native storage borrowers have not joined; retain the physical owner.";
    private const string Unpublished = "The original physical host opening has not completed successfully.";
    private readonly Lock lifecycle = new();
    private readonly PartitionHostOwnership ownership = new();
    private readonly TaskCompletionSource<Task?> openingAnnounced = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PartitionHost? host;
    private Task? opening;
    private Task? shutdown;
    private bool published;
    private bool stopping;
    private bool connectionsJoined = connections is null;

    /// <inheritdoc />
    public Task? Opening
    {
        get
        {
            lock (lifecycle)
            {
                return opening;
            }
        }
    }

    internal Task? Closing
    {
        get
        {
            lock (lifecycle)
            {
                return shutdown;
            }
        }
    }

    internal bool CanReleaseRoot => Volatile.Read(ref connectionsJoined)
        && (userAnalytics?.CanReleasePhysical ?? true)
        && (node?.HasJoinedRequestWork ?? true) && ownership.CanReleaseRoot;

    /// <inheritdoc />
    public async Task WaitForOpenAsync(CancellationToken cancellationToken)
    {
        var original = await openingAnnounced.Task.WaitAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new ObjectDisposedException(nameof(PartitionHostApplicationOwner));
        await original.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
        }
    }

    internal PartitionHost Borrow()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            return published && host is not null ? host : throw new InvalidOperationException(Unpublished);
        }
    }

    internal Task OpenAsync()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(stopping, this);
            opening ??= OpenCoreAsync();
            openingAnnounced.TrySetResult(opening);
            return opening;
        }
    }

    private async Task OpenCoreAsync()
    {
        var opened = await PartitionHost.OpenAsync(options, authorization, admission, clock, logger, grains, ownership)
            .ConfigureAwait(false);
        lock (lifecycle)
        {
            host = opened;
        }
        userAnalytics?.Open(opened);
        lock (lifecycle)
        {
            published = true;
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
        if (connections is not null)
        {
            var prior = failures.Count;
            await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures)
                .ConfigureAwait(false);
            Volatile.Write(ref connectionsJoined, failures.Count == prior);
        }
        if (node is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => node.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        }
        if (userAnalytics is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => userAnalytics.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (!(userAnalytics?.CanReleasePhysical ?? true)
            || !Volatile.Read(ref connectionsJoined)
            || node is { HasJoinedRequestWork: false })
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, NativeBorrowersUnjoined));
            ServerFailureObserver.ThrowIfAny(failures);
        }
        if (host is not null)
        {
            try
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception failure) when (KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

}
