using KeyLoad.Clustering;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class ServerConnectionRegistry(IServiceProvider services,
    IOptions<GrainRoutingOptions> options, TimeProvider clock)
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, ServerConnectionFeature> connections = [];
    private bool closing;
    private Task? shutdown;

    internal async Task RunAsync(BaseConnectionContext transport, Func<Task> next)
    {
        ServerConnectionFeature feature;
        lock (gate)
        {
            if (closing || connections.Count >= options.Value.MaximumConnections)
            {
                transport.Abort(new ConnectionAbortedException(ServerConnectionProtocol.Capacity));
                return;
            }
            feature = new(transport, services, options, clock);
            connections.Add(feature.Id, feature);
            transport.Features.Set(feature);
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(next, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => feature.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => transport.Features.Set<ServerConnectionFeature>(null), failures);
        lock (gate)
        {
            connections.Remove(feature.Id);
        }
        if (failures.Count == ServerConnectionProtocol.NoFailures)
        {
            feature.Completion.TrySetResult();
        }
        else
        {
            feature.Completion.TrySetException(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Task original;
        lock (gate)
        {
            closing = true;
            shutdown ??= ShutdownCoreAsync([.. connections.Values]);
            original = shutdown;
        }
        return original.WaitAsync(cancellationToken);
    }

    private static async Task ShutdownCoreAsync(ServerConnectionFeature[] owned)
    {
        // Initialize the original task under the registry gate before invoking transport callbacks.
        await Task.Yield();
        var failures = new List<Exception>();
        foreach (var connection in owned)
        {
            ServerFailureObserver.Observe(connection.RequestClose, failures);
        }
        await ServerFailureObserver.ObserveAsync(() => Task.WhenAll(
            owned.Select(connection => connection.Completion.Task)), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
