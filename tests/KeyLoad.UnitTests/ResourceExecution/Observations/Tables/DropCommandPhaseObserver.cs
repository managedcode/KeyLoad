using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.ResourceExecution;

/// <summary>Selects execution before-submit or returned-command phases without replacing the production operation.</summary>
internal sealed class DropCommandPhaseObserver : IGrainRequestPhaseObserver, RuntimeIGrainRequestPhaseObserver
{
    private readonly ConcurrentDictionary<Guid, DropCommandPhaseGate> holds = new();

    internal DropCommandPhaseGate Hold(Guid commandId, GrainRequestPhase phase = GrainRequestPhase.SubmitReturned,
        Exception? failure = null)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException(DropRuntimeProtocol.InvalidCommand, nameof(commandId));
        }
        var hold = new DropCommandPhaseGate(commandId, phase, failure);
        if (!holds.TryAdd(commandId, hold))
        {
            throw new InvalidOperationException(DropRuntimeProtocol.DuplicateHold);
        }
        return hold;
    }

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
        => ObserveGateAsync(identity, phase, cancellationToken);

    public ValueTask RuntimeObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        SiloStorageIdentityProvider actualRuntime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actualRuntime);
        cancellationToken.ThrowIfCancellationRequested();
        if (phase == GrainRequestPhase.RequestStarted)
        {
            throw new InvalidOperationException(DropRuntimeProtocol.IdentityChanged);
        }
        return ObserveGateAsync(identity, phase, cancellationToken);
    }

    private async ValueTask ObserveGateAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        CancellationToken cancellationToken)
    {
        if (!holds.TryGetValue(identity.CommandId, out var hold) || hold.Phase != phase || !hold.Select(identity))
        {
            return;
        }
        try
        {
            await hold.Released.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
            if (hold.Failure is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            hold.Settled.TrySetResult();
        }
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
    {
        if (holds.TryGetValue(identity.CommandId, out var hold))
        {
            hold.ObserveDisposed(identity);
        }
    }

    internal async Task JoinSelectedAsync(CancellationToken token)
    {
        foreach (var hold in holds.Values)
        {
            if (hold.Arrived.Task.IsCompletedSuccessfully)
            {
                await hold.Settled.Task.WaitAsync(token).ConfigureAwait(false);
                await hold.Disposed.Task.WaitAsync(token).ConfigureAwait(false);
            }
        }
    }

    internal void ReleaseAll()
    {
        foreach (var hold in holds.Values)
        {
            hold.Released.TrySetResult();
        }
    }
}
