using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class GrainRequestCapabilityEnvelopes
{
    internal static GrainRequestEnvelope RemoteRead(DatabaseEngine database, DateTimeOffset now,
        TimeSpan requestLifetime, Guid requestId, string principalId, GrainReadKind kind,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        if (expiresAt <= now || expiresAt > now + requestLifetime)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        return new GrainRequestEnvelope
        {
            Purpose = GrainNativeContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = GrainNativePayload.Copy(payload, database.Limits.MaxBatchBytes),
            ExpiresAt = expiresAt
        };
    }

    internal static GrainRequestEnvelope RuntimeJournalRead(DatabaseEngine database, TimeProvider clock,
        TimeSpan requestLifetime, Guid requestId, string principalId, GrainReadKind kind, ReadOnlyMemory<byte> payload)
        => new()
        {
            Purpose = GrainNativeContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = GrainNativePayload.Copy(payload, database.Limits.MaxBatchBytes),
            ExpiresAt = clock.GetUtcNow() + requestLifetime
        };

    internal static GrainRequestEnvelope RuntimeJournalCommand(DatabaseEngine database, TimeProvider clock,
        TimeSpan requestLifetime, Guid requestId, string principalId, Guid commandId, ReadOnlyMemory<byte> payload)
        => new()
        {
            Purpose = GrainNativeContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = principalId,
            CommandKind = OperationKind.RuntimeJournal,
            CommandId = commandId,
            Payload = GrainNativePayload.Copy(payload, database.Limits.MaxBatchBytes),
            ExpiresAt = clock.GetUtcNow() + requestLifetime
        };
}
