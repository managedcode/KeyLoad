
namespace KeyLoad.Clustering;

internal static class GrainRequestCapabilityEnvelopes
{
    internal static GrainRequestEnvelope RemoteRead(GrainRequestCodec scope, DateTimeOffset now,
        TimeSpan requestLifetime, Guid requestId, string principalId, GrainReadKind kind,
        ReadOnlyMemory<byte> payload, DateTimeOffset expiresAt)
    {
        if (expiresAt <= now || expiresAt > now + requestLifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        return new GrainRequestEnvelope
        {
            Purpose = GrainNativeContracts.RequestPurpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = GrainNativePayload.Copy(payload, scope.Limits.MaxBatchBytes),
            ExpiresAt = expiresAt
        };
    }

    internal static GrainRequestEnvelope RuntimeJournalRead(GrainRequestCodec scope, TimeProvider clock,
        TimeSpan requestLifetime, Guid requestId, string principalId, GrainReadKind kind, ReadOnlyMemory<byte> payload)
        => new()
        {
            Purpose = GrainNativeContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = principalId,
            ReadKind = kind,
            Payload = GrainNativePayload.Copy(payload, scope.Limits.MaxBatchBytes),
            ExpiresAt = clock.GetUtcNow() + requestLifetime
        };

    internal static GrainRequestEnvelope RuntimeJournalCommand(GrainRequestCodec scope, TimeProvider clock,
        TimeSpan requestLifetime, Guid requestId, string principalId, Guid commandId, ReadOnlyMemory<byte> payload)
        => new()
        {
            Purpose = GrainNativeContracts.RuntimeJournalPurpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = principalId,
            CommandKind = OperationKind.RuntimeJournal,
            CommandId = commandId,
            Payload = GrainNativePayload.Copy(payload, scope.Limits.MaxBatchBytes),
            ExpiresAt = clock.GetUtcNow() + requestLifetime
        };
}
