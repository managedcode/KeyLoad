using System.Runtime.ExceptionServices;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedResourceLogCaptureStopSupport
{
    internal static async Task CollectFailureAsync(Func<Task> action, List<Exception> failures)
    {
        try
        {
            await WrapFailureAsync(action);
        }
        catch (AggregateException failure)
        {
            AddDistinctFailures(failures, failure.Flatten().InnerExceptions);
        }
    }

    private static async Task WrapFailureAsync(Func<Task> action)
    {
        try
        { await action(); }
        catch (Exception failure)
        { throw new AggregateException(failure); }
    }

    private static void AddDistinctFailures(List<Exception> failures, IEnumerable<Exception> additions)
    {
        foreach (var addition in additions)
        {
            if (!failures.Any(failure => ReferenceEquals(failure, addition)))
            {
                failures.Add(addition);
            }
        }
    }

    internal static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    internal static async Task<T> AwaitBoundedAndObserveAsync<T>(Task<T> operation, CancellationToken token)
    {
        try
        {
            return await operation.WaitAsync(token);
        }
        catch (Exception)
        {
            ObserveLateFailure(operation);
            throw;
        }
    }

    internal static async Task AwaitBoundedAndObserveAsync(Task operation, CancellationToken token)
    {
        try
        {
            await operation.WaitAsync(token);
        }
        catch (Exception)
        {
            ObserveLateFailure(operation);
            throw;
        }
    }

    private static void ObserveLateFailure(Task operation)
        => _ = operation.ContinueWith(static completed => { _ = completed.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
