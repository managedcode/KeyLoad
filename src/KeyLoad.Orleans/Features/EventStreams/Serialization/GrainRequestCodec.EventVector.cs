namespace KeyLoad.Orleans;

public sealed partial class GrainRequestCodec
{
    internal string CreateEventFeedControlParent(Guid requestId, string principalId, EventFeedControlRequest request)
        => Issue(EventVectorParentEnvelope.Create(database, clock.GetUtcNow(), settings.RequestLifetime,
            requestId, principalId, request));

    internal string CreateEventVectorPhase(Guid requestId, ReplicatedOperation operation,
        DateTimeOffset originalExpiry)
        => Issue(EventVectorRequestEnvelopes.CreatePhase(database, clock.GetUtcNow(),
            settings.RequestLifetime, requestId, operation, originalExpiry));

    internal string CreateEventVectorRead(Guid requestId, string principalId, GrainReadKind kind,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
        => Issue(EventVectorReadEnvelope.Create(database, clock, settings.RequestLifetime,
            requestId, principalId, kind, payload, expiresAt));
}
