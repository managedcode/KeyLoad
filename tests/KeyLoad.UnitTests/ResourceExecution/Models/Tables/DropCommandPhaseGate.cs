using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Retains the actual selected command identity and cancellation through execution producer settlement.</summary>
internal sealed class DropCommandPhaseGate(Guid commandId, GrainRequestPhase phase, Exception? failure)
{
    private readonly Lock gate = new();
    private GrainRequestProbeIdentity? selected;
    internal GrainRequestPhase Phase => phase;
    internal Exception? Failure => failure;
    internal TaskCompletionSource<GrainRequestProbeIdentity> Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Settled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal bool Select(GrainRequestProbeIdentity identity)
    {
        lock (gate)
        {
            if (identity.CommandId != commandId)
            {
                throw new InvalidOperationException(DropRuntimeProtocol.IdentityChanged);
            }
            if (selected is not null)
            {
                return false;
            }
            selected = identity;
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
                    throw new InvalidOperationException(DropRuntimeProtocol.IdentityChanged);
                }
                Disposed.TrySetResult();
            }
        }
    }
}
