namespace KeyLoad.Replication;

internal static class ReplicaShutdownStage
{
    internal static async Task ObserveAsync(Task stage, List<Exception> failures)
    {
        // A fault never skips the remaining physical owners' terminal drains.
        await stage.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        if (stage.Exception is { } failure)
        {
            failures.AddRange(failure.InnerExceptions);
        }
        else if (stage.IsCanceled)
        {
            failures.Add(new TaskCanceledException(stage));
        }
    }
}
