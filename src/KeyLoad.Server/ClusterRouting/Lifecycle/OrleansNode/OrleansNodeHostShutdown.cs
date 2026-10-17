using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Joins borrowed node producers while the application-owned native silo remains live.</summary>
internal static class OrleansNodeHostShutdown
{
    internal static async Task JoinAsync(NativeRequestWorkOwner requestWork, IAsyncDisposable? administration,
        RequestCqrsProbeNativeRelay? relay, RequestCqrsProbeObserver? observer, List<Exception> failures)
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
        if (relay is not null)
        {
            await ServerFailureObserver.ObserveAsync(relay.StopAsync, failures).ConfigureAwait(false);
        }
        if (observer is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => observer.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
    }
}
