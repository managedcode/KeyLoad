using ManagedCode.Communication.Commands;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Retains verified identity values only, never the borrowed grain context.</summary>
internal sealed class NativeDropCommandPhaseGate(Guid commandId, GrainRequestPhase phase)
{
    private readonly Lock gate = new();
    private GrainRequestProbeIdentity? selected;
    internal Guid CommandId => commandId;
    internal GrainRequestPhase Phase => phase;
    internal TaskCompletionSource<GrainRequestProbeIdentity> Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource<Command<DatabaseCommand>> Original { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken SelectedCancellation
    {
        get; private set;
    }

    internal bool Select(GrainRequestProbeIdentity identity, CancellationToken token)
    {
        lock (gate)
        {
            if (selected is not null)
            {
                return false;
            }

            selected = identity;
            SelectedCancellation = token;
            Arrived.TrySetResult(identity);
            return true;
        }
    }

    internal void ObserveDisposed(GrainRequestProbeIdentity identity)
    {
        lock (gate)
        {
            if (selected is { } actual && actual.RequestId == identity.RequestId)
            {
                if (actual != identity)
                {
                    throw new InvalidOperationException(NativeDropRuntimeProtocol.IdentityChanged);
                }

                Disposed.TrySetResult();
            }
        }
    }
}
