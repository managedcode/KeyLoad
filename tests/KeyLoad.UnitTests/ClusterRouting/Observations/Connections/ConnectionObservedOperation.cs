using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed record ConnectionObservedOperation(GrainRequestProbeIdentity Identity,
    IGrainContext ConnectionContext, IGrainContext ProducerContext, GrainRequestContextState ConnectionIdentity,
    NativeRequestWorkOwner WorkOwner)
{
    internal GrainId GrainId => ConnectionContext.GrainId;
    internal ActivationId ActivationId => ConnectionContext.ActivationId;
    internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
