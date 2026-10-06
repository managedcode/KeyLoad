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
        ServerFailureObserver.Observe(operation, failures);
        if (failures.Count > previous)
        { Notify(failures, observer, stage); }
    }

    internal static async Task ObserveAsync(Func<Task> operation, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? observer, RequestCqrsLifecycleStage stage)
    {
        var previous = failures.Count;
        await ServerFailureObserver.ObserveAsync(operation, failures).ConfigureAwait(false);
        if (failures.Count > previous)
        { Notify(failures, observer, stage); }
    }

    private static void Notify(List<Exception> failures, Action<RequestCqrsLifecycleStage>? observer,
        RequestCqrsLifecycleStage stage)
    {
        if (observer is null)
        { return; }
        ServerFailureObserver.Observe(() => observer(stage), failures);
    }
}
