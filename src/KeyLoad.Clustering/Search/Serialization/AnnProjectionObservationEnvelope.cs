
namespace KeyLoad.Clustering;

internal static class AnnProjectionObservationEnvelope
{
    internal static GrainRequestEnvelope Create(GrainRequestCodec scope, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, string principalId, object payload, DateTimeOffset expiresAt)
    {
        var envelope = GrainRequestCapabilityEnvelopes.RemoteRead(scope, now,
            lifetime, requestId, principalId, GrainReadKind.AnnProjectionObservation, payload, expiresAt);
        return envelope with
        {
            Purpose = AnnProjectionObservationProtocol.Purpose
        };
    }
}
