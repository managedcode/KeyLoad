namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreateAnnProjectionObservation(Guid requestId, string principalId,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        var envelope = GrainRequestCapabilityEnvelopes.RemoteRead(database, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, principalId, GrainReadKind.AnnProjectionObservation,
            payload, expiresAt);
        return Issue(envelope with { Purpose = AnnProjectionObservationProtocol.Purpose });
    }
}
