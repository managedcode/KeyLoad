using KeyLoad.Clustering.Features.Search;
using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

internal static class OnlineTextRequestEnvelopes
{
    internal static GrainRequestEnvelope Publication(GrainRequestCodec scope, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, Command<DatabaseCommand> operation, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        var verified = DatabaseEngine.SnapshotCommand(operation, scope.Limits);
        if (verified.Value!.Kind != OperationKind.OnlineTextPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return new()
        {
            Purpose = OnlineTextRequestScope.Purpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = verified.UserId,
            CommandKind = verified.Value!.Kind,
            CommandId = verified.CommandId,
            Payload = GrainNativePayload.Copy(NativeSerialization.Serialize(verified), scope.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
    }

    internal static GrainRequestEnvelope Capability(GrainRequestCodec scope, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, string principal, OnlineTextCapabilityRequest request,
        DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        return GrainRequestEnvelopeConstruction.Read(requestId, scope.Incarnation,
            principal, GrainReadKind.OnlineTextMaintenance,
            GrainNativePayload.Copy(NativeSerialization.Serialize(request), scope.Limits.MaxBatchBytes), expiry)
            with
        {
            Purpose = OnlineTextRequestScope.Purpose
        };
    }

    internal static GrainRequestEnvelope ProjectionRead(GrainRequestCodec scope, DateTimeOffset now,
        TimeSpan lifetime, Guid actor, string principal, ReadProjectionBatchRequest request, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        return GrainRequestEnvelopeConstruction.Read(actor, scope.Incarnation, principal,
            GrainReadKind.ProjectionBatch,
            GrainNativePayload.Copy(NativeSerialization.Serialize(request), scope.Limits.MaxBatchBytes), expiry)
            with
        {
            Purpose = OnlineTextRequestScope.Purpose
        };
    }

    internal static GrainRequestEnvelope Checkpoint(GrainRequestCodec scope, DateTimeOffset now,
        TimeSpan lifetime, Guid actor, string principal, CommitProjectionBatchRequest request, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        return GrainRequestEnvelopeConstruction.Command(actor, scope.Incarnation, principal,
            OperationKind.CommitProjectionBatch, request.CommandId,
            GrainNativePayload.Copy(NativeSerialization.Serialize(request), scope.Limits.MaxBatchBytes), expiry)
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
