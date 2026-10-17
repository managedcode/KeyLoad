using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class AnnProjectionObservationEnvelope
{
    internal static GrainRequestEnvelope Create(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, string principalId, ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        var envelope = GrainRequestCapabilityEnvelopes.RemoteRead(database, now,
            lifetime, requestId, principalId, GrainReadKind.AnnProjectionObservation, payload, expiresAt);
        return envelope with
        {
            Purpose = AnnProjectionObservationProtocol.Purpose
        };
    }
}
