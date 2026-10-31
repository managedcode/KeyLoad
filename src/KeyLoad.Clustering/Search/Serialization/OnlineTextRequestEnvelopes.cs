using KeyLoad.Clustering.Search;
using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

internal static class OnlineTextRequestEnvelopes
{
    internal static GrainRequestEnvelope Publication(GrainRequestCodec scope, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, Command<DatabaseCommand> operation, DateTimeOffset expiry)
    {
        RequireExpiry(now, lifetime, expiry);
        var verified = CommandExecutionIdentity.SnapshotCommand(operation);
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
            Payload = GrainPayload.Strict(verified),
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
            GrainPayload.Strict(request), expiry)
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
            GrainPayload.Strict(request), expiry)
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
            GrainPayload.Strict(request), expiry)
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
