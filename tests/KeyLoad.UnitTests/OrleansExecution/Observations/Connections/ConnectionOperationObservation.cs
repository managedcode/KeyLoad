using System.Collections.Concurrent;
using KeyLoad.Clustering;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class ConnectionOperationObservation : IGrainRequestPhaseObserver, RuntimeIGrainRequestPhaseObserver,
    IIncomingGrainCallFilter
{
    private readonly ConcurrentDictionary<Guid, ConnectionObservedOperation> operations = new();
    private readonly ConcurrentDictionary<Guid, ConnectionOperationGate> gates = new();
    private readonly ConcurrentDictionary<Guid, IGrainContext> connections = new();

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is ConnectionGrain connection)
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
            throw new InvalidOperationException(ConnectionProtocol.DuplicateObservation);
        }
        return gate;
    }

    public async ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
    {
        if (phase == GrainRequestPhase.RequestStarted)
        {
            if (context.GrainInstance is not ConnectionGrain producer)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            if (RequestContext.Get(GrainRequestStreamProtocol.ContextKey) is not GrainRequestContextState state)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            if (state.RequestId != identity.RequestId)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            if (state.CommandId != identity.CommandId)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            if (!connections.TryGetValue(state.ConnectionId, out var connection))
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            if (connection.GrainInstance is not ConnectionGrain owner)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            if (owner.GetPrimaryKey() != state.ConnectionId)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            if (producer.GetPrimaryKey() != state.ConnectionId)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            if (connection.GrainId != context.GrainId)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            if (connection.ActivationId != context.ActivationId)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            var workOwner = context.ActivationServices.GetRequiredService<Func<RequestWorkOwner>>()();
            var observed = new ConnectionObservedOperation(identity, connection, context, state, workOwner);
            if (!operations.TryAdd(identity.RequestId, observed))
            {
                throw new InvalidOperationException(ConnectionProtocol.DuplicateObservation);
            }
        }
        await ObserveGateAsync(identity, phase, cancellationToken).ConfigureAwait(true);
    }

    public ValueTask RuntimeObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        SiloStorageIdentityProvider actualRuntime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actualRuntime);
        cancellationToken.ThrowIfCancellationRequested();
        if (phase == GrainRequestPhase.RequestStarted)
        {
            throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
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
            throw new IOException(ConnectionProtocol.OrdinaryFailure);
        }
        await gate.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        if (operations.TryGetValue(identity.RequestId, out var observed))
        {
            if (observed.Identity != identity || observed.ProducerContext.GrainId != context.GrainId
                || observed.ProducerContext.ActivationId != context.ActivationId)
            {
                throw new InvalidOperationException(ConnectionProtocol.IdentityChanged);
            }
            observed.Disposed.TrySetResult();
        }
    }

    internal ConnectionObservedOperation Read(Guid requestId)
        => operations.TryGetValue(requestId, out var result) ? result
            : throw new InvalidOperationException(ConnectionProtocol.MissingObservation);

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
                $"RuntimeConnection phase={phase} request={observed.Key:D} connection={observed.Value.GrainId} activation={observed.Value.ActivationId} producer={observed.Value.ProducerContext.GrainId} producerActivation={observed.Value.ProducerContext.ActivationId} producerDisposed={observed.Value.Disposed.Task.Status}"));
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
