namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ReportFileWriteSettlement
{
    private static async Task AwaitCanceledOrCompletedAsync(Task? operation)
    {
        if (operation is null)
        {
            return;
        }

        try
        {
            await operation;
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal static async Task CleanupAsync(RealFileGrowthObserver? observer, Task? growth, Task? writing,
        CancellationTokenSource observation, CancellationTokenSource cancellation, string directory)
    {
        var failures = new List<Exception>();
        await ObserveCleanupTasksAsync(
            [InvokeCleanupAsync(observation.CancelAsync), InvokeCleanupAsync(cancellation.CancelAsync)], failures);
        if (observer is not null)
        {
            await ObserveCleanupTasksAsync([InvokeCleanupAsync(async () => await observer.DisposeAsync())], failures);
        }
        await ObserveCleanupTasksAsync(
            [InvokeCleanupAsync(() => AwaitCanceledOrCompletedAsync(growth)),
                InvokeCleanupAsync(() => AwaitCanceledOrCompletedAsync(writing))], failures);
        await ObserveCleanupTasksAsync(
            [InvokeCleanupAsync(() =>
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }

                return Task.CompletedTask;
            })], failures);
        if (failures.Count > 0)
        {
            throw new AggregateException("Report cancellation cleanup failed.", failures);
        }
    }

    private static async Task ObserveCleanupTasksAsync(IReadOnlyCollection<Task> cleanupTasks, List<Exception> failures)
    {
        await Task.WhenAll(cleanupTasks).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        foreach (var cleanupTask in cleanupTasks)
        {
            if (cleanupTask.IsFaulted)
            {
                failures.AddRange(cleanupTask.Exception!.InnerExceptions);
            }
            else if (cleanupTask.IsCanceled)
            {
                failures.Add(new TaskCanceledException(cleanupTask));
            }
        }
    }

    private static async Task InvokeCleanupAsync(Func<Task> cleanup)
        => await cleanup().ConfigureAwait(false);
}
