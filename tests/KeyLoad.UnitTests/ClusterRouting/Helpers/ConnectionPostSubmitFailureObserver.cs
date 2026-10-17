using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ConnectionPostSubmitFailureObserver(ConnectionOperationObservation original)
    : IGrainRequestPhaseObserver, IGrainRequestNativePhaseObserver
{
    internal const string PrivateFailure = "post-submit-private-domain-failure";
    internal Guid RequestId
    {
        get; set;
    }
    internal bool Tagged
    {
        get; set;
    }
    internal bool Seen
    {
        get; private set;
    }

    public async ValueTask ObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        IGrainContext context, CancellationToken token)
    {
        await original.ObserveAsync(identity, phase, context, token).ConfigureAwait(true);
        if (identity.RequestId != RequestId || phase != GrainRequestPhase.SubmitReturned)
        {
            return;
        }
        Seen = true;
        throw Tagged ? GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.TypedPayloadDecode)
            : Errors.Fail(ErrorCode.Validation, PrivateFailure);
    }

    public async ValueTask ObserveNativeAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        ReplicaSiloDiscoveryState actualRuntime, CancellationToken token)
    {
        await original.ObserveNativeAsync(identity, phase, actualRuntime, token).ConfigureAwait(false);
        if (identity.RequestId != RequestId || phase != GrainRequestPhase.SubmitReturned)
        {
            return;
        }
        Seen = true;
        throw Tagged ? GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.TypedPayloadDecode)
            : Errors.Fail(ErrorCode.Validation, PrivateFailure);
    }

    public void ProducerDisposed(GrainRequestProbeIdentity identity, IGrainContext context)
        => original.ProducerDisposed(identity, context);
}
