using System.Runtime.ExceptionServices;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheReadPermitConcurrencyTests
{
    private const int Contenders = 8;
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task ConcurrentDuplicateSequenceHasOneWinnerAndLeavesOneCurrentRevision()
    {
        using var permit = new CacheReadPermit(TimeProvider.System);
        await RunContendersAsync(permit);

        await Assert.That(permit.TryCapture(out var revision)).IsTrue();
        await Assert.That(revision).IsEqualTo(12);
        await Assert.That(permit.IsCurrent(revision)).IsTrue();
    }

    private static async Task RunContendersAsync(CacheReadPermit permit)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = new List<Task<bool>>(Contenders);
        var grant = Guid.NewGuid();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var prepared = 0L;
        var readyCount = 0;
        Task<bool[]>? aggregate = null;
        ExceptionDispatchInfo? primaryFailure = null;
        var cleanupFailures = new List<Exception>();

        try
        {
            await RethrowAsAggregateAsync(async () =>
            {
                StartAttempts(attempts, ready, start, grant, permit,
                    () => prepared, () => Interlocked.Increment(ref readyCount), Contenders, cancellationToken);
                await ready.Task.WaitAsync(CoordinationTimeout, TimeProvider.System, cancellationToken);
                prepared = TimeProvider.System.GetTimestamp();
                start.TrySetResult();
                aggregate = Task.WhenAll(attempts);
                var results = await aggregate.WaitAsync(CoordinationTimeout, TimeProvider.System, cancellationToken);
                await Assert.That(results.Count(result => result)).IsEqualTo(1);
            });
        }
        catch (AggregateException error)
        {
            primaryFailure = ExceptionDispatchInfo.Capture(error.InnerException ?? error);
        }
        finally
        {
            start.TrySetResult();
            aggregate ??= Task.WhenAll(attempts);
            await JoinAttemptsAsync(aggregate, attempts, primaryFailure, cleanupFailures);
        }

        ThrowFailures(primaryFailure, cleanupFailures);
    }

    private static void StartAttempts(List<Task<bool>> attempts, TaskCompletionSource ready,
        TaskCompletionSource start, Guid grant, CacheReadPermit permit,
        Func<long> getPrepared, Func<int> signalReady, int contenderCount, CancellationToken cancellationToken)
    {
        for (var index = 0; index < contenderCount; index++)
        {
            attempts.Add(Task.Factory.StartNew(() =>
            {
                if (signalReady() == contenderCount)
                {
                    ready.TrySetResult();
                }
                start.Task.WaitAsync(CoordinationTimeout, TimeProvider.System, cancellationToken)
                    .GetAwaiter().GetResult();
                return permit.TryAccept(grant, 12, getPrepared(), out _);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
        }
    }

    private static async Task JoinAttemptsAsync(Task<bool[]> aggregate, List<Task<bool>> attempts,
        ExceptionDispatchInfo? primaryFailure, List<Exception> cleanupFailures)
    {
        if (aggregate.IsCompleted)
        {
            AddUnobservedFailures(aggregate, primaryFailure, cleanupFailures);
            return;
        }

        try
        {
            await RethrowAsAggregateAsync(() => aggregate.WaitAsync(CoordinationTimeout, TimeProvider.System));
        }
        catch (AggregateException error)
        {
            var cause = error.InnerException ?? error;
            AddUnobservedFailures(aggregate, primaryFailure, cleanupFailures, cause);
            ObserveLateFailures(aggregate, attempts);
        }
    }

    private static async Task RethrowAsAggregateAsync(Func<Task> action)
    {
        try
        { await action(); }
        catch (Exception error)
        { throw new AggregateException(error); }
    }

    private static void AddUnobservedFailures(Task<bool[]> aggregate, ExceptionDispatchInfo? primaryFailure,
        List<Exception> cleanupFailures, Exception? fallback = null)
    {
        var failures = aggregate.Exception?.Flatten().InnerExceptions;
        if (failures is null && aggregate.IsCanceled)
        {
            if (primaryFailure?.SourceException is not OperationCanceledException)
            {
                cleanupFailures.Add(fallback ?? new TaskCanceledException(aggregate));
            }
            else if (fallback is not null and not OperationCanceledException)
            {
                cleanupFailures.Add(fallback);
            }
            return;
        }
        if (failures is null)
        {
            if (fallback is not null)
            {
                cleanupFailures.Add(fallback);
            }
            return;
        }

        foreach (var failure in failures)
        {
            if (primaryFailure is null || !ReferenceEquals(failure, primaryFailure.SourceException))
            {
                cleanupFailures.Add(failure);
            }
        }
        if (fallback is not null && !ReferenceEquals(fallback, primaryFailure?.SourceException)
            && !failures.Any(failure => ReferenceEquals(failure, fallback)))
        {
            cleanupFailures.Add(fallback);
        }
    }

    private static void ObserveLateFailures(Task<bool[]> aggregate, List<Task<bool>> attempts)
    {
        ObserveFault(aggregate);
        foreach (var attempt in attempts)
        {
            ObserveFault(attempt);
        }
    }

    private static void ObserveFault(Task task)
        => _ = task.ContinueWith(static failed => _ = failed.Exception, CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private static void ThrowFailures(ExceptionDispatchInfo? primaryFailure, List<Exception> cleanupFailures)
    {
        if (primaryFailure is not null && cleanupFailures.Count == 0)
        {
            primaryFailure.Throw();
        }
        if (primaryFailure is not null)
        {
            cleanupFailures.Insert(0, primaryFailure.SourceException);
        }
        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException("Concurrent permit assertions or worker cleanup failed.", cleanupFailures);
        }
    }
}
