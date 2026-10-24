using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed record ConnectionObservedOperation(GrainRequestProbeIdentity Identity,
    IGrainContext ConnectionContext, IGrainContext ProducerContext, GrainRequestContextState ConnectionIdentity,
    RequestWorkOwner WorkOwner)
{
    internal GrainId GrainId => ConnectionContext.GrainId;
    internal ActivationId ActivationId => ConnectionContext.ActivationId;
    internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
