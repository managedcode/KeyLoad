using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Clustering.Features.Search;
using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class OnlineTextRequestEnvelopes
{
    internal static GrainRequestEnvelope Publication(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, Command<DatabaseCommand> operation, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        var verified = database.OwnCommand(operation);
        if (verified.Value!.Kind != OperationKind.OnlineTextPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return new()
        {
            Purpose = OnlineTextRequestScope.Purpose,
            RequestId = requestId,
            Incarnation = database.Store.Identity.Incarnation,
            PrincipalId = verified.UserId,
            CommandKind = verified.Value!.Kind,
            CommandId = verified.CommandId,
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
        {
            Purpose = OnlineTextRequestScope.Purpose
        };
    }

    internal static GrainRequestEnvelope ProjectionRead(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid actor, string principal, ReadProjectionBatchRequest request, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        return GrainRequestEnvelopeConstruction.Read(actor, database.Store.Identity.Incarnation, principal,
            GrainReadKind.ProjectionBatch,
            GrainNativePayload.Copy(NativeSerialization.Serialize(request), database.Limits.MaxBatchBytes), expiry)
            with
        {
            Purpose = OnlineTextRequestScope.Purpose
        };
    }

    internal static GrainRequestEnvelope Checkpoint(DatabaseEngine database, DateTimeOffset now,
        TimeSpan lifetime, Guid actor, string principal, CommitProjectionBatchRequest request, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        return GrainRequestEnvelopeConstruction.Command(actor, database.Store.Identity.Incarnation, principal,
            OperationKind.CommitProjectionBatch, request.CommandId,
            GrainNativePayload.Copy(NativeSerialization.Serialize(request), database.Limits.MaxBatchBytes), expiry)
            with
        {
            Purpose = OnlineTextRequestScope.Purpose
        };
    }

    internal static void RequireExpiry(DateTimeOffset now, TimeSpan lifetime, DateTimeOffset expiry)
    {
        if (expiry <= now || expiry > now + lifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
    }
}
