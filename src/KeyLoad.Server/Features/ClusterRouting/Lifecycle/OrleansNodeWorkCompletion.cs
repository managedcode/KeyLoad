using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class OrleansNodeWorkCompletion
{
    internal static async Task JoinAsync(IHost? stopping, NativeRequestWorkOwner requestWork,
        Func<IHost, List<Exception>, Task> stopHost, List<Exception> failures)
    {
        if (stopping is not null)
        {
            await stopHost(stopping, failures).ConfigureAwait(false);
        }
        else
        {
            await ServerFailureObserver.ObserveAsync(requestWork.DrainAsync, failures).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(() => requestWork.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
