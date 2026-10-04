using System.Runtime.ExceptionServices;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class AspireOwnedTaskJoin
{
    private const string MultipleFailures = "Aspire execution and owned task cleanup failed.";

    internal static async Task<Task> FirstFailureOrAllAsync(Task[] tasks)
    {
        var pending = tasks.ToList();
        while (pending.Count != 0)
        {
            var settled = await Task.WhenAny(pending).ConfigureAwait(false);
            if (!settled.IsCompletedSuccessfully)
            {
                return settled;
            }
            pending.Remove(settled);
        }
        return Task.CompletedTask;
    }

    internal static async Task CompleteAsync(CancellationTokenSource lifetime, Task[] tasks, Task primary)
    {
        await primary.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        var failures = new List<Exception>();
        await RetainAsync(primary, failures, includeCancellation: true).ConfigureAwait(false);
        var cancellation = lifetime.CancelAsync();
        await cancellation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await RetainAsync(cancellation, failures, includeCancellation: true).ConfigureAwait(false);
        await Task.WhenAll(tasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        foreach (var task in tasks.Where(task => !ReferenceEquals(task, primary)))
        {
            await RetainAsync(task, failures, includeCancellation: false).ConfigureAwait(false);
        }
        ThrowFailures(failures);
    }

    private static async Task RetainAsync(Task task, List<Exception> failures, bool includeCancellation)
    {
        if (task.IsFaulted)
        {
            failures.AddRange(task.Exception!.InnerExceptions);
        }
        else if (includeCancellation && task.IsCanceled)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException error)
            {
                failures.Add(error);
            }
        }
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(MultipleFailures, failures);
        }
    }
}
