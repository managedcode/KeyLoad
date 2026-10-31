using KeyLoad.Core;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Clustering;

internal static class Bm25RequestEnvelopes
{
    internal static GrainRequestEnvelope Publication(GrainRequestCodec scope, Guid requestId, Command<DatabaseCommand> operation)
    {
        var verified = CommandExecutionIdentity.SnapshotCommand(operation);
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
            Payload = GrainPayload.Strict(verified)
        };
    }
}
