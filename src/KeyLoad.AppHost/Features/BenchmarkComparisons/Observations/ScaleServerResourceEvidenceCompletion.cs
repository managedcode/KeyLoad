using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceEvidenceCompletion
{
    private readonly Lock sync = new();
    private Task? original;
    private Task? completion;

    internal Task CompleteAsync(Task observation, Task? expected, Func<Task> cancel,
        Action markMissing, Action dispose, Func<Task> write)
    {
        lock (sync)
        {
            if (expected is null || !ReferenceEquals(observation, expected))
            {
                return Task.FromException(new InvalidOperationException("The original server resource observation is required."));
            }

            if (original is not null && !ReferenceEquals(original, observation))
            {
                return Task.FromException(new InvalidOperationException("A different server resource observation was supplied."));
            }

            original = observation;
            return completion ??= SettleAsync(observation, cancel, markMissing, dispose, write);
        }
    }

    private static async Task SettleAsync(Task observation, Func<Task> cancel,
        Action markMissing, Action dispose, Func<Task> write)
    {
        var failures = new List<Exception>();
        await CancelAsync(cancel, failures);
        await JoinAsync(observation, markMissing, failures);
        Attempt(dispose, failures);
        await WriteAsync(write, failures);
        ThrowFailures(failures);
    }

    private static async Task CancelAsync(Func<Task> cancel, List<Exception> failures)
    {
        try
        { await cancel(); }
        catch (Exception failure) when (IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (HasFatal(failure)) { failures.Add(failure); }
    }

    private static async Task JoinAsync(Task observation, Action markMissing, List<Exception> failures)
    {
        CancellationTokenSource? thresholdCancellation = null;
        Task? threshold = null;
        try
        {
            thresholdCancellation = new CancellationTokenSource();
            threshold = Task.Delay(TimeSpan.FromSeconds(ScaleServerResourceBounds.CleanupSeconds), thresholdCancellation.Token);
            if (await Task.WhenAny(observation, threshold) != observation)
            {
                Attempt(markMissing, failures);
            }
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            Attempt(markMissing, failures);
            failures.Add(failure);
        }
        catch (Exception failure) when (HasFatal(failure))
        {
            Attempt(markMissing, failures);
            failures.Add(failure);
        }
        finally
        {
            CancelThreshold(thresholdCancellation, failures);
            if (threshold is not null && thresholdCancellation is not null)
            {
                await JoinThresholdAsync(threshold, thresholdCancellation.Token, failures);
            }

            if (thresholdCancellation is not null)
            {
                Attempt(thresholdCancellation.Dispose, failures);
            }
        }
        try
        { await observation; }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            Attempt(markMissing, failures);
            failures.Add(failure);
        }
        catch (Exception failure) when (HasFatal(failure))
        {
            Attempt(markMissing, failures);
            failures.Add(failure);
        }
    }

    private static void CancelThreshold(CancellationTokenSource? cancellation, List<Exception> failures)
    {
        try
        { cancellation?.Cancel(); }
        catch (Exception failure) when (IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (HasFatal(failure)) { failures.Add(failure); }
    }

    private static async Task JoinThresholdAsync(Task threshold, CancellationToken expectedToken,
        List<Exception> failures)
    {
        try
        { await threshold; }
        catch (OperationCanceledException failure) when (failure.CancellationToken == expectedToken
            && expectedToken.IsCancellationRequested)
        { }
        catch (Exception failure) when (IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (HasFatal(failure)) { failures.Add(failure); }
    }

    private static void Attempt(Action? action, List<Exception> failures)
    {
        if (action is null)
        {
            return;
        }

        try
        { action(); }
        catch (Exception failure) when (IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (HasFatal(failure)) { failures.Add(failure); }
    }

    private static async Task WriteAsync(Func<Task> write, List<Exception> failures)
    {
        try
        { await write(); }
        catch (Exception failure) when (IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (HasFatal(failure)) { failures.Add(failure); }
    }

    private static void ThrowFailures(List<Exception> failures)
    {
        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private static bool IsNonFatal(Exception failure) => CqrsRuntimeFailures.FindFatal(failure) is null;

    private static bool HasFatal(Exception failure) => CqrsRuntimeFailures.FindFatal(failure) is not null;
}
