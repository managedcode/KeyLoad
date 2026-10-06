using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeOriginalTaskSettlement
{
    internal static async Task<bool> RunAsync(Func<Task> start, string stage,
        IsolatedNativeTeardownFailures failures, IOptions<NativeComparisonHarnessOptions> options, Func<Task>? escalate = null)
    {
        var initiation = StartAsync(start);
        Task original;
        try
        { original = await initiation; }
        catch (Exception failure) when (initiation.IsFaulted || initiation.IsCanceled)
        {
            Record(initiation, failure, stage, failures);
            return false;
        }
        return await JoinAsync(original, stage, failures, options, escalate);
    }

    private static async Task<Task> StartAsync(Func<Task> start)
    {
        await Task.CompletedTask;
        return start();
    }

    private static async Task<bool> JoinAsync(Task original, string stage,
        IsolatedNativeTeardownFailures failures, IOptions<NativeComparisonHarnessOptions> options, Func<Task>? escalate)
    {
        var wait = original.WaitAsync(options.Value.OriginalTaskSettlementTimeout);
        try
        {
            await wait;
            return true;
        }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            Record(original, failure, stage, failures);
            return false;
        }
        catch (TimeoutException threshold)
        {
            failures.Record(stage, threshold);
            if (!original.IsCompleted && escalate is not null)
            {
                await RunAsync(escalate, stage, failures, options);
            }
            return await JoinAfterThresholdAsync(original, stage, failures);
        }
    }

    private static async Task<bool> JoinAfterThresholdAsync(Task original, string stage,
        IsolatedNativeTeardownFailures failures)
    {
        try
        { await original; }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            Record(original, failure, stage, failures);
        }
        return false;
    }

    private static void Record(Task original, Exception observed, string stage, IsolatedNativeTeardownFailures failures)
    {
        failures.Record(stage, observed);
        if (original.Exception is { } aggregate)
        {
            foreach (var failure in aggregate.InnerExceptions)
            { failures.Record(stage, failure); }
        }
    }
}
