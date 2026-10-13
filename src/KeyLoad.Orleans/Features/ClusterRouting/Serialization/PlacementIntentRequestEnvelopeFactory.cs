using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class PlacementIntentRequestEnvelopeFactory
{
    internal static GrainRequestEnvelope Create(DatabaseEngine database, TimeProvider clock, TimeSpan requestLifetime,
        Guid requestId, ReplicatedOperation operation, DateTimeOffset originalExpiry)
    {
        var now = clock.GetUtcNow();
        if (originalExpiry <= now || originalExpiry > now + requestLifetime)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var verified = database.VerifyOperationAuthority(operation);
        if (verified.Kind != OperationKind.PlacementIntentPhase)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        return new GrainRequestEnvelope
        {
            Purpose = PlacementIntentNativeProtocol.Purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = verified.PrincipalId,
            CommandId = verified.Id,
            CommandKind = verified.Kind,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(verified), database.Limits.MaxBatchBytes),
            ExpiresAt = originalExpiry
        };
    }
}

