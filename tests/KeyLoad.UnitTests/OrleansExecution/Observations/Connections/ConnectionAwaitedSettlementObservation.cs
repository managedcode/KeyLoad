using System.Collections.Concurrent;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class ConnectionAwaitedSettlementObservation(ConnectionOperationObservation original)
    : IGrainRequestPhaseObserver, RuntimeIGrainRequestPhaseObserver, IGrainRequestProducerDisposedObserver
{
    private readonly ConcurrentDictionary<Guid, ConnectionAwaitedCallback> callbacks = new();

    internal ConnectionAwaitedCallback Hold(Guid requestId, Exception? failure = null)
    {
        var callback = new ConnectionAwaitedCallback(failure);
        if (!callbacks.TryAdd(requestId, callback))
        {
            throw Invalid();
        }
        return callback;
    }

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken token)
        => original.ObserveAsync(identity, phase, context, token);

    public ValueTask RuntimeObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        SiloStorageIdentityProvider actualRuntime, CancellationToken token)
        => original.RuntimeObserveAsync(identity, phase, actualRuntime, token);

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
        => original.ProducerDisposed(identity, context);

    public ValueTask ProducerDisposedAsync(GrainRequestProbeIdentity identity, Guid connectionId, IGrainContext context)
    {
        var observed = original.Read(identity.RequestId);
        if (observed.ConnectionIdentity.ConnectionId != connectionId
            || observed.ConnectionContext.GrainInstance is not ConnectionGrain connection
            || connection.GetPrimaryKey() != connectionId)
        {
            throw Invalid();
        }
        original.ProducerDisposed(identity, context);
        return callbacks.TryGetValue(identity.RequestId, out var callback) ? callback.NotifyAsync(connection.AdmissionClosedToken) : ValueTask.CompletedTask;
    }

    internal GrainRequestContextState Identity(Guid requestId) => original.Read(requestId).ConnectionIdentity;
    internal Task Deactivated(Guid requestId) => original.Read(requestId).ConnectionContext.Deactivated;
    internal void ReleaseAll()
    {
        foreach (var callback in callbacks.Values)
        {
            callback.Release.TrySetResult();
        }
        original.ReleaseAll();
    }
    private static InvalidOperationException Invalid() => new(ConnectionProtocol.IdentityChanged);
}
