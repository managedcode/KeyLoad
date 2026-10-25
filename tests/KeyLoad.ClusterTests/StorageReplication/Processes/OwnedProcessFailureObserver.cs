namespace KeyLoad.ClusterTests.StorageReplication.Processes;

internal static class OwnedProcessFailureObserver
{
    internal static void Observe(Action stage, List<Exception> failures)
    {
        var actual = InvokeAsync(stage);
        actual.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).GetAwaiter().GetResult();
        if (actual.Exception is { } error)
        {
            failures.AddRange(error.InnerExceptions);
        }
        if (actual.IsCanceled)
        {
            try
            {
                actual.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException canceled) { failures.Add(canceled); }
        }
    }

    internal static async Task<(bool HasValue, Exception? Value)> CaptureAsync(Task original)
    {
        await original.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (original.Exception is { } error)
        {
            return error.InnerExceptions.Count == 1 ? (true, ((Exception?)(error.InnerExceptions[0]))) : (true, ((Exception?)(error)));
        }
        if (original.IsCanceled)
        {
            try
            {
                await original.ConfigureAwait(false);
            }
            catch (OperationCanceledException canceled) { return (true, ((Exception?)(canceled))); }
        }
        return (false, default(Exception?));
    }

    private static async Task InvokeAsync(Action stage)
    {
        stage();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
