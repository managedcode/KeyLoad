using Aspire.Hosting.ApplicationModel;
using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.Logging;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

/// <summary>Owns one real native Aspire resource stream independent of the three RF3 captures.</summary>
internal sealed class RequestCqrsRf3DiagnosticsIndependentConsumer : IAsyncDisposable
{
    private const string MarkerTemplate = "Native diagnostic join oracle {Marker}";
    private static readonly Action<ILogger, string, Exception?> LogMarker =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(0), MarkerTemplate);
    private readonly ResourceLoggerService logger;
    private readonly IResource resource;
    private readonly ILogger resourceLogger;
    private readonly CancellationTokenSource lifetime;
    private readonly Action<RequestCqrsLifecycleStage>? failureObserver;
    private IAsyncEnumerator<IReadOnlyList<LogLine>>? enumerator;
    private Task<bool>? pendingMove;
    private Task<bool>? lastMove;
    private Task? disposalTask;
    private bool lifetimeDisposed;

    internal RequestCqrsRf3DiagnosticsIndependentConsumer(ResourceLoggerService logger, IResource resource,
        CancellationToken cancellationToken, Action<RequestCqrsLifecycleStage>? failureObserver = null)
    {
        this.logger = logger;
        this.resource = resource;
        this.failureObserver = failureObserver;
        resourceLogger = logger.GetLogger(resource);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    internal RequestCqrsSingleTaskLifecycleSnapshot ReadLifecycleSnapshot()
        => new(pendingMove?.Status ?? lastMove?.Status, lifetime.IsCancellationRequested);

    internal bool IsJoined => enumerator is null && pendingMove is null && lifetimeDisposed
        && disposalTask is { IsCompleted: true };

    internal async Task StartAsync(CancellationToken cancellationToken)
    {
        enumerator = logger.WatchAsync(resource).GetAsyncEnumerator(lifetime.Token);
        pendingMove = enumerator.MoveNextAsync().AsTask();
        await EmitAndObserveAsync("before-join", cancellationToken).ConfigureAwait(false);
    }

    internal async Task EmitAndObserveAsync(string marker, CancellationToken cancellationToken)
    {
        LogMarker(resourceLogger, marker, null);
        await WaitForMarkerAsync(marker, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        var actual = disposalTask ??= DisposeCoreAsync();
        return new(actual);
    }

    internal async Task RetryFailedCloseAsync(List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? observer = null)
    {
        if (pendingMove is { IsCompleted: false })
        {
            await RequestCqrsLifecycleFailureObserver.ObserveAsync(lifetime.CancelAsync, failures,
                observer ?? failureObserver, RequestCqrsLifecycleStage.IndependentLifetimeDispose).ConfigureAwait(false);
        }
        await JoinPendingReadAsync(failures, observer ?? failureObserver).ConfigureAwait(false);
        await DisposeEnumeratorAsync(failures, observer ?? failureObserver).ConfigureAwait(false);
        DisposeLifetime(failures, observer ?? failureObserver);
    }

    private async Task WaitForMarkerAsync(string marker, CancellationToken cancellationToken)
    {
        while (true)
        {
            var move = pendingMove
                ?? throw new InvalidOperationException("The original native log read is absent.");
            var moved = await move.WaitAsync(cancellationToken).ConfigureAwait(false);
            lastMove = move;
            pendingMove = null;
            if (!moved)
            { throw new InvalidOperationException("The independent native log stream completed before its marker."); }
            var found = false;
            foreach (var line in enumerator!.Current)
            { found |= line.Content.Contains(marker, StringComparison.Ordinal); }
            pendingMove = enumerator.MoveNextAsync().AsTask();
            if (found)
            { return; }
        }
    }

    private async Task DisposeCoreAsync()
    {
        var failures = new List<Exception>();
        RequestCqrsLifecycleFailureObserver.Observe(() => logger.Complete(resource), failures,
            failureObserver, RequestCqrsLifecycleStage.IndependentComplete);
        await JoinPendingReadAsync(failures, failureObserver).ConfigureAwait(false);
        await DisposeEnumeratorAsync(failures, failureObserver).ConfigureAwait(false);
        DisposeLifetime(failures, failureObserver);
        ThrowWithNativeFatalPriority(failures);
    }

    private async Task JoinPendingReadAsync(List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? observer = null)
    {
        var actual = pendingMove;
        if (actual is null)
        { return; }
        await RequestCqrsLifecycleFailureObserver.ObserveAsync(() => actual, failures,
            observer ?? failureObserver, RequestCqrsLifecycleStage.IndependentJoin).ConfigureAwait(false);
        lastMove = actual;
        pendingMove = null;
    }

    private async Task DisposeEnumeratorAsync(List<Exception> failures,
        Action<RequestCqrsLifecycleStage>? observer = null)
    {
        var actual = enumerator;
        if (actual is null)
        { return; }
        var before = failures.Count;
        try
        { await enumerator!.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.IndependentEnumeratorDispose); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.IndependentEnumeratorDispose); }
        if (failures.Count == before)
        { enumerator = null; }
    }

    private void DisposeLifetime(List<Exception> failures, Action<RequestCqrsLifecycleStage>? observer = null)
    {
        if (lifetimeDisposed)
        { return; }
        try
        {
            lifetime.Dispose();
            lifetimeDisposed = true;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.IndependentLifetimeDispose); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { RequestCqrsLifecycleFailureObserver.Append(failures, error, observer ?? failureObserver,
            RequestCqrsLifecycleStage.IndependentLifetimeDispose); }
    }

    private static void ThrowWithNativeFatalPriority(List<Exception> failures)
    {
        var fatalIndex = failures.FindIndex(error => !NativeCqrsBoundaryErrors.IsNonFatal(error));
        if (fatalIndex < 0)
        { ServerFailureObserver.ThrowIfAny(failures); return; }
        var prioritized = new List<Exception>(failures.Count) { failures[fatalIndex] };
        for (var index = 0; index < failures.Count; index++)
        {
            if (index != fatalIndex)
            { prioritized.Add(failures[index]); }
        }
        ServerFailureObserver.ThrowIfAny(prioritized);
    }
}
