namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureCleanup
{
    internal static async Task CollectFailureAsync(Func<Task> cleanup, List<Exception> failures)
    {
        var pending = Task.Run(cleanup);
        await CollectFailureAsync(pending, failures).ConfigureAwait(false);
    }

    internal static async Task CollectFailureAsync(Task pending, List<Exception> failures)
    {
        await Task.WhenAny(pending).ConfigureAwait(false);
        if (pending.Exception is { } aggregate)
        {
            failures.AddRange(aggregate.Flatten().InnerExceptions);
        }
        else if (pending.IsCanceled)
        {
            failures.Add(new TaskCanceledException(pending));
        }
    }
}
