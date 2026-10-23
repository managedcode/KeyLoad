namespace KeyLoad.Clustering;

public sealed partial class GrainRequestCodec
{
    internal GrainRequestEnvelope CreateAnnProjectionObservation(Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
        => Issue(AnnProjectionObservationEnvelope.Create(this, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, principalId, payload, expiresAt));
}
