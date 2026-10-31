namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateAnnProjectionObservation(Guid requestId, string principalId, object payload)
        => Issue(AnnProjectionObservationEnvelope.Create(this, requestId, principalId, payload));
}
