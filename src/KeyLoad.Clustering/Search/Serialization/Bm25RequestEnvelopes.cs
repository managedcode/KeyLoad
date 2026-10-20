using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

internal static class Bm25RequestEnvelopes
{
    internal static GrainRequestEnvelope Publication(GrainRequestCodec scope, DateTimeOffset now,
        TimeSpan lifetime, Guid requestId, Command<DatabaseCommand> operation, DateTimeOffset expiry)
    {
        if (expiry <= now || expiry > now + lifetime)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var verified = CommandExecutionIdentity.SnapshotCommand(operation, scope.Limits);
        if (verified.Value!.Kind != OperationKind.Bm25CorpusPublicationPhase)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return new()
        {
            Purpose = Bm25RequestScope.Purpose,
            RequestId = requestId,
            Incarnation = scope.Incarnation,
            PrincipalId = verified.UserId,
            CommandKind = verified.Value!.Kind,
            CommandId = verified.CommandId,
            Payload = GrainPayload.Copy(OrleansSerialization.Serialize(verified), scope.Limits.MaxBatchBytes),
            ExpiresAt = expiry
        };
    }
}
