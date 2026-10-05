using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.Comparisons;

internal static class ComparisonSessionCleanup
{
    internal const string Failure = "ComparisonSessionCleanupFailed";
    private const int MinimumTimeoutSeconds = 1;

    internal static async Task<bool> CloseAsync(IEnumerable<IComparisonSession> sessions, int timeoutSeconds)
    {
        var result = await CloseAndJoinAsync(sessions, timeoutSeconds).ConfigureAwait(false);
        ThrowFatal(result.Failures);
        return result.Failures.IsEmpty && !result.ThresholdExpired;
    }

    internal static Task<ComparisonSessionCloseResult> CloseAndJoinAsync(
        IEnumerable<IComparisonSession> sessions, int timeoutSeconds)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutSeconds, MinimumTimeoutSeconds);
        return CloseAndJoinAsync(sessions, TimeSpan.FromSeconds(timeoutSeconds));
    }

    internal static async Task<ComparisonSessionCloseResult> CloseAndJoinAsync(
        IEnumerable<IComparisonSession> sessions, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        var closeTasks = StartDisposals(sessions);
        var joined = Task.WhenAll(closeTasks);
        using var deadlineSource = new CancellationTokenSource();
        var deadline = Task.Delay(timeout, deadlineSource.Token);
        var thresholdExpired = await Task.WhenAny(joined, deadline).ConfigureAwait(false) != joined;
        if (!thresholdExpired)
        {
            await deadlineSource.CancelAsync().ConfigureAwait(false);
            try
            {
                await deadline.ConfigureAwait(false);
            }
            catch (OperationCanceledException error) when (error.CancellationToken == deadlineSource.Token
                && deadlineSource.IsCancellationRequested)
            {
            }
        }
        var failures = await JoinOriginalsAsync(closeTasks).ConfigureAwait(false);
        return new(thresholdExpired, failures);
    }

    private static List<Task> StartDisposals(IEnumerable<IComparisonSession> sessions)
    {
        var tasks = new List<Task>();
        foreach (var session in sessions)
        {
            tasks.Add(StartDispose(session));
        }
        return tasks;
    }

    private static Task StartDispose(IComparisonSession session)
    {
        try
        {
            return session.DisposeAsync().AsTask();
        }
        catch (Exception failure)
        {
            return Task.FromException(failure);
        }
    }

    private static async Task<ImmutableArray<Exception>> JoinOriginalsAsync(IEnumerable<Task> tasks)
    {
        var failures = ImmutableArray.CreateBuilder<Exception>();
        foreach (var task in tasks)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                failures.Add(failure);
            }
        }
        return failures.ToImmutable();
    }

    private static void ThrowFatal(ImmutableArray<Exception> failures)
    {
        if (failures.Any(failure => CqrsRuntimeFailures.FindFatal(failure) is not null))
        {
            var combined = OpenLoopFailure.Combine(null, failures)!;
            ExceptionDispatchInfo.Capture(combined).Throw();
        }
    }
}

internal sealed record ComparisonSessionCloseResult(bool ThresholdExpired, ImmutableArray<Exception> Failures);
