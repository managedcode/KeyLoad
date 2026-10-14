using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeObserverShutdown
{
    internal static async Task JoinAsync(Task start, Task join, CancellationTokenSource stopping,
        Lazy<ConnectionProbeCapture> connections)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(stopping.Cancel, failures);
        await ServerFailureObserver.ObserveAsync(() => join, failures).ConfigureAwait(false);
        if (connections.IsValueCreated)
        {
            await ServerFailureObserver.ObserveAsync(() => connections.Value.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        try
        {
            stopping.Dispose();
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
