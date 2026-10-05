using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Captures the first lifecycle snapshot immediately after a real cleanup failure is appended.</summary>
internal static class RequestCqrsLifecycleFailureObserver
{
    internal static void Append(List<Exception> failures, Exception failure,
        Action<RequestCqrsLifecycleStage>? observer, RequestCqrsLifecycleStage stage)
    {
        failures.Add(failure);
        Notify(failures, observer, stage);
    }

    internal static void Observe(Action operation, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? observer, RequestCqrsLifecycleStage stage)
    {
        var previous = failures.Count;
        try
        { ServerFailureObserver.Observe(operation, failures); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        if (failures.Count > previous)
        { Notify(failures, observer, stage); }
    }

    internal static async Task ObserveAsync(Func<Task> operation, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? observer, RequestCqrsLifecycleStage stage)
    {
        var previous = failures.Count;
        try
        { await ServerFailureObserver.ObserveAsync(operation, failures).ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        if (failures.Count > previous)
        { Notify(failures, observer, stage); }
    }

    private static void Notify(List<Exception> failures, Action<RequestCqrsLifecycleStage>? observer,
        RequestCqrsLifecycleStage stage)
    {
        if (observer is null)
        { return; }
        try
        { observer(stage); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }
}
