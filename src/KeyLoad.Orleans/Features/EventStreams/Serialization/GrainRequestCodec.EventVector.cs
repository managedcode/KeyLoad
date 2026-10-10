namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreateEventVectorRead(Guid requestId, string principalId, GrainReadKind kind,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
        => Issue(EventVectorReadEnvelope.Create(database, clock, settings.RequestLifetime,
            requestId, principalId, kind, payload, expiresAt));
}
