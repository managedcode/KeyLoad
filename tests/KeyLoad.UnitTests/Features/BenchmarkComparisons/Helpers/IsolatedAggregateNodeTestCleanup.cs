using System.Diagnostics;
using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class IsolatedAggregateNodeTestCleanup
{
    private const int CleanupSeconds = 5;

    internal static async Task<(Task? Cancellation, bool Settled)> CleanupAsync(CancellationTokenSource prompt, Task? originalCancellation,
        Task<IsolatedAggregateNodeResult>? original, OperationCanceledException? expectedCancellation,
        string directory, List<IsolatedAggregateNodeIdentity> identities, Stopwatch timer,
        List<Exception> failures)
    {
        Task? cancellation = null;
        var cancellationSettled = false;
        try
        {
            (cancellation, cancellationSettled) = await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(
                () => CancelPromptAsync(prompt, originalCancellation, timer, failures));
        }
        catch (AggregateException envelope)
        {
            AddDistinct(failures, IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
        }
        var originalSettled = false;
        try
        {
            originalSettled = await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(
                () => ObserveOriginalAsync(original, expectedCancellation, timer, failures));
        }
        catch (AggregateException envelope)
        {
            AddDistinct(failures, IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
        }
        await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
            () => IsolatedAggregateNodeTestProcessReaper.ReapOwnedProcessesAsync(identities, timer, failures),
            failure => AddDistinct(failures, failure));
        IsolatedAggregateNodeGuardedInvocation.Capture(
            () => DeleteOwnedDirectory(directory), failure => AddDistinct(failures, failure));
        return (cancellation, cancellationSettled && originalSettled);
    }

    private static async Task<(Task? Cancellation, bool Settled)> CancelPromptAsync(
        CancellationTokenSource prompt, Task? originalCancellation, Stopwatch timer, List<Exception> failures)
    {
        Task? cancellation;
        if (originalCancellation is not null)
        {
            cancellation = originalCancellation;
        }
        else
        {
            try
            {
                cancellation = IsolatedAggregateNodeGuardedInvocation.Invoke(() => prompt.CancelAsync());
            }
            catch (AggregateException envelope)
            {
                AddDistinct(failures, IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
                return (null, true);
            }
        }
        var bound = Remaining(timer);
        if (bound <= TimeSpan.Zero && !cancellation.IsCompleted)
        {
            AddDistinct(failures, new TimeoutException(IsolatedAggregateNodeLifetimeProgram.OriginalDidNotSettle));
            return (cancellation, false);
        }
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(
                () => cancellation.WaitAsync(bound > TimeSpan.Zero ? bound : TimeSpan.Zero));
        }
        catch (AggregateException envelope)
        {
            if (!cancellation.IsCompleted)
            {
                AddDistinct(failures, IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
                return (cancellation, false);
            }
        }
        await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(() => cancellation,
            failure => AddDistinct(failures, failure));
        return (cancellation, true);
    }

    private static async Task<bool> ObserveOriginalAsync(Task<IsolatedAggregateNodeResult>? original,
        OperationCanceledException? expectedCancellation, Stopwatch timer, List<Exception> failures)
    {
        if (original is null)
        {
            return true;
        }
        var bound = Remaining(timer);
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(
                () => original.WaitAsync(bound > TimeSpan.Zero ? bound : TimeSpan.Zero));
        }
        catch (AggregateException envelope)
        {
            if (!original.IsCompleted)
            {
                AddDistinct(failures, IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope));
                return false;
            }
        }
        await CaptureOriginalOutcomeAsync(original, expectedCancellation, failures);
        return true;
    }

    private static async Task CaptureOriginalOutcomeAsync(Task original,
        OperationCanceledException? expectedCancellation, List<Exception> failures)
    {
        try
        {
            await IsolatedAggregateNodeGuardedInvocation.InvokeAsync(() => original);
        }
        catch (AggregateException envelope)
        {
            var failure = IsolatedAggregateNodeGuardedInvocation.Unwrap(envelope);
            if (failure is not OperationCanceledException cancellation
                || expectedCancellation is null
                || cancellation.CancellationToken != expectedCancellation.CancellationToken
                || !cancellation.CancellationToken.IsCancellationRequested)
            {
                AddDistinct(failures, failure);
            }
        }
    }

    private static void DeleteOwnedDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
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
