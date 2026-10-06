namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedResourceLogSubscriberScopeFactory
{
    internal static IsolatedResourceLogSubscriberScope<T> Create<T>(
        IAsyncEnumerable<T> source, CancellationToken parentToken)
        => new(source, parentToken);
}

internal sealed class IsolatedResourceLogSubscriberScope<T> : IAsyncDisposable
{
    private const string PendingOperations = "Native subscriber operations remain active; enumerator disposal is deferred.";
    private const string PendingDisposal = "Native subscriber disposal is still running; its lifetime remains retained.";
    private static TimeSpan CleanupDeadline => NativeExecutionPolicyFixture.Harness().Value.SubscriberCleanupTimeout;
    private readonly List<Task> _originalOperations = [];
    private readonly CancellationTokenSource _lifetime;
    private int _disposeStarted;

    internal IsolatedResourceLogSubscriberScope(IAsyncEnumerable<T> source, CancellationToken parentToken)
    {
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        try
        {
            Enumerator = source.GetAsyncEnumerator(_lifetime.Token);
        }
        catch (Exception failure)
        {
            _lifetime.Dispose();
            throw new AggregateException(failure);
        }
    }

    internal IAsyncEnumerator<T> Enumerator { get; }

    internal Task<bool> RegisterMove(Task<bool> move)
    {
        _originalOperations.Add(move);
        return move;
    }

    internal Task RegisterPublication(Task publication)
    {
        _originalOperations.Add(publication);
        return publication;
    }

    public async ValueTask DisposeAsync()
    {
        var failures = new List<Exception>();
        await DisposeAndCollectAsync(failures);
        IsolatedResourceLogCaptureStopSupport.ThrowFailures(failures);
    }

    private async Task DisposeAndCollectAsync(List<Exception> failures)
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
        {
            return;
        }

        Task? cancellation = null;
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(async () =>
        {
            cancellation = await StartCancellationAsync();
        }, failures);

        if (cancellation is not null)
        {
            _originalOperations.Add(cancellation);
        }

        var settled = Task.WhenAll(_originalOperations);
        using var timeout = new CancellationTokenSource(CleanupDeadline);
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(
            () => IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(settled, timeout.Token),
            failures);
        AddOriginalFaults(_originalOperations, failures);
        if (!settled.IsCompleted)
        {
            AddFailure(failures, new TimeoutException(PendingOperations));
            ScheduleDeferredDisposal(settled);
            return;
        }

        await DisposeEnumeratorAsync(failures);
    }

    private void ScheduleDeferredDisposal(Task settled)
    {
        foreach (var operation in _originalOperations)
        {
            ObserveLateFailure(operation);
        }

        var deferredDisposal = DisposeAfterOperationsAsync(settled);
        ObserveLateFailure(deferredDisposal);
    }

    private async Task DisposeAfterOperationsAsync(Task settled)
    {
        var failures = new List<Exception>();
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(() => settled, failures);
        AddOriginalFaults(_originalOperations, failures);
        await DisposeEnumeratorAsync(failures);
        IsolatedResourceLogCaptureStopSupport.ThrowFailures(failures);
    }

    private async Task DisposeEnumeratorAsync(List<Exception> failures)
    {
        var waitFailures = new List<Exception>();
        Task? disposal = null;
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(async () =>
        {
            disposal = await StartEnumeratorDisposalAsync();
        }, waitFailures);
        if (disposal is null)
        {
            AddFailures(failures, waitFailures);
            _lifetime.Dispose();
            return;
        }

        using var timeout = new CancellationTokenSource(CleanupDeadline);
        await IsolatedResourceLogCaptureStopSupport.CollectFailureAsync(
            () => IsolatedResourceLogCaptureStopSupport.AwaitBoundedAndObserveAsync(disposal, timeout.Token),
            waitFailures);

        if (!disposal.IsCompleted)
        {
            AddFailures(failures, waitFailures);
            AddFailure(failures, new TimeoutException(PendingDisposal));
            ReleaseLifetimeWhenComplete(disposal);
            return;
        }

        AddFailures(failures, waitFailures);
        AddOriginalFaults([disposal], failures);
        _lifetime.Dispose();
    }

    private Task<Task> StartEnumeratorDisposalAsync()
    {
        try
        {
            return Task.FromResult(Enumerator.DisposeAsync().AsTask());
        }
        catch (Exception failure)
        {
            throw new AggregateException(failure);
        }
    }

    private Task<Task> StartCancellationAsync()
    {
        try
        {
            return Task.FromResult(_lifetime.CancelAsync());
        }
        catch (Exception failure)
        {
            throw new AggregateException(failure);
        }
    }

    private void ReleaseLifetimeWhenComplete(Task disposal)
    {
        var release = disposal.ContinueWith(static (completed, state) =>
            {
                if (completed.IsFaulted)
                {
                    _ = completed.Exception;
                }
                ((CancellationTokenSource)state!).Dispose();
            }, _lifetime, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        ObserveLateFailure(release);
    }

    private static void AddOriginalFaults(IEnumerable<Task> operations, List<Exception> failures)
    {
        foreach (var operation in operations)
        {
            if (operation.Exception is { } operationFailure)
            {
                AddFailures(failures, operationFailure.Flatten().InnerExceptions);
            }
        }
    }

    private static void AddFailures(List<Exception> failures, IEnumerable<Exception> additions)
    {
        foreach (var addition in additions)
        {
            AddFailure(failures, addition);
        }
    }

    private static void AddFailure(List<Exception> failures, Exception addition)
    {
        if (!failures.Any(failure => ReferenceEquals(failure, addition)))
        {
            failures.Add(addition);
        }
    }

    private static void ObserveLateFailure(Task operation)
        => _ = operation.ContinueWith(static completed => { _ = completed.Exception; },
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
}
