
namespace KeyLoad.Clustering;

internal static class AnnProjectionObservationEnvelope
{
    internal static GrainRequestEnvelope Create(GrainRequestCodec scope, Guid requestId, string principalId, AnnProjectionObservationRequest payload)
    {
        var envelope = GrainRequestCapabilityEnvelopes.RemoteRead(scope, requestId, principalId,
            GrainReadKind.AnnProjectionObservation, payload);
        return envelope with
        {
            Purpose = AnnProjectionObservationProtocol.Purpose
        };
    }
}
