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
    private Task<bool>? lastAdmissionMove;
    private Task? disposeTask;
    private Action<RequestCqrsLifecycleStage>? failureObserver;
    private readonly RequestCqrsRf3SubscriberTransitionObservation transitionObservation = new();
    private int moveOwner;
    private bool Initialized { get; set; }

    internal RequestCqrsSingleTaskLifecycleSnapshot ReadLifecycleSnapshot()
        => new(lastAdmissionMove?.Status, lifetimeToken.IsCancellationRequested);

    internal bool IsJoined => !Initialized || (enumerator is null && lifetime is null && pendingMove is null
        && disposeTask is { IsCompleted: true });

    internal void Initialize(ResourceLoggerService logger, CancellationToken cancellationToken,
        Action<RequestCqrsLifecycleStage>? observer = null)
    {
        failureObserver = observer;
        transitionObservation.SetCallerToken(cancellationToken);
        Initialized = true;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetimeToken = lifetime.Token;
        enumerator = logger.WatchAnySubscribersAsync(lifetimeToken).GetAsyncEnumerator(lifetimeToken);
        pendingMove = enumerator.MoveNextAsync().AsTask();
        lastAdmissionMove = pendingMove;
    }

    internal async Task WaitForStateAsync(bool expected)
    {
        if (System.Threading.Interlocked.CompareExchange(ref moveOwner, 1, 0) != 0)
        { throw new InvalidOperationException("The native subscriber stream already has an active reader."); }
        try
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
                lastAdmissionMove = pending;
                transitionObservation.ClaimAdmissionMove(pending);
                bool moved;
                try
                { moved = await pending.ConfigureAwait(false); }
                catch (Exception)
                {
                    lastMove = pending;
                    pendingMove = null;
                    throw;
                }
                lastMove = pending;
                pendingMove = null;
                if (!moved)
                { throw new InvalidOperationException(RequestCqrsRf3SubscriberTransitionObservation.UnexpectedCompletionMessage); }
                var subscriber = active.Current;
                var isNode = transitionObservation.RecordAdmission(subscriber.Name,
                    expected && subscriber.AnySubscribers);
                if (isNode && subscriber.AnySubscribers == expected)
                { observed.Add(subscriber.Name); }
            }
        }
        finally
        { System.Threading.Volatile.Write(ref moveOwner, 0); }
    }

    internal void BeginCompletionObservation() => transitionObservation.BeginCompletionObservation();

    internal void DrainReadyTransitions(bool afterOriginalJoin)
    {
        var active = enumerator;
        if (active is null)
        { return; }
        if (System.Threading.Interlocked.CompareExchange(ref moveOwner, 1, 0) != 0)
        {
            transitionObservation.MarkAmbiguous();
            return;
        }
        try
        {
            transitionObservation.DrainAvailableEvents(active, ref pendingMove, ref lastMove, afterOriginalJoin);
        }
        finally
        { System.Threading.Volatile.Write(ref moveOwner, 0); }
    }

    internal string FormatTransitionObservation() => transitionObservation.Format();

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
        {
            transitionObservation.BeginOwnedCancellation(pendingMove);
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(active.CancelAsync, failures,
                failureObserver, RequestCqrsLifecycleStage.ObserverCancellation).ConfigureAwait(false);
            transitionObservation.CompleteOwnedCancellation(active.IsCancellationRequested);
        }
    }

    private async Task JoinPendingMoveAsync(List<Exception> failures)
    {
        var pending = pendingMove;
        if (pending is null)
        { return; }
        await ((Task)pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (!transitionObservation.IsExpectedDiagnosticCancellation(pending))
        {
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => pending, failures,
                failureObserver, RequestCqrsLifecycleStage.ObserverJoin).ConfigureAwait(false);
        }
        lastMove = pending;
        pendingMove = null;
        transitionObservation.MoveJoined(pending);
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
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverEnumeratorDispose);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverEnumeratorDispose);
        }
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
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverLifetimeDispose);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.ObserverLifetimeDispose);
        }
    }

}
