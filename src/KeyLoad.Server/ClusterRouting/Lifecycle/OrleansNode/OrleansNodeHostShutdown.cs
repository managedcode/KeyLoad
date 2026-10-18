using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Joins borrowed node producers while the application-owned native silo remains live.</summary>
internal static class OrleansNodeHostShutdown
{
    private const string ApplicationBorrowersUnjoined = "Application storage borrowers have not joined; retain the physical owner.";

    internal static async Task<bool> JoinApplicationAsync(ServerConnectionRegistry? connections, OrleansNode? node,
        List<Exception> failures)
    {
        var joined = connections is null;
        if (connections is not null)
        {
            var before = failures.Count;
            await ServerFailureObserver.ObserveAsync(() => connections.ShutdownAsync(CancellationToken.None), failures)
                .ConfigureAwait(false);
            joined = before == failures.Count;
        }
        if (node is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => node.StopAsync(CancellationToken.None), failures).ConfigureAwait(false);
        }
        if (!joined || node is { HasJoinedRequestWork: false })
        {
            failures.Add(Errors.Fail(ErrorCode.OwnershipLost, ApplicationBorrowersUnjoined));
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return joined;
    }

    internal static async Task JoinAsync(NativeRequestWorkOwner requestWork, IAsyncDisposable? administration,
        List<Exception> failures)
    {
        Task? drain = null;
        ServerFailureObserver.Observe(() => drain = requestWork.DrainAsync(), failures);
        if (administration is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => administration.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        if (drain is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => drain, failures).ConfigureAwait(false);
        }
    }
}
