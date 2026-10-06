using System.Runtime.ExceptionServices;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>A threshold is reported only after the original native operation has settled.</summary>
internal static class PostgresSchemaOriginalTaskSettlement
{
    private const string SettlementFailure = "PostgreSQL regression threshold and original native operation failed.";

    internal static Task JoinAsync(Task original, TimeSpan timeout, CancellationToken cancellationToken)
        => JoinCoreAsync(original, timeout, null, cancellationToken);

    private static async Task JoinCoreAsync(Task original, TimeSpan timeout, Func<Task>? escalate,
        CancellationToken cancellationToken)
    {
        var observation = original.WaitAsync(timeout, cancellationToken);
        try
        {
            await observation;
        }
        catch (Exception failure) when (observation.IsFaulted || observation.IsCanceled)
        {
            if (ReferenceEquals(observation, original)
                || original.Exception?.InnerExceptions.Any(error => ReferenceEquals(error, failure)) == true
                || observation.IsCanceled && original.IsCanceled && !cancellationToken.IsCancellationRequested)
            {
                ExceptionDispatchInfo.Capture(OriginalFailure(original, failure)).Throw();
            }
            await JoinAfterThresholdAsync(original, failure, escalate);
        }
    }

    internal static async Task<T> JoinAsync<T>(Task<T> original, TimeSpan timeout, CancellationToken cancellationToken)
    {
        await JoinAsync((Task)original, timeout, cancellationToken);
        return await original;
    }

    internal static async Task<T> JoinAsync<T>(Task<T> original, TimeSpan timeout, Func<Task> escalate,
        CancellationToken cancellationToken)
    {
        await JoinCoreAsync(original, timeout, escalate, cancellationToken);
        return await original;
    }

    private static async Task JoinAfterThresholdAsync(Task original, Exception threshold, Func<Task>? escalate)
    {
        var failures = new List<Exception> { threshold };
        if (escalate is not null && !original.IsCompleted)
        {
            var escalation = InvokeEscalationAsync(escalate);
            await RetainFailureAsync(escalation, failures);
        }
        await RetainFailureAsync(original, failures);
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(threshold).Throw();
        }
        throw new AggregateException(SettlementFailure, failures);
    }

    private static async Task InvokeEscalationAsync(Func<Task> escalate) => await escalate();

    private static async Task RetainFailureAsync(Task original, List<Exception> failures)
    {
        try
        {
            await original;
        }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            failures.Add(OriginalFailure(original, failure));
        }
    }

    private static Exception OriginalFailure(Task original, Exception observed)
        => original.Exception is { } aggregate
            ? aggregate.InnerExceptions.Count == 1 ? aggregate.InnerExceptions[0] : aggregate
            : observed;
}
