using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.OrleansExecution;

internal sealed class ConnectionPostSubmitFailureObserver(ConnectionOperationObservation original)
    : IGrainRequestPhaseObserver, RuntimeIGrainRequestPhaseObserver
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

    public async ValueTask RuntimeObserveAsync(GrainRequestProbeIdentity identity, GrainRequestPhase phase,
        SiloStorageIdentityProvider actualRuntime, CancellationToken token)
    {
        await original.RuntimeObserveAsync(identity, phase, actualRuntime, token).ConfigureAwait(false);
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
