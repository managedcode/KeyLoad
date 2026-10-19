using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Owns the original request registry and both startup cancellation sources until their known owners join.</summary>
internal sealed class OrleansNodeRequestLifetime(IOptions<GrainRoutingOptions> options) : IAsyncDisposable
{
    private const int NoCloseFailures = 0;
    private readonly NativeRequestWorkOwner work = new(options);
    private CancellationTokenSource? localInitializationCancellation;
    private CancellationTokenSource? bootstrapCancellation;
    private readonly Lock closing = new();
    private readonly List<Exception> closeFailures = [];
    private Task? shutdown;

    internal NativeRequestWorkOwner Work => work;

    internal CancellationToken CreateLocal(CancellationToken caller, CancellationToken stopping)
    {
        localInitializationCancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping);
        return localInitializationCancellation.Token;
    }

    internal CancellationToken CreateBootstrap(CancellationToken caller, CancellationToken stopping)
    {
        bootstrapCancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, stopping);
        return bootstrapCancellation.Token;
    }

    internal async Task CancelAndJoinStartupAsync(Task? preparing, Task? starting,
        List<Exception> startupFailures, List<Exception> cleanupFailures)
    {
        if (localInitializationCancellation is { } local)
        {
            await ServerFailureObserver.ObserveAsync(local.CancelAsync, cleanupFailures).ConfigureAwait(false);
        }
        if (bootstrapCancellation is { } bootstrap)
        {
            await ServerFailureObserver.ObserveAsync(bootstrap.CancelAsync, cleanupFailures).ConfigureAwait(false);
        }
        if (preparing is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => preparing, startupFailures).ConfigureAwait(false);
        }
        if (starting is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => starting, startupFailures).ConfigureAwait(false);
        }
    }

    internal async Task JoinAsync(List<Exception> failures)
    {
        var terminalFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), terminalFailures).ConfigureAwait(false);
        failures.AddRange(closeFailures.Count > NoCloseFailures ? closeFailures : terminalFailures);
    }

    /// <summary>Called only after the node's original known-owner join predicate permits releasing these resources.</summary>
    public ValueTask DisposeAsync()
    {
        lock (closing)
        {
            if (shutdown is null)
            {
                var registered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                shutdown = DisposeCoreAsync(registered.Task);
                registered.SetResult();
            }
            return new(shutdown);
        }
    }

    private async Task DisposeCoreAsync(Task registered)
    {
        await registered.ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => work.DisposeAsync().AsTask(), closeFailures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => localInitializationCancellation?.Dispose(), closeFailures);
        ServerFailureObserver.Observe(() => bootstrapCancellation?.Dispose(), closeFailures);
        ServerFailureObserver.ThrowIfAny(closeFailures);
    }
}
