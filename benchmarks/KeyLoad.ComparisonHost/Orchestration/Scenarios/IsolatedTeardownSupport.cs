using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

internal static class IsolatedTeardownSupport
{
    internal const string WorkerFile = "session-state.json";
    internal const string RunnerLog = "runner.log";
    internal const string NodeLog = "progress-execution-node.log";
    internal const string ReceiptFile = "teardown.json";

    internal static async Task CollectFailureAsync(Func<Task> operation, List<Exception> failures)
    {
        var initiation = StartAsync(operation);
        var failure = await RuntimeComparisonFailure.ObserveAsync(initiation);
        if (failure is not null)
        {
            AddOriginalFailures(initiation, failure, failures);
            return;
        }
        var original = await initiation;
        failure = await RuntimeComparisonFailure.ObserveAsync(original);
        if (failure is not null)
        {
            AddOriginalFailures(original, failure, failures);
        }
    }

    internal static void AddOriginalFailures(Task original, Exception observed, List<Exception> failures)
    {
        AddDistinct(observed, failures);
        if (original.Exception is { } aggregate)
        {
            foreach (var failure in aggregate.InnerExceptions)
            {
                AddDistinct(failure, failures);
            }
        }
    }

    private static void AddDistinct(Exception failure, List<Exception> failures)
    {
        if (!failures.Any(existing => ReferenceEquals(existing, failure)))
        {
            failures.Add(failure);
        }
    }

    private static async Task<Task> StartAsync(Func<Task> operation)
    {
        await Task.CompletedTask;
        return operation();
    }
}
