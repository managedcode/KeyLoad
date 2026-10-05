using ManagedCode.Communication.CQRS;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedNativeOriginalTaskSettlement
{
    private static readonly TimeSpan Threshold = TimeSpan.FromSeconds(30);

    internal static async Task<bool> RunAsync(Func<Task> start, string stage,
        IsolatedNativeTeardownFailures failures, Func<Task>? escalate = null)
    {
        Task original;
        try
        { original = start(); }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            failures.Record(stage, failure);
            return false;
        }
        catch (Exception failure) when (HasFatal(failure))
        {
            failures.Record(stage, failure);
            return false;
        }
        return await JoinAsync(original, stage, failures, escalate);
    }

    private static async Task<bool> JoinAsync(Task original, string stage,
        IsolatedNativeTeardownFailures failures, Func<Task>? escalate)
    {
        try
        {
            await original.WaitAsync(Threshold);
            return true;
        }
        catch (TimeoutException threshold)
        {
            failures.Record(stage, threshold);
            if (!original.IsCompleted && escalate is not null)
            {
                await RunAsync(escalate, stage, failures);
            }

            return await JoinAfterThresholdAsync(original, stage, failures);
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            failures.Record(stage, failure);
            return false;
        }
        catch (Exception failure) when (HasFatal(failure))
        {
            failures.Record(stage, failure);
            return false;
        }
    }

    private static async Task<bool> JoinAfterThresholdAsync(Task original, string stage,
        IsolatedNativeTeardownFailures failures)
    {
        try
        {
            await original;
            return false;
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            failures.Record(stage, failure);
            return false;
        }
        catch (Exception failure) when (HasFatal(failure))
        {
            failures.Record(stage, failure);
            return false;
        }
    }

    private static bool IsNonFatal(Exception failure) => CqrsRuntimeFailures.FindFatal(failure) is null;

    private static bool HasFatal(Exception failure) => CqrsRuntimeFailures.FindFatal(failure) is not null;
}
