using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceEvidenceCompletion(IOptions<ScaleServerResourceOptions> options)
{
    private readonly TimeSpan cleanupThreshold = options.Value.CleanupThreshold;
    private readonly Lock sync = new();
    private Task? original;
    private Task? completion;

    internal Task CompleteAsync(Task observation, Task? expected, Func<Task> cancel,
        Action markMissing, Action dispose, Func<Task> write)
    {
        const string MessageText = "The original server resource observation is required.";
        const string CompleteAsyncMessageText = "A different server resource observation was supplied.";

        lock (sync)
        {
            if (expected is null || !ReferenceEquals(observation, expected))
            {
                return Task.FromException(new InvalidOperationException(MessageText));
            }

            if (original is not null && !ReferenceEquals(original, observation))
            {
                return Task.FromException(new InvalidOperationException(CompleteAsyncMessageText));
            }

            original = observation;
            return completion ??= SettleAsync(observation, cancel, markMissing, dispose, write);
        }
    }

    private async Task SettleAsync(Task observation, Func<Task> cancel,
        Action markMissing, Action dispose, Func<Task> write)
    {
        var failures = new List<Exception>();
        await RecordAsync(InvokeAsync(cancel), failures);
        await JoinAsync(observation, markMissing, failures);
        await AttemptAsync(dispose, failures);
        await RecordAsync(InvokeAsync(write), failures);
        ThrowFailures(failures);
    }

    private async Task JoinAsync(Task observation, Action markMissing, List<Exception> failures)
    {
        if (await RecordAsync(WaitThresholdAsync(observation, markMissing, failures), failures))
        {
            await AttemptAsync(markMissing, failures);
        }
        if (await RecordAsync(observation, failures))
        {
            await AttemptAsync(markMissing, failures);
        }
    }

    private async Task WaitThresholdAsync(Task observation, Action markMissing, List<Exception> failures)
    {
        using var thresholdCancellation = new CancellationTokenSource();
        var threshold = Task.Delay(cleanupThreshold, thresholdCancellation.Token);
        try
        {
            if (await Task.WhenAny(observation, threshold) != observation)
            {
                await AttemptAsync(markMissing, failures);
            }
        }
        finally
        {
            await RecordAsync(InvokeAsync(thresholdCancellation.CancelAsync), failures);
            await JoinThresholdAsync(threshold, failures, thresholdCancellation.Token);
        }
    }

    private static async Task JoinThresholdAsync(Task threshold, List<Exception> failures,
        CancellationToken expectedToken)
    {
        try
        {
            await threshold;
        }
        catch (OperationCanceledException failure) when (failure.CancellationToken == expectedToken
            && expectedToken.IsCancellationRequested)
        {
        }
        catch (Exception failure) when (threshold.IsFaulted || threshold.IsCanceled)
        {
            failures.Add(failure);
        }
    }

    private static Task<bool> AttemptAsync(Action action, List<Exception> failures)
        => RecordAsync(InvokeAsync(() =>
        {
            action();
            return Task.CompletedTask;
        }), failures);

    private static async Task InvokeAsync(Func<Task> factory)
        => await factory();

    private static async Task<bool> RecordAsync(Task original, List<Exception> failures)
    {
        try
        {
            await original;
            return false;
        }
        catch (Exception failure) when (original.IsFaulted || original.IsCanceled)
        {
            failures.Add(failure);
            return true;
        }
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        const int SingleFailureCount = 1;
        const int IndexValue = 0;
        const int BoundaryValue = 1;

        if (failures.Count == SingleFailureCount)
        {
            ExceptionDispatchInfo.Capture(failures[IndexValue]).Throw();
        }
        if (failures.Count > BoundaryValue)
        {
            throw new AggregateException(failures);
        }
    }
}
