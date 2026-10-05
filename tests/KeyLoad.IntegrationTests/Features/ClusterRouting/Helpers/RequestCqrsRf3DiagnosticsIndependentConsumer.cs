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
    private IAsyncEnumerator<IReadOnlyList<LogLine>>? enumerator;
    private Task<bool>? pendingMove;
    private Task? disposalTask;
    private bool lifetimeDisposed;

    internal RequestCqrsRf3DiagnosticsIndependentConsumer(ResourceLoggerService logger, IResource resource,
        CancellationToken cancellationToken)
    {
        this.logger = logger;
        this.resource = resource;
        resourceLogger = logger.GetLogger(resource);
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

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

    internal async Task RetryFailedCloseAsync(List<Exception> failures)
    {
        if (pendingMove is { IsCompleted: false })
        {
            await ServerFailureObserver.ObserveAsync(lifetime.CancelAsync, failures).ConfigureAwait(false);
        }
        await JoinPendingReadAsync(failures).ConfigureAwait(false);
        await DisposeEnumeratorAsync(failures).ConfigureAwait(false);
        DisposeLifetime(failures);
    }

    private async Task WaitForMarkerAsync(string marker, CancellationToken cancellationToken)
    {
        while (true)
        {
            var move = pendingMove
                ?? throw new InvalidOperationException("The original native log read is absent.");
            var moved = await move.WaitAsync(cancellationToken).ConfigureAwait(false);
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
        ServerFailureObserver.Observe(() => logger.Complete(resource), failures);
        await JoinPendingReadAsync(failures).ConfigureAwait(false);
        await DisposeEnumeratorAsync(failures).ConfigureAwait(false);
        DisposeLifetime(failures);
        ThrowWithNativeFatalPriority(failures);
    }

    private async Task JoinPendingReadAsync(List<Exception> failures)
    {
        var actual = pendingMove;
        if (actual is null)
        { return; }
        await ServerFailureObserver.ObserveAsync(() => actual, failures).ConfigureAwait(false);
        pendingMove = null;
    }

    private async Task DisposeEnumeratorAsync(List<Exception> failures)
    {
        var actual = enumerator;
        if (actual is null)
        { return; }
        try
        {
            await enumerator!.DisposeAsync().ConfigureAwait(false);
            enumerator = null;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
    }

    private void DisposeLifetime(List<Exception> failures)
    {
        if (lifetimeDisposed)
        { return; }
        try
        {
            lifetime.Dispose();
            lifetimeDisposed = true;
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        { failures.Add(error); }
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
