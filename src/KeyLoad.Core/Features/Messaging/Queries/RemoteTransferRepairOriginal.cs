using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private ReplicatedOperation RequireRemoteTransferRepairOriginal(IKeyValueView view, PrincipalRecord principal,
        RemoteTransferCoordinationReadRequest request, RemoteTransferIntentClaims intent)
    {
        var stage = request.RepairStage ?? throw Errors.Fail(ErrorCode.Validation, RemoteTransferRepairProtocol.Invalid);
        var hint = new RemoteTransferCoordinationHint(intent.Source, intent.Destination, intent.TransferId,
            intent.PrincipalId, intent.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(request.IntentToken),
            Token(view, intent.Source.Partition, Store.Position), request.ExpectedGeneration, null,
            request.PolicyGeneration, request.CompleteGeneration);
        if (request.AcceptCommandId != RemoteTransferRepairIdentity.CommandId(hint, stage))
        { throw Errors.Fail(ErrorCode.Conflict, RemoteTransferRepairProtocol.Invalid); }
        var partition = stage == QueueTransferRepairStage.Accept ? intent.Destination.Partition : intent.Source.Partition;
        Mutation mutation = stage == QueueTransferRepairStage.Accept
            ? new AcceptQueueTransfer(intent.Destination, request.IntentToken)
            : new CompleteQueueTransfer(intent.Source, intent.TransferId, request.ReceiptToken!);
        var command = new CommandRequest(request.AcceptCommandId, partition, [mutation]);
        var original = CreateNativeOperation(OperationKind.Batch, command.CommandId, principal.Id,
            Clock.GetUtcNow(), NativeSerialization.Serialize(command));
        _ = AuthorizeOperation(view, principal, original);
        if (stage == QueueTransferRepairStage.Complete)
        {
            var record = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(intent.Source, intent.TransferId))
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferRepairProtocol.Unavailable);
            ValidateIntentRecord(view, record, intent.Source, intent.TransferId);
            if (record.PrincipalId != principal.Id || record.State != QueueTransferState.OutputPending)
            { throw Errors.Fail(ErrorCode.RevisionConflict, RemoteTransferRepairProtocol.Stale); }
            ValidateReceipt(view, Verify<RemoteTransferReceiptClaims>(request.ReceiptToken!, Limits.MaxBatchBytes), record);
        }
        return original;
    }
}
