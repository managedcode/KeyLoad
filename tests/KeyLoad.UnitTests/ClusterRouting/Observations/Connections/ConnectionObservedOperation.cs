using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record ConnectionObservedOperation(GrainRequestProbeIdentity Identity, GrainId GrainId,
    ActivationId ActivationId)
{
    internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
