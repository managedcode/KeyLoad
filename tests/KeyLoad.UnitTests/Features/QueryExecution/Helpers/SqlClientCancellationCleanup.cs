using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Retains original test/client/handler failures while attempting every real cleanup.</summary>
internal static class SqlClientCancellationCleanup
{
    internal static async Task DrainAsync(Exception? primary, CancellationTokenSource cancellation,
        Task pending, Func<Task?> originalResponse, Task stopped, TimeSpan deadline)
    {
        var failures = new List<Exception>();
        if (primary is not null)
        { failures.Add(primary); }
        await CaptureAsync(cancellation.CancelAsync(), failures);
        await CaptureAsync(pending.WaitAsync(deadline, TimeProvider.System), failures);
        if (originalResponse() is { } original)
        {
            await CaptureAsync(original.WaitAsync(deadline, TimeProvider.System), failures);
            await CaptureAsync(stopped.WaitAsync(deadline, TimeProvider.System), failures);
        }
        if (failures.Count == 1 && primary is null)
        { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1)
        { throw new AggregateException(failures); }
    }

    private static async Task CaptureAsync(Task cleanup, List<Exception> failures)
    {
        await cleanup.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (cleanup.IsCanceled)
        {
            try
            { await cleanup; }
            catch (OperationCanceledException error)
            { failures.Add(error); }
        }
        if (cleanup.Exception is not { } fault)
        { return; }
        foreach (var error in fault.InnerExceptions)
        {
            if (!failures.Contains(error))
            { failures.Add(error); }
        }
    }
}
