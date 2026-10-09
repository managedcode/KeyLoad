using KeyLoad.Orleans;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class ServerConnectionRegistry(IServiceProvider services,
    IOptions<GrainRoutingOptions> options, TimeProvider clock)
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, ServerConnectionFeature> connections = [];
    private bool closing;

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
        lock (gate) { connections.Remove(feature.Id); }
        if (failures.Count == ServerConnectionProtocol.NoFailures) { feature.Completion.TrySetResult(); }
        else { feature.Completion.TrySetException(failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        ServerConnectionFeature[] owned;
        lock (gate)
        {
            closing = true;
            owned = [.. connections.Values];
        }
        foreach (var connection in owned) { connection.RequestClose(); }
        await Task.WhenAll(owned.Select(connection => connection.Completion.Task))
            .WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
