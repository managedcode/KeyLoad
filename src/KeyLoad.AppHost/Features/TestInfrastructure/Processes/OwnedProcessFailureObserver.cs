namespace KeyLoad.AppHost.Features.TestInfrastructure.Processes;

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
            { actual.GetAwaiter().GetResult(); }
            catch (OperationCanceledException canceled) { failures.Add(canceled); }
        }
    }

    internal static async Task<Exception?> CaptureAsync(Task original)
    {
        const int SingleFailureCount = 1;
        const int IndexValue = 0;

        await original.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (original.Exception is { } error)
        {
            return error.InnerExceptions.Count == SingleFailureCount ? error.InnerExceptions[IndexValue] : error;
        }
        if (original.IsCanceled)
        {
            try
            { await original.ConfigureAwait(false); }
            catch (OperationCanceledException canceled) { return canceled; }
        }
        return null;
    }

    private static async Task InvokeAsync(Action stage)
    {
        stage();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
