using KeyLoad.Core;
using KeyLoad.Orleans.Features.Search;

namespace KeyLoad.Orleans;

internal static class OnlineTextRequestEnvelopes
{
    internal static GrainRequestEnvelope Publication(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, ReplicatedOperation operation, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        var verified = database.VerifyOperationAuthority(operation);
        if (verified.Kind != OperationKind.OnlineTextPublicationPhase)
        { throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired); }
        return new()
        {
            Purpose = OnlineTextRequestScope.Purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = verified.PrincipalId,
            CommandKind = verified.Kind,
            CommandId = verified.Id,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(verified), database.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
    }

    internal static GrainRequestEnvelope Capability(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, string principal, OnlineTextCapabilityRequest request,
        DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        return GrainRequestEnvelopeConstruction.Read(requestId, database.Store.Identity.Incarnation,
            principal, GrainReadKind.OnlineTextMaintenance,
            GrainNativePayload.Copy(NativeSerialization.Serialize(request), database.Limits.MaxBatchBytes), expiry)
            with
        { Purpose = OnlineTextRequestScope.Purpose };
    }

    internal static GrainRequestEnvelope ProjectionRead(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid actor, string principal, ReadProjectionBatchRequest request, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        return GrainRequestEnvelopeConstruction.Read(actor, database.Store.Identity.Incarnation, principal,
            GrainReadKind.ProjectionBatch,
            GrainNativePayload.Copy(NativeSerialization.Serialize(request), database.Limits.MaxBatchBytes), expiry);
    }

    internal static GrainRequestEnvelope Checkpoint(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid actor, string principal, CommitProjectionBatchRequest request, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        return GrainRequestEnvelopeConstruction.Command(actor, database.Store.Identity.Incarnation, principal,
            OperationKind.CommitProjectionBatch, request.CommandId,
            GrainNativePayload.Copy(NativeSerialization.Serialize(request), database.Limits.MaxBatchBytes), expiry);
    }

    internal static void RequireExpiry(DateTimeOffset now, TimeSpan lifetime, DateTimeOffset expiry)
    {
        if (expiry <= now || expiry > now + lifetime)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
    }
}
