using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class PartitionMovementRequestEnvelopes
{
    internal static GrainRequestEnvelope Command(DatabaseEngine database, DateTimeOffset now,
        TimeSpan requestLifetime, Guid requestId, ReplicatedOperation operation, DateTimeOffset originalExpiry)
    {
        RequireExpiry(now, requestLifetime, originalExpiry);
        var verified = database.VerifyOperationAuthority(operation);
        if (verified.Kind != OperationKind.PartitionMovementPhase)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        return new GrainRequestEnvelope
        {
            Purpose = PartitionMovementRequestScope.Purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = verified.PrincipalId,
            CommandKind = verified.Kind,
            CommandId = verified.Id,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(verified), database.Limits.MaxBatchBytes),
            ExpiresAt = originalExpiry
        };
    }

    internal static GrainRequestEnvelope Capture(DatabaseEngine database, DateTimeOffset now,
        TimeSpan requestLifetime, Guid requestId, string localPrincipalId, PartitionMovementCaptureCapability capability)
    {
        var expiresAt = capability.Verified.ExpiresAt;
        RequireExpiry(now, requestLifetime, expiresAt);
        return new GrainRequestEnvelope
        {
            Purpose = PartitionMovementRequestScope.Purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = localPrincipalId,
            ReadKind = GrainReadKind.PartitionMovementCapture,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(capability), database.Limits.MaxBatchBytes),
            ExpiresAt = expiresAt
        };
    }

    private static void RequireExpiry(DateTimeOffset now, TimeSpan requestLifetime, DateTimeOffset expiresAt)
    {
        if (expiresAt <= now || expiresAt > now + requestLifetime)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
    }
}
