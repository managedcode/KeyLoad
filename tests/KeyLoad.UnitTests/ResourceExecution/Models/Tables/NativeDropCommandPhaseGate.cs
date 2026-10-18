using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

/// <summary>Retains the actual selected command identity and cancellation through native producer settlement.</summary>
internal sealed class NativeDropCommandPhaseGate(Guid commandId, GrainRequestPhase phase, Exception? failure)
{
    private readonly Lock gate = new();
    private GrainRequestProbeIdentity? selected;
    internal GrainRequestPhase Phase => phase;
    internal Exception? Failure => failure;
    internal TaskCompletionSource<GrainRequestProbeIdentity> Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal CancellationToken SelectedCancellation { get; private set; }

    internal bool Select(GrainRequestProbeIdentity identity, CancellationToken token)
    {
        lock (gate)
        {
            if (identity.CommandId != commandId)
            {
                throw new InvalidOperationException(NativeDropRuntimeProtocol.IdentityChanged);
            }
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
