using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Orleans;

internal static class RemoteTransferRequestEnvelopes
{
    internal static GrainRequestEnvelope Command(DatabaseEngine database, DateTimeOffset now,
        TimeSpan requestLifetime, Guid requestId, ReplicatedOperation operation, DateTimeOffset expiry)
    {
        RequireExpiry(now, requestLifetime, expiry);
        var verified = database.VerifyOperationAuthority(operation);
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(verified.NativePayload.Span);
        if (verified.Kind != OperationKind.Batch || payload.TransferProof.IsEmpty)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        return new()
        {
            Purpose = RemoteTransferPeerProtocol.GrainPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = verified.PrincipalId,
            CommandKind = verified.Kind,
            CommandId = verified.Id,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(verified), database.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
    }

    internal static GrainRequestEnvelope Read(DatabaseEngine database, DateTimeOffset now,
        TimeSpan requestLifetime, Guid requestId, RemoteTransferNativeProof proof)
    {
        RequireExpiry(now, requestLifetime, proof.Call.ExpiresAt);
        return new()
        {
            Purpose = RemoteTransferPeerProtocol.GrainPurpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = proof.TechnicalPrincipalId,
            ReadKind = GrainReadKind.QueueTransferCoordination,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(proof), database.Limits.MaxBatchBytes),
            ExpiresAt = proof.Call.ExpiresAt
        };
    }

    private static void RequireExpiry(DateTimeOffset now, TimeSpan lifetime, DateTimeOffset expiry)
    {
        if (expiry <= now || expiry > now + lifetime)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
    }
}
