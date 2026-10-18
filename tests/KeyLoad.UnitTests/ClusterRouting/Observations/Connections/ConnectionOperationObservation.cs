using System.Collections.Concurrent;
using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionOperationObservation : IGrainRequestPhaseObserver, IGrainRequestNativePhaseObserver,
    IIncomingGrainCallFilter
{
    private readonly ConcurrentDictionary<Guid, ConnectionObservedOperation> operations = new();
    private readonly ConcurrentDictionary<Guid, ConnectionOperationGate> gates = new();
    private readonly ConcurrentDictionary<Guid, IGrainContext> connections = new();

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is ConnectionGrain connection
            && context.MethodName == nameof(IConnectionGrain.ExecuteStreamAsync))
        {
            connections[connection.GetPrimaryKey()] = context.TargetContext;
        }
        await context.Invoke().ConfigureAwait(true);
    }

    internal ConnectionOperationGate Hold(Guid requestId, GrainRequestPhase phase = GrainRequestPhase.RequestStarted,
        bool fail = false)
    {
        var gate = new ConnectionOperationGate(phase, fail);
        if (!gates.TryAdd(requestId, gate))
        {
            throw new InvalidOperationException(ConnectionNativeProtocol.DuplicateObservation);
        }
        return gate;
    }

    public async ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
    {
        if (phase == GrainRequestPhase.RequestStarted)
        {
            if (context.GrainInstance is not RequestWorkerGrain
                || RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState state
                || state.RequestId != identity.RequestId || state.CommandId != identity.CommandId
                || !connections.TryGetValue(state.ConnectionId, out var connection)
                || connection.GrainInstance is not ConnectionGrain owner || owner.GetPrimaryKey() != state.ConnectionId)
            {
                throw new InvalidOperationException(ConnectionNativeProtocol.IdentityChanged);
            }
            var workOwner = context.ActivationServices.GetRequiredService<Func<NativeRequestWorkOwner>>()();
            var observed = new ConnectionObservedOperation(identity, connection, context, state, workOwner);
            if (!operations.TryAdd(identity.RequestId, observed))
            {
                throw new InvalidOperationException(ConnectionNativeProtocol.DuplicateObservation);
            }
        }
        await ObserveGateAsync(identity, phase, cancellationToken).ConfigureAwait(true);
    }

    public ValueTask ObserveNativeAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        SiloStorageIdentityProvider actualRuntime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actualRuntime);
        cancellationToken.ThrowIfCancellationRequested();
        if (phase == GrainRequestPhase.RequestStarted)
        {
            throw new InvalidOperationException(ConnectionNativeProtocol.IdentityChanged);
        }
        return ObserveGateAsync(identity, phase, cancellationToken);
    }

    private async ValueTask ObserveGateAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        CancellationToken cancellationToken)
    {
        if (!gates.TryGetValue(identity.RequestId, out var gate) || phase != gate.Phase)
        {
            return;
        }
        gate.Arrived.TrySetResult();
        if (gate.Fail)
        {
            throw new IOException(ConnectionNativeProtocol.OrdinaryFailure);
        }
        await gate.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        if (operations.TryGetValue(identity.RequestId, out var observed))
        {
            if (observed.Identity != identity || observed.WorkerContext.GrainId != context.GrainId
                || observed.WorkerContext.ActivationId != context.ActivationId)
            {
                throw new InvalidOperationException(ConnectionNativeProtocol.IdentityChanged);
            }
            observed.Disposed.TrySetResult();
        }
    }

    internal ConnectionObservedOperation Read(Guid requestId)
        => operations.TryGetValue(requestId, out var result) ? result
            : throw new InvalidOperationException(ConnectionNativeProtocol.MissingObservation);

    internal ConnectionObservedOperation ForCommand(Guid commandId)
        => operations.Values.Single(observed => observed.Identity.CommandId == commandId);

    internal async Task JoinAsync(List<Exception> failures, CancellationToken cancellationToken)
    {
        foreach (var observed in operations.OrderBy(static pair => pair.Key))
        {
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(
                () => observed.Value.Disposed.Task.WaitAsync(cancellationToken), failures);
        }
    }

    internal void WriteTrace(string phase)
    {
        foreach (var observed in operations.OrderBy(static pair => pair.Key))
        {
            Console.WriteLine(FormattableString.Invariant(
                $"ConnectionNative phase={phase} request={observed.Key:D} connection={observed.Value.GrainId} activation={observed.Value.ActivationId} worker={observed.Value.WorkerContext.GrainId} workerActivation={observed.Value.WorkerContext.ActivationId} producerDisposed={observed.Value.Disposed.Task.Status}"));
        }
    }

    internal void ReleaseAll()
    {
        foreach (var gate in gates.Values)
        {
            gate.Release.TrySetResult();
        }
    }
}
