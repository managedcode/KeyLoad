using System.Collections.Concurrent;
using KeyLoad.Clustering;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Test-only command-ID selection on the existing real native request observer.</summary>
internal sealed class NativeDropCommandPhaseObserver : IGrainRequestPhaseObserver, IGrainRequestNativePhaseObserver, IGrainNativeOperationPhaseObserver
{
    private readonly ConcurrentDictionary<Guid, NativeDropCommandPhaseGate> holds = new();

    internal NativeDropCommandPhaseGate Hold(Guid commandId, GrainRequestPhase phase = GrainRequestPhase.SubmitReturned)
    {
        if (commandId == Guid.Empty)
        {
            throw new ArgumentException(NativeDropRuntimeProtocol.InvalidCommand, nameof(commandId));
        }

        var hold = new NativeDropCommandPhaseGate(commandId, phase);
        if (!holds.TryAdd(commandId, hold))
        {
            throw new InvalidOperationException(NativeDropRuntimeProtocol.DuplicateHold);
        }

        return hold;
    }

    public ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken cancellationToken)
        => ObserveGateAsync(identity, phase, cancellationToken);

    public ValueTask ObserveNativeAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        ReplicaSiloDiscoveryState actualRuntime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actualRuntime);
        cancellationToken.ThrowIfCancellationRequested();
        if (phase == GrainRequestPhase.RequestStarted)
        {
            throw new InvalidOperationException(NativeDropRuntimeProtocol.IdentityChanged);
        }
        return ObserveGateAsync(identity, phase, cancellationToken);
    }

    private async ValueTask ObserveGateAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        CancellationToken cancellationToken)
    {
        if (!holds.TryGetValue(identity.CommandId, out var hold) || hold.Phase != phase || !hold.Select(identity, cancellationToken))
        {
            return;
        }

        await hold.Released.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
    }

    public void ObserveNativeOperation(GrainRequestProbeIdentity identity, Command<DatabaseCommand> original)
    {
        if (identity.CommandId != original.CommandId || identity.PrincipalId != original.UserId!)
        {
            throw new InvalidOperationException(NativeDropRuntimeProtocol.IdentityChanged);
        }

        if (holds.TryGetValue(identity.CommandId, out var hold))
        {
            hold.Original.TrySetResult(original);
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
                await hold.Disposed.Task.WaitAsync(token).ConfigureAwait(false);
            }
        }
    }

    internal void Release(Guid commandId)
    {
        if (!holds.TryGetValue(commandId, out var hold))
        {
            throw new InvalidOperationException(NativeDropRuntimeProtocol.MissingHold);
        }

        hold.Released.TrySetResult();
    }

    internal void ReleaseAll()
    {
        foreach (var hold in holds.Values)
        {
            hold.Released.TrySetResult();
        }
    }
}
