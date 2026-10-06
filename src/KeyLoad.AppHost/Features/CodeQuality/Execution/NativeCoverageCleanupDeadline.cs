using System.Diagnostics.CodeAnalysis;

namespace KeyLoad.AppHost.Features.CodeQuality;

/// <summary>Joins coverage-owned cleanup tasks under one original absolute deadline.</summary>
internal sealed class NativeCoverageCleanupDeadline : IDisposable
{
    private const int FailStopExitCode = 1;
    private const string UnsettledMessage = "Native coverage cleanup did not settle before its owner deadline.";

    private readonly TimeSpan timeout;
    private readonly CancellationTokenSource cancellation;
    private readonly TimeProvider timeProvider;
    private readonly long started;
    private readonly Task expiration;

    internal CancellationToken Token => cancellation.Token;

    internal NativeCoverageCleanupDeadline(TimeSpan timeout, TimeProvider? provider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero, nameof(timeout));
        this.timeout = timeout;
        timeProvider = provider ?? TimeProvider.System;
        started = timeProvider.GetTimestamp();
        cancellation = new(timeout, timeProvider);
        expiration = Task.Delay(timeout, timeProvider, cancellation.Token);
    }

    internal async Task WaitAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var original = Start(operation);
        await JoinBeforeDeadlineAsync(original).ConfigureAwait(false);
        await original.ConfigureAwait(false);
    }

    internal async Task<T> WaitAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var original = Start(operation);
        await JoinBeforeDeadlineAsync(original).ConfigureAwait(false);
        return await original.ConfigureAwait(false);
    }

    internal async Task CollectAsync(Func<Task> operation, List<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        var original = Start(operation);
        await CollectAsync(original, failures).ConfigureAwait(false);
    }

    internal async Task CollectAsync(Task original, List<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(failures);
        await JoinBeforeDeadlineAsync(original).ConfigureAwait(false);
        RecordCompletedFailure(original, failures);
    }

    private static async Task Start(Func<Task> operation)
        => await operation().ConfigureAwait(false);

    private static async Task<T> Start<T>(Func<Task<T>> operation)
        => await operation().ConfigureAwait(false);

    private async Task JoinBeforeDeadlineAsync(Task original)
    {
        if (IsExpired())
        {
            FailStop();
        }
        if (original.IsCompleted)
        {
            return;
        }
        var completed = await Task.WhenAny(original, expiration).ConfigureAwait(false);
        if (completed != original || IsExpired())
        {
            FailStop();
        }
    }

    private bool IsExpired() => cancellation.IsCancellationRequested || timeProvider.GetElapsedTime(started) >= timeout;

    private static void RecordCompletedFailure(Task original, List<Exception> failures)
    {
        if (original.Exception is { } aggregate)
        {
            failures.AddRange(aggregate.InnerExceptions);
        }
        else if (original.IsCanceled)
        {
            try
            {
                original.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException failure)
            {
                failures.Add(failure);
            }
        }
    }

    [DoesNotReturn]
    private static void FailStop()
    {
        try
        { Console.Error.WriteLine(UnsettledMessage); }
        finally
        { Environment.Exit(FailStopExitCode); }
        throw new InvalidOperationException(UnsettledMessage);
    }

    public void Dispose()
    {
        try
        {
            cancellation.Cancel();
        }
        finally
        {
            expiration.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing).GetAwaiter().GetResult();
            cancellation.Dispose();
        }
    }
}
