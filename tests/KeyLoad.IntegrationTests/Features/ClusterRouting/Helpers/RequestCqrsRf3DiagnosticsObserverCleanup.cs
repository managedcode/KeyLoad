using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal static class RequestCqrsRf3DiagnosticsObserverCleanup
{
    internal static async Task DisposeAsync(RequestCqrsRf3DiagnosticsSubscriberObserver observer,
        List<Exception> failures, Action<RequestCqrsLifecycleStage>? failureObserver = null)
    {
        try
        { await observer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
            RequestCqrsLifecycleStage.ObserverJoin);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, failureObserver,
            RequestCqrsLifecycleStage.ObserverJoin);
        }
        await observer.RetryFailedCloseAsync(failures, failureObserver).ConfigureAwait(false);
    }
}
