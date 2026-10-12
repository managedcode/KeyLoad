using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class Bm25RequestEnvelopes
{
    internal static GrainRequestEnvelope Publication(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, ReplicatedOperation operation, DateTimeOffset expiry)
    {
        if (expiry <= now || expiry > now + lifetime)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        var verified = database.VerifyOperationAuthority(operation);
        if (verified.Kind != OperationKind.Bm25CorpusPublicationPhase)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        return new()
        {
            Purpose = Bm25RequestScope.Purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = verified.PrincipalId,
            CommandKind = verified.Kind,
            CommandId = verified.Id,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(verified), database.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
    }
}
