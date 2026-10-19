using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.OrleansHosting;

/// <summary>Owns the original request registry and both startup cancellation sources until their known owners join.</summary>
internal sealed class OrleansNodeRequestLifetime(IOptions<GrainRoutingOptions> options) : IAsyncDisposable
{
    private const int NoCloseFailures = 0;
    private readonly RequestWorkOwner work = new(options);
    private CancellationTokenSource? localInitializationCancellation;
    private CancellationTokenSource? bootstrapCancellation;
    private readonly Lock closing = new();
    private readonly List<Exception> closeFailures = [];
    private Task? shutdown;

    internal RequestWorkOwner Work => work;

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
            await ServerFailureObserver.ObserveAsync(local.CancelAsync, cleanupFailures);
        }
        if (bootstrapCancellation is { } bootstrap)
        {
            await ServerFailureObserver.ObserveAsync(bootstrap.CancelAsync, cleanupFailures);
        }
        if (preparing is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => preparing, startupFailures);
        }
        if (starting is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => starting, startupFailures);
        }
    }

    internal async Task JoinAsync(List<Exception> failures)
    {
        var terminalFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => DisposeAsync().AsTask(), terminalFailures);
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
        await registered;
        await ServerFailureObserver.ObserveAsync(() => work.DisposeAsync().AsTask(), closeFailures);
        ServerFailureObserver.Observe(() => localInitializationCancellation?.Dispose(), closeFailures);
        ServerFailureObserver.Observe(() => bootstrapCancellation?.Dispose(), closeFailures);
        ServerFailureObserver.ThrowIfAny(closeFailures);
    }
}
