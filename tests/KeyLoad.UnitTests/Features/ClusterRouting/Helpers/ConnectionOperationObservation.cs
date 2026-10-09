using System.Collections.Concurrent;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionOperationObservation : IGrainRequestPhaseObserver
{
    private readonly ConcurrentDictionary<Guid, ConnectionObservedOperation> operations = new();
    private readonly ConcurrentDictionary<Guid, ConnectionOperationGate> gates = new();

    internal ConnectionOperationGate Hold(Guid requestId, GrainRequestPhase phase = GrainRequestPhase.RequestStarted,
        bool fail = false)
    {
        var gate = new ConnectionOperationGate(phase, fail);
        if (!gates.TryAdd(requestId, gate))
        { throw new InvalidOperationException(ConnectionNativeProtocol.DuplicateObservation); }
        return gate;
    }

    public async ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
    {
        if (phase == GrainRequestPhase.RequestStarted)
        {
            var observed = new ConnectionObservedOperation(identity, context.GrainId, context.ActivationId);
            if (!operations.TryAdd(identity.RequestId, observed))
            { throw new InvalidOperationException(ConnectionNativeProtocol.DuplicateObservation); }
        }
        if (!gates.TryGetValue(identity.RequestId, out var gate) || phase != gate.Phase)
        { return; }
        gate.Arrived.TrySetResult();
        if (gate.Fail)
        { throw new IOException(ConnectionNativeProtocol.OrdinaryFailure); }
        await gate.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        if (operations.TryGetValue(identity.RequestId, out var observed))
        {
            if (observed.Identity != identity || observed.GrainId != context.GrainId
                || observed.ActivationId != context.ActivationId)
            { throw new InvalidOperationException(ConnectionNativeProtocol.IdentityChanged); }
            observed.Disposed.TrySetResult();
        }
    }

    internal ConnectionObservedOperation Read(Guid requestId)
        => operations.TryGetValue(requestId, out var result) ? result
            : throw new InvalidOperationException(ConnectionNativeProtocol.MissingObservation);

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
                $"ConnectionNative phase={phase} request={observed.Key:D} grain={observed.Value.GrainId} activation={observed.Value.ActivationId} producerDisposed={observed.Value.Disposed.Task.Status}"));
        }
    }

    internal void ReleaseAll()
    {
        foreach (var gate in gates.Values)
        { gate.Release.TrySetResult(); }
    }
}

internal sealed record ConnectionObservedOperation(GrainRequestProbeIdentity Identity, GrainId GrainId,
    ActivationId ActivationId)
{
    internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed record ConnectionOperationGate(GrainRequestPhase Phase, bool Fail)
{
    internal TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
