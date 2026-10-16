namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal ValueTask ObserveNativePhaseAsync(DecodedGrainRequest request, GrainRequestPhase phase,
        ReplicaSiloDiscoveryState actualRuntime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actualRuntime);
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(phase) || phase == GrainRequestPhase.RequestStarted)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        if (PhaseObserver is null)
        {
            return ValueTask.CompletedTask;
        }
        if (PhaseObserver is not IGrainRequestNativePhaseObserver native)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
        }
        return native.ObserveNativeAsync(GrainRequestProbeIdentity.From(request.Envelope), phase,
            actualRuntime, cancellationToken);
    }
}
