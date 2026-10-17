namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class RequestCqrsCohortObservationOwner
{
    internal static async Task RunAsync(RequestCqrsCohortRuntimeFixture runtime,
        Func<RequestCqrsCohortScenario, CancellationToken, Task> flow, CancellationToken token)
    {
        var scenario = await RequestCqrsCohortScenario.StartAsync(runtime, token);
        var failures = new List<Exception>();
        try
        {
            await RequestCqrsCohortCleanup.CaptureAsync(() => flow(scenario, token), failures);
        }
        finally
        {
            var cleanup = scenario.DisposeAsync().AsTask();
            await cleanup.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (cleanup.Exception is { } originalCleanup)
            {
                failures.AddRange(originalCleanup.InnerExceptions);
            }
            else if (cleanup.IsCanceled)
            {
                failures.Add(new TaskCanceledException(cleanup));
            }
        }
        RequestCqrsCohortCleanup.ThrowIfAny(failures);
    }
}
