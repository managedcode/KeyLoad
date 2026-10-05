using Aspire.Hosting.ApplicationModel;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

internal sealed class RequestCqrsRf3DiagnosticsSubscriberObserver : IAsyncDisposable
{
    private CancellationTokenSource? lifetime;
    private CancellationToken lifetimeToken;
    private IAsyncEnumerator<LogSubscriber>? enumerator;
    private Task<bool>? pendingMove;
    private Task<bool>? lastMove;
    private Task? disposeTask;
    private Action<RequestCqrsLifecycleStage>? failureObserver;
    private bool Initialized { get; set; }

    internal RequestCqrsSingleTaskLifecycleSnapshot ReadLifecycleSnapshot()
        => new(pendingMove?.Status ?? lastMove?.Status, lifetimeToken.IsCancellationRequested);

    internal bool IsJoined => !Initialized || (enumerator is null && lifetime is null && pendingMove is null
        && disposeTask is { IsCompleted: true });

    internal void Initialize(ResourceLoggerService logger, CancellationToken cancellationToken,
        Action<RequestCqrsLifecycleStage>? observer = null)
    {
        failureObserver = observer;
        Initialized = true;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetimeToken = lifetime.Token;
        enumerator = logger.WatchAnySubscribersAsync(lifetimeToken).GetAsyncEnumerator(lifetimeToken);
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
            bool moved;
            try
            { moved = await pending.ConfigureAwait(false); }
            catch
            {
                lastMove = pending;
                pendingMove = null;
                throw;
            }
            lastMove = pending;
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

    internal async Task RetryFailedCloseAsync(List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? observer = null)
    {
        if (enumerator is not null)
        { await CloseEnumeratorAsync(failures, observer ?? failureObserver).ConfigureAwait(false); }
        if (lifetime is not null)
        { CloseLifetime(failures, observer ?? failureObserver); }
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
        { await RequestCqrsLifecycleFailureObserver.ObserveAsync(active.CancelAsync, failures,
            failureObserver, RequestCqrsLifecycleStage.ObserverCancellation).ConfigureAwait(false); }
    }

    private async Task JoinPendingMoveAsync(List<Exception> failures)
    {
        var pending = pendingMove;
        if (pending is null)
        { return; }
        await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => pending, failures,
            failureObserver, RequestCqrsLifecycleStage.ObserverJoin).ConfigureAwait(false);
        lastMove = pending;
        pendingMove = null;
    }

    private async Task CloseEnumeratorAsync(List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? observer = null)
    {
        var active = enumerator;
        if (active is null)
        { return; }
        var before = failures.Count;
        try
        { await active.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverEnumeratorDispose); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverEnumeratorDispose); }
        if (failures.Count == before)
        { enumerator = null; }
    }

    private void CloseLifetime(List<Exception> failures, Action<RequestCqrsLifecycleStage>? observer = null)
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
        { RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverLifetimeDispose); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverLifetimeDispose); }
    }

    private static bool IsNode(string name) => name is RequestCqrsRf3Protocol.Node1
        or RequestCqrsRf3Protocol.Node2 or RequestCqrsRf3Protocol.Node3;
}
