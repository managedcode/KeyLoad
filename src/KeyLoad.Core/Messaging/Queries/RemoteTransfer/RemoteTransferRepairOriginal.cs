using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferRepairOriginal operations against the caller's database context.</summary>
public static class RemoteTransferRepairOriginal
{
    internal static Command<DatabaseCommand> RequireRemoteTransferRepairOriginal(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, RemoteTransferCoordinationReadRequest request, RemoteTransferIntentClaims intent)
    {
        var stage = request.RepairStage ?? throw Errors.Fail(ErrorCode.Validation, RemoteTransferRepairProtocol.Invalid);
        var hint = new RemoteTransferCoordinationHint(intent.Source, intent.Destination, intent.TransferId, intent.PrincipalId, intent.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(request.IntentToken), engine.Token(view, intent.Source.Partition, engine.Store.Position), request.ExpectedGeneration, null, request.PolicyGeneration, request.CompleteGeneration);
        if (request.AcceptCommandId != RemoteTransferRepairIdentity.CommandId(hint, stage))
        {
            throw Errors.Fail(ErrorCode.Conflict, RemoteTransferRepairProtocol.Invalid);
        }

        var partition = stage == QueueTransferRepairStage.Accept ? intent.Destination.Partition : intent.Source.Partition;
        Mutation mutation = stage == QueueTransferRepairStage.Accept ? new AcceptQueueTransfer(intent.Destination, request.IntentToken) : new CompleteQueueTransfer(intent.Source, intent.TransferId, request.ReceiptToken!);
        var command = new CommandRequest(request.AcceptCommandId, partition, [mutation]);
        var original = engine.CreateCommand(view, OperationKind.Batch, command.CommandId, principal.Id, engine.Clock.GetUtcNow(), OrleansSerialization.Serialize(command));
        _ = engine.AuthorizeOperation(view, principal, original);
        if (stage == QueueTransferRepairStage.Complete)
        {
            var record = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(intent.Source, intent.TransferId)) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Unavailable);
            engine.ValidateIntentRecord(view, record, intent.Source, intent.TransferId);
            if (record.PrincipalId != principal.Id || record.State != QueueTransferState.OutputPending)
            {
                throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale);
            }

            engine.ValidateReceipt(view, engine.Verify<RemoteTransferReceiptClaims>(request.ReceiptToken!, engine.Limits.MaxBatchBytes), record);
        }

        return original;
    }
}
