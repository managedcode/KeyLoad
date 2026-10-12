using System.Collections.Concurrent;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionAwaitedSettlementObservation(ConnectionOperationObservation original)
    : IGrainRequestPhaseObserver, IGrainRequestProducerDisposedObserver
{
    private readonly ConcurrentDictionary<Guid, ConnectionAwaitedCallback> callbacks = new();
    private readonly ConcurrentDictionary<Guid, GrainRequestContextState> identities = new();

    internal ConnectionAwaitedCallback Hold(Guid requestId, Exception? failure = null)
    {
        var callback = new ConnectionAwaitedCallback(failure);
        if (!callbacks.TryAdd(requestId, callback))
        { throw Invalid(); }
        return callback;
    }

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken token)
    {
        if (phase == GrainRequestPhase.RequestStarted)
        {
            if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState state
                || state.RequestId != identity.RequestId || state.CommandId != identity.CommandId
                || context.GrainInstance is not ConnectionGrain connection || connection.GetPrimaryKey() != state.ConnectionId
                || !identities.TryAdd(identity.RequestId, state))
            { throw Invalid(); }
        }
        return original.ObserveAsync(identity, phase, context, token);
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
        => original.ProducerDisposed(identity, context);

    public ValueTask ProducerDisposedAsync(GrainRequestProbeIdentity identity, Guid connectionId, IGrainContext context)
    {
        if (!identities.TryGetValue(identity.RequestId, out var state) || state.ConnectionId != connectionId
            || context.GrainInstance is not ConnectionGrain connection || connection.GetPrimaryKey() != connectionId)
        { throw Invalid(); }
        original.ProducerDisposed(identity, context);
        return callbacks.TryGetValue(identity.RequestId, out var callback) ? callback.NotifyAsync() : ValueTask.CompletedTask;
    }

    internal GrainRequestContextState Identity(Guid requestId) => identities[requestId];
    internal void ReleaseAll()
    {
        foreach (var callback in callbacks.Values)
        { callback.Release.TrySetResult(); }
        original.ReleaseAll();
    }
    private static InvalidOperationException Invalid() => new(ConnectionNativeProtocol.IdentityChanged);
}
