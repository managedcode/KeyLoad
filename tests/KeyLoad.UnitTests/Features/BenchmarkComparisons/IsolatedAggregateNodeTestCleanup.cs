using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeTestCleanup
{
    private const int CleanupSeconds = 5;

    internal static async Task CleanupAsync(CancellationTokenSource prompt,
        Task<IsolatedAggregateNodeResult>? original, OperationCanceledException? expectedCancellation,
        string directory, List<IsolatedAggregateNodeIdentity> identities, Stopwatch timer,
        List<Exception> failures)
    {
        await RunCleanupAsync(() => CancelPromptAsync(prompt, timer, failures), failures);
        await RunCleanupAsync(() => ObserveOriginalAsync(original, expectedCancellation, timer, failures), failures);
        await RunCleanupAsync(() => IsolatedAggregateNodeTestProcessReaper.ReapOwnedProcessesAsync(identities, timer, failures), failures);
        await RunCleanupAsync(() => DeleteOwnedDirectoryAsync(directory, failures), failures);
    }

    private static async Task RunCleanupAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            await operation();
        }
        catch (Exception failure)
        {
            AddDistinct(failures, failure);
        }
    }

    private static async Task CancelPromptAsync(CancellationTokenSource prompt, Stopwatch timer,
        List<Exception> failures)
    {
        try
        {
            var cancellation = prompt.CancelAsync();
            var remaining = Remaining(timer);
            if (remaining <= TimeSpan.Zero)
            {
                if (cancellation.IsCompleted)
                {
                    await cancellation;
                }
                else
                {
                    ObserveLateOriginal(cancellation);
                    AddDistinct(failures, new TimeoutException(IsolatedAggregateNodeLifetimeProgram.OriginalDidNotSettle));
                }
                return;
            }
            await cancellation.WaitAsync(remaining);
        }
        catch (Exception failure)
        {
            AddDistinct(failures, failure);
        }
    }

    private static async Task ObserveOriginalAsync(Task<IsolatedAggregateNodeResult>? original,
        OperationCanceledException? expectedCancellation, Stopwatch timer, List<Exception> failures)
    {
        if (original is null)
        {
            return;
        }
        var timedOut = false;
        var bound = Remaining(timer);
        if (bound <= TimeSpan.Zero)
        {
            if (original.IsCompleted)
            {
                bound = TimeSpan.Zero;
            }
            else
            {
                ObserveLateOriginal(original);
                AddDistinct(failures, new TimeoutException(IsolatedAggregateNodeLifetimeProgram.OriginalDidNotSettle));
                return;
            }
        }
        try
        {
            await original.WaitAsync(bound);
        }
        catch (OperationCanceledException failure) when (SameCancellation(failure, expectedCancellation, original))
        {
        }
        catch (TimeoutException failure) when (!original.IsCompleted)
        {
            timedOut = true;
            AddDistinct(failures, failure);
        }
        catch (Exception failure)
        {
            AddDistinct(failures, failure);
        }
        if (timedOut || !original.IsCompleted)
        {
            ObserveLateOriginal(original);
            if (!timedOut)
            {
                AddDistinct(failures, new TimeoutException(IsolatedAggregateNodeLifetimeProgram.OriginalDidNotSettle));
            }
        }
    }

    private static bool SameCancellation(OperationCanceledException observed,
        OperationCanceledException? expected, Task original)
        => original.IsCanceled && expected is not null
            && observed.CancellationToken == expected.CancellationToken
            && observed.CancellationToken.IsCancellationRequested;

    private static void ObserveLateOriginal(Task original)
        => _ = original.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    private static Task DeleteOwnedDirectoryAsync(string directory, List<Exception> failures)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            AddDistinct(failures, failure);
        }
        return Task.CompletedTask;
    }

    private static TimeSpan Remaining(Stopwatch timer)
        => TimeSpan.FromSeconds(CleanupSeconds) - timer.Elapsed;

    internal static void AddDistinct(List<Exception> failures, Exception failure)
    {
        if (!failures.Contains(failure, ReferenceEqualityComparer.Instance))
        {
            failures.Add(failure);
        }
    }

    internal static void ThrowFailures(Exception? primary, List<Exception> cleanupFailures)
    {
        var failures = cleanupFailures.Where(failure => !ReferenceEquals(failure, primary)).ToArray();
        if (primary is not null && failures.Length == 0)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
        if (primary is null && failures.Length == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
        if (primary is not null || failures.Length > 0)
        {
            throw new AggregateException("Native Node lifetime test and cleanup failed.",
                primary is null ? failures : [primary, .. failures]);
        }
    }
}
