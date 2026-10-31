using KeyLoad.Clustering.Search;
using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

internal static class OnlineTextRequestEnvelopes
{
    internal static GrainRequestEnvelope Publication(GrainRequestCodec scope, Guid requestId, Command<DatabaseCommand> operation)
    {
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
            Payload = GrainPayload.Strict(verified)
        };
    }

    internal static GrainRequestEnvelope Capability(GrainRequestCodec scope, Guid requestId, string principal,
        OnlineTextCapabilityRequest request)
    {
        return GrainRequestEnvelopeConstruction.Read(requestId, scope.Incarnation,
            principal, GrainReadKind.OnlineTextMaintenance,
            GrainPayload.Strict(request))
            with
        {
            Purpose = OnlineTextRequestScope.Purpose
        };
    }

    internal static GrainRequestEnvelope ProjectionRead(GrainRequestCodec scope, Guid actor, string principal,
        ReadProjectionBatchRequest request)
    {
        return GrainRequestEnvelopeConstruction.Read(actor, scope.Incarnation, principal,
            GrainReadKind.ProjectionBatch,
            GrainPayload.Strict(request))
            with
        {
            Purpose = OnlineTextRequestScope.Purpose
        };
    }

    internal static GrainRequestEnvelope Checkpoint(GrainRequestCodec scope, Guid actor, string principal,
        CommitProjectionBatchRequest request)
    {
        return GrainRequestEnvelopeConstruction.Command(actor, scope.Incarnation, principal,
            OperationKind.CommitProjectionBatch, request.CommandId,
            GrainPayload.Strict(request))
            with
        {
            Purpose = OnlineTextRequestScope.Purpose
        };
    }
}
