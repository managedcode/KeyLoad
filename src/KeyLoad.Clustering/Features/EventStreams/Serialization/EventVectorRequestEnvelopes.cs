using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class EventVectorRequestEnvelopes
{
    internal static GrainRequestEnvelope CreatePhase(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, ReplicatedOperation operation, DateTimeOffset originalExpiry)
        => operation.Kind switch
        {
            OperationKind.EventFeedSourcePhase => SourcePhase(database, now, lifetime, requestId, operation, originalExpiry),
            OperationKind.EventFeedControl => ControlPhase(database, now, lifetime, requestId, operation, originalExpiry),
            _ => throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired)
        };

    internal static GrainRequestEnvelope SourcePhase(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, ReplicatedOperation operation, DateTimeOffset originalExpiry)
        => Phase(database, now, lifetime, requestId, operation, originalExpiry,
            OperationKind.EventFeedSourcePhase, EventVectorRequestScope.SourcePurpose);

    internal static GrainRequestEnvelope ControlPhase(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, ReplicatedOperation operation, DateTimeOffset originalExpiry)
        => Phase(database, now, lifetime, requestId, operation, originalExpiry,
            OperationKind.EventFeedControl, EventVectorRequestScope.ControlPurpose);

    private static GrainRequestEnvelope Phase(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, ReplicatedOperation operation, DateTimeOffset originalExpiry,
        OperationKind expectedKind, string purpose)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(operation);
        OnlineTextRequestEnvelopes.RequireExpiry(now, lifetime, originalExpiry);
        var verified = database.VerifyOperationAuthority(operation);
        if (verified.Kind != expectedKind)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return new()
        {
            Purpose = purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = verified.PrincipalId,
            CommandKind = verified.Kind,
            CommandId = verified.Id,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(verified), database.Limits.MaxBatchBytes),
            ExpiresAt = originalExpiry
        };
    }
}
