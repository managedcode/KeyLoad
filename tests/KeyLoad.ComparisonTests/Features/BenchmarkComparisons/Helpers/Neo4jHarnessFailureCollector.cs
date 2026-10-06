namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jHarnessFailureCollector
{
    private const string CapturedFailureMessage = "Neo4jHarnessCapturedFailure";

    public static async Task CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (Exception error)
        {
            throw new AggregateException(CapturedFailureMessage, error);
        }
    }

    public static async Task AttemptAsync(Func<Task> operation, List<Exception> failures)
    {
        try
        {
            await CaptureAsync(operation);
        }
        catch (AggregateException failure)
        {
            failures.AddRange(failure.InnerExceptions);
        }
    }

    public static async Task AttemptBoundedAsync(Func<CancellationToken, Task> operation, List<Exception> failures)
    {
        using var timeout = new CancellationTokenSource(NativeExecutionPolicyFixture.Harness().Value.Neo4jFailureCleanupTimeout);
        await AttemptAsync(() => operation(timeout.Token), failures);
    }

    public static void ThrowIfAny(string message, List<Exception> failures)
    {
        if (failures.Count > 0)
        {
            throw new AggregateException(message, failures);
        }
    }
}
