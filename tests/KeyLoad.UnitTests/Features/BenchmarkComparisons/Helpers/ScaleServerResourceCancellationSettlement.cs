namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ScaleServerResourceCancellationSettlement
{
    private const string FailureIdentityMessage = "The original native cancellation failure changed while settling its task.";

    internal static async Task SettleAsync(Task<string?>? original, CancellationTokenSource cancellation,
        OperationCanceledException? expectedCancellation, NativeSerializationBenchmarkFailures failures)
    {
        if (original is null)
        {
            return;
        }

        await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
            () => RequestCancellationAsync(original, cancellation, failures), failures.Add);
        await IsolatedAggregateNodeGuardedInvocation.CaptureAsync(
            () => ObserveOriginalAsync(original, cancellation, expectedCancellation, failures), failures.Add);
    }

    private static async Task RequestCancellationAsync(Task original, CancellationTokenSource cancellation,
        NativeSerializationBenchmarkFailures failures)
    {
        if (original.IsCompleted || cancellation.IsCancellationRequested)
        {
            return;
        }

        Task? cancellationTask = null;
        IsolatedAggregateNodeGuardedInvocation.Capture(
            () => cancellationTask = cancellation.CancelAsync(), failures.Add);
        if (cancellationTask is null)
        {
            return;
        }

        await cancellationTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (cancellationTask.Exception is { } cancellationFailures)
        {
            foreach (var failure in cancellationFailures.InnerExceptions)
            {
                failures.Add(failure);
            }
        }
        if (cancellationTask.IsCanceled)
        {
            try
            {
                await cancellationTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException failure)
            {
                failures.Add(failure);
            }
        }
    }

    private static async Task ObserveOriginalAsync(Task<string?> original, CancellationTokenSource cancellation,
        OperationCanceledException? expectedCancellation, NativeSerializationBenchmarkFailures failures)
    {
        await ((Task)original).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (original.Exception is { } originalFailures)
        {
            foreach (var failure in originalFailures.InnerExceptions)
            {
                failures.Add(failure);
            }
            return;
        }
        if (!original.IsCanceled)
        {
            return;
        }

        try
        {
            _ = await original.ConfigureAwait(false);
        }
        catch (OperationCanceledException failure)
        {
            if (cancellation.IsCancellationRequested && failure.CancellationToken == cancellation.Token)
            {
                if (expectedCancellation is not null && !ReferenceEquals(expectedCancellation, failure))
                {
                    failures.Add(failure);
                    failures.Add(new InvalidOperationException(FailureIdentityMessage));
                }
                return;
            }
            failures.Add(failure);
        }
    }
}
