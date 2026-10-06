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

        await RequestCancellationAsync(original, cancellation, failures);
        await ObserveOriginalAsync(original, cancellation, expectedCancellation, failures);
    }

    private static async Task RequestCancellationAsync(Task original, CancellationTokenSource cancellation,
        NativeSerializationBenchmarkFailures failures)
    {
        if (original.IsCompleted || cancellation.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await cancellation.CancelAsync();
        }
        catch (Exception failure)
        {
            failures.Add(failure);
        }
    }

    private static async Task ObserveOriginalAsync(Task<string?> original, CancellationTokenSource cancellation,
        OperationCanceledException? expectedCancellation, NativeSerializationBenchmarkFailures failures)
    {
        try
        {
            _ = await original;
        }
        catch (OperationCanceledException failure) when (original.IsCanceled
            && cancellation.IsCancellationRequested && failure.CancellationToken == cancellation.Token)
        {
            if (expectedCancellation is not null && !ReferenceEquals(expectedCancellation, failure))
            {
                failures.Add(new InvalidOperationException(FailureIdentityMessage));
            }
        }
        catch (Exception failure)
        {
            failures.AddTaskFailures(original, failure);
        }
    }
}
