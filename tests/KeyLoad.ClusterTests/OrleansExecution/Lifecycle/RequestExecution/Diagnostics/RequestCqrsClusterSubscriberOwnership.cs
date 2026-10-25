using Aspire.Hosting.ApplicationModel;
using KeyLoad.Clustering;

namespace KeyLoad.ClusterTests.OrleansExecution.Helpers;

internal static class RequestCqrsClusterSubscriberOwnership
{
    internal static bool ObserveAdmission(RequestCqrsClusterSubscriberTransitionObservation transitionObservation,
        LogSubscriber subscriber, bool expected, string? actualResourceId,
        string? canonicalNode)
    {
        if (actualResourceId is null)
        {
            return transitionObservation.RecordAdmission(subscriber.Name, expected && subscriber.AnySubscribers);
        }
        if (!string.Equals(subscriber.Name, actualResourceId, StringComparison.Ordinal) || !subscriber.AnySubscribers)
        {
            return false;
        }
        return transitionObservation.RecordAdmission(canonicalNode!, true);
    }

    internal static void CloseLifetime(ref CancellationTokenSource? lifetime,
        List<Exception> failures, Action<RequestCqrsLifecycleStage>? observer,
        ref Action<RequestCqrsLifecycleStage>? failureObserver)
    {
        var active = lifetime;
        if (active is null)
        {
            return;
        }
        try
        {
            active.Dispose();
            lifetime = null;
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverLifetimeDispose);
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverLifetimeDispose);
        }
    }

}
