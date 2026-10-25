using KeyLoad.Clustering;

namespace KeyLoad.ClusterTests.OrleansExecution.Helpers;

internal static class RequestCqrsClusterDiagnosticsObserverCleanup
{
    internal static async Task DisposeAsync(RequestCqrsClusterDiagnosticsSubscriberObserver observer,
        List<Exception> failures, Action<RequestCqrsLifecycleStage>? failureObserver = null)
    {
        try
        {
            await observer.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
            RequestCqrsLifecycleStage.ObserverJoin);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
            RequestCqrsLifecycleStage.ObserverJoin);
        }
        await observer.RetryFailedCloseAsync(failures, failureObserver).ConfigureAwait(false);
    }
}
