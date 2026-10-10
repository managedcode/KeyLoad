using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class EventVectorReadEnvelope
{
    internal static GrainRequestEnvelope Create(DatabaseEngine database, TimeProvider clock,
        TimeSpan requestLifetime, Guid requestId, string principalId, GrainReadKind kind,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        if (!EventVectorRequestScope.HandlesRead(kind))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var original = GrainRequestCapabilityEnvelopes.RemoteRead(database, clock.GetUtcNow(),
            requestLifetime, requestId, principalId, kind, payload, expiresAt);
        return original with { Purpose = EventVectorRequestScope.SourcePurpose };
    }
}
