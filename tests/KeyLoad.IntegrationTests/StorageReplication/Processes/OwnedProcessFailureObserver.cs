namespace KeyLoad.IntegrationTests.StorageReplication.Processes;

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

    internal static async Task<global::ManagedCode.Communication.Result<Exception?>> CaptureAsync(Task original)
    {
        await original.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (original.Exception is { } error)
        {
            return error.InnerExceptions.Count == 1 ? global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error.InnerExceptions[0]))) : global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(error)));
        }
        if (original.IsCanceled)
        {
            try
            {
                await original.ConfigureAwait(false);
            }
            catch (OperationCanceledException canceled) { return global::ManagedCode.Communication.Result<Exception?>.Succeed(((Exception?)(canceled))); }
        }
        return global::ManagedCode.Communication.Result<Exception?>.FailNotFound(nameof(CaptureAsync));
    }

    private static async Task InvokeAsync(Action stage)
    {
        stage();
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
