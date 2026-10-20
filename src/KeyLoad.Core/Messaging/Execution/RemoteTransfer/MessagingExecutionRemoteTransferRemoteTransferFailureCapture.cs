using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingExecutionRemoteTransferRemoteTransferFailureCapture operations against the caller's database context.</summary>
public static class MessagingExecutionRemoteTransferRemoteTransferFailureCapture
{
    internal static RemoteTransferAcceptFailureAuthority? CaptureRemoteTransferFailureAuthority(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, Command<DatabaseCommand> operation)
    {
        if (engine.Limits.MaxQueueTransferAcceptAttempts is null || operation.Value!.Kind != OperationKind.Batch)
        {
            return null;
        }

        var batch = OperationProtocol.Payload<CommandRequest>(operation);
        if (batch.Mutations.Length != RemoteTransferAttemptProtocol.SingleMutation || batch.Mutations[RemoteTransferAttemptProtocol.FirstIndex] is not AcceptQueueTransfer accept)
        {
            return null;
        }

        var claims = engine.AuthorizeAcceptQueueTransfer(view, principal, batch.Partition, accept);
        var existing = view.GetRecord<RemoteTransferTargetReceiptRecord>(RemoteTransferStorage.TargetReceiptKey(claims.Source, claims.TransferId, claims.Destination));
        if (existing is not null)
        {
            return null;
        }

        return new(claims.Source, claims.Destination, claims.TransferId, principal.Id, claims.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(accept.IntentToken), operation.CommandId, OperationProtocol.CommandFingerprint(operation), engine.Store.Identity.Incarnation, engine.CaptureRemoteTransferDependency(view, principal, claims.Destination));
    }

    internal static bool HasRemoteTransferCapacityFailure(Result<object> result) => Errors.Code(result.Problem) == ErrorCode.ResourceExhausted && (result.Problem?.Detail == MessagingCommandsQueueSweepTransitions.StoredQuotaExhausted || RemoteTransferStorage.IsCapacityFailure(result));
    internal static RemoteTransferAcceptFailureKind RemoteTransferFailureKind(Result<object> result) => result.Problem?.Detail == MessagingCommandsQueueSweepTransitions.StoredQuotaExhausted ? RemoteTransferAcceptFailureKind.QueueStorage : RemoteTransferAcceptFailureKind.TransferRetention;
}
