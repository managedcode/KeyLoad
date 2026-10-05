using Aspire.Hosting.ApplicationModel;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal sealed class RequestCqrsRf3DiagnosticsSubscriberObserver : IAsyncDisposable
{
    private CancellationTokenSource? lifetime;
    private IAsyncEnumerator<LogSubscriber>? enumerator;
    private Task<bool>? pendingMove;
    private Task? disposeTask;
    private bool Initialized { get; set; }

    internal bool IsJoined => !Initialized || (enumerator is null && lifetime is null && pendingMove is null
        && disposeTask is { IsCompleted: true });

    internal void Initialize(ResourceLoggerService logger, CancellationToken cancellationToken)
    {
        Initialized = true;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        enumerator = logger.WatchAnySubscribersAsync(lifetime.Token).GetAsyncEnumerator(lifetime.Token);
        pendingMove = enumerator.MoveNextAsync().AsTask();
    }

    internal async Task WaitForStateAsync(bool expected)
    {
        var observed = new HashSet<string>(StringComparer.Ordinal);
        while (observed.Count < RequestCqrsRf3Protocol.NodeCount)
        {
            var active = enumerator
                ?? throw new InvalidOperationException("The Aspire subscriber observer is not initialized.");
            var pending = pendingMove;
            if (pending is null)
            {
                pending = active.MoveNextAsync().AsTask();
                pendingMove = pending;
            }
            var moved = await pending.ConfigureAwait(false);
            pendingMove = null;
            if (!moved)
            { throw new InvalidOperationException("Aspire ended its resource subscriber observation unexpectedly."); }
            var subscriber = active.Current;
            if (IsNode(subscriber.Name) && subscriber.AnySubscribers == expected)
            { observed.Add(subscriber.Name); }
        }
    }

    public ValueTask DisposeAsync()
    {
        var actual = disposeTask ??= ShutdownCoreAsync();
        return new(actual);
    }

    internal async Task RetryFailedCloseAsync(List<Exception> failures)
    {
        if (enumerator is not null)
        { await CloseEnumeratorAsync(failures).ConfigureAwait(false); }
        if (lifetime is not null)
        { CloseLifetime(failures); }
    }

    private async Task ShutdownCoreAsync()
    {
        var failures = new List<Exception>();
        await CancelLifetimeAsync(failures).ConfigureAwait(false);
        await JoinPendingMoveAsync(failures).ConfigureAwait(false);
        await CloseEnumeratorAsync(failures).ConfigureAwait(false);
        CloseLifetime(failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task CancelLifetimeAsync(List<Exception> failures)
    {
        var active = lifetime;
        if (active is not null)
        { await ServerFailureObserver.ObserveAsync(active.CancelAsync, failures).ConfigureAwait(false); }
    }

    private async Task JoinPendingMoveAsync(List<Exception> failures)
    {
        var pending = pendingMove;
        if (pending is null)
        { return; }
        await ServerFailureObserver.ObserveAsync(() => pending, failures).ConfigureAwait(false);
        pendingMove = null;
    }

    private async Task CloseEnumeratorAsync(List<Exception> failures)
    {
        var active = enumerator;
        if (active is null)
        { return; }
        try
        {
            await active.DisposeAsync().ConfigureAwait(false);
            enumerator = null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    private void CloseLifetime(List<Exception> failures)
    {
        var active = lifetime;
        if (active is null)
        { return; }
        try
        {
            active.Dispose();
            lifetime = null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;
}
