namespace KeyLoad.IntegrationTests.OrleansExecution.Helpers;

internal sealed class RequestCqrsClusterSubscriberTransitionHooks(
    RequestCqrsClusterDiagnosticsSubscriberObserver observer)
{
    internal void Begin() => observer.BeginCompletionObservation();
    internal void Drain(bool afterOriginalJoin) => observer.DrainReadyTransitions(afterOriginalJoin);
    internal void WriteDiagnostic(int completedWatcherCount)
        => Console.Error.WriteLine(FormattableString.Invariant(
            $"{observer.FormatTransitionObservation()} watcherTasks={completedWatcherCount}/{RequestCqrsClusterProtocol.NodeCount}"));

    internal static void Observe(Action? operation, List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? failureObserver, RequestCqrsLifecycleStage stage)
    {
        if (operation is not null)
        {
            RequestCqrsLifecycleFailureObserver.Observe(operation, failures, failureObserver, stage);
        }
    }
}
