using System.Text;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferCommands operations against the caller's database context.</summary>
public static class RemoteTransferCommands
{
    internal const int RemoteTransferCommandsSingleElementCount = 1;
    internal const int RemoteTransferCommandsTransferIdentifierSeparatorCount = 2;
    internal const int RemoteTransferCommandsNoRetainedBytes = 0;
    internal const string TransferInvalidMessage = "The queue transfer request is invalid.";
    internal const string TransferConflictMessage = "The transfer identity was reused with different content.";
    internal const string TransferMissingMessage = "The source transfer intent is unavailable.";
    internal const string TransferReceiptInvalidMessage = "The destination receipt does not match the source intent.";
    internal const string TransferCorruptionMessage = "Persisted queue transfer state is inconsistent.";
    internal const string TransferCapacityMessage = "The retained queue transfer capacity is exhausted.";
    internal static MutationReceipt ApplyCreateQueueTransfer(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, CreateQueueTransfer request, DateTimeOffset now)
    {
        RemoteTransferValidation.ValidateTransferSource(request, partition);
        RemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, request.SourceQueue, Capability.QueuePublish);
        engine.RequireTransferCapabilities(principal, request.Destination, Capability.QueuePublish);
        var sourceResource = engine.Resource(tx, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        var targetResource = engine.RemoteTransferDestinationResource(tx, request.Destination);
        engine.RequireTransferPublisher(principal, request.SourceQueue, sourceResource);
        engine.RequireTransferPublisher(principal, request.Destination, targetResource);
        if (request.Message is null)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferCommands.TransferInvalidMessage);
        }

        var fingerprint = JsonData.Fingerprint(request.Message);
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var existing = tx.GetRecord<RemoteTransferIntentRecord>(key);
        if (existing is not null)
        {
            engine.RequireExistingIntent(tx, existing, request, principal.Id, fingerprint);
            RemoteTransferStorage.RequireSourceCounter(tx, request.SourceQueue);
            return RemoteTransferCommands.TransferMutationReceipt(RemoteTransferProtocol.CreateReceiptKind, request.SourceQueue, request.TransferId, RemoteTransferCommands.RemoteTransferCommandsSingleElementCount);
        }

        engine.ValidateTransferMessage(request.Message, request.Destination, now);
        var claims = new RemoteTransferIntentClaims(RemoteTransferProtocol.IntentPurpose, engine.Store.Identity.Incarnation, request.SourceQueue, request.TransferId, request.Destination, principal.Id, request.Message, fingerprint);
        var intentToken = engine.Sign(claims);
        var reservationBytes = engine.ReceiptReservationBytes(tx, request.SourceQueue, request.TransferId, request.Destination, principal.Id, fingerprint);
        engine.RequireTokenBound(intentToken);
        var record = new RemoteTransferIntentRecord(request.SourceQueue, request.TransferId, request.Destination, principal.Id, request.Message, fingerprint, QueueTransferState.OutputPending, intentToken, null, reservationBytes)
        {
            Attempts = RemoteTransferAttemptStateValidation.Initial(engine.Limits.MaxQueueTransferAcceptAttempts),
            Repairs = RemoteTransferRepairStateValidation.Initial(engine.Limits.MaxQueueTransferRepairAttempts)
        };
        var recordBytes = RemoteTransferStorage.SourceAccountedBytes(record);
        var capacity = RemoteTransferStorage.SourceCapacity(tx, request.SourceQueue);
        RemoteTransferStorage.RequireCapacity(capacity, recordBytes, engine.Limits);
        tx.PutRecord(key, record);
        tx.PutRecord(RemoteTransferStorage.SourceCapacityKey(request.SourceQueue), RemoteTransferStorage.Add(capacity, recordBytes));
        return RemoteTransferCommands.TransferMutationReceipt(RemoteTransferProtocol.CreateReceiptKind, request.SourceQueue, request.TransferId, RemoteTransferCommands.RemoteTransferCommandsSingleElementCount);
    }

    internal static MutationReceipt ApplyAcceptQueueTransfer(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AcceptQueueTransfer request, DateTimeOffset now, long position)
    {
        RemoteTransferValidation.ValidateTransferDestination(request.DestinationQueue, partition);
        RemoteTransferAuthorization.RequireTransferAdministrator(principal);
        if (request.IntentToken is null)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferCommands.TransferInvalidMessage);
        }

        var claims = engine.Verify<RemoteTransferIntentClaims>(request.IntentToken, engine.Limits.MaxBatchBytes);
        engine.ValidateIntentClaims(claims, request.DestinationQueue, principal.Id);
        engine.RequireTransferCapabilities(principal, request.DestinationQueue, Capability.QueuePublish);
        var resource = engine.Resource(tx, request.DestinationQueue.Partition, request.DestinationQueue.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferPublisher(principal, request.DestinationQueue, resource);
        var key = RemoteTransferStorage.TargetReceiptKey(claims.Source, claims.TransferId, claims.Destination);
        var existing = tx.GetRecord<RemoteTransferTargetReceiptRecord>(key);
        if (existing is not null)
        {
            engine.RequireExistingTargetReceipt(tx, existing, claims);
            RemoteTransferStorage.RequireTargetCounter(tx, claims.Destination);
            return RemoteTransferCommands.TransferMutationReceipt(RemoteTransferProtocol.AcceptReceiptKind, claims.Destination, claims.TransferId, existing.TargetCommit.Position);
        }

        engine.ValidateTransferMessage(claims.Message, claims.Destination, now);
        var targetCommit = engine.Token(tx, partition, position);
        var receiptClaims = new RemoteTransferReceiptClaims(RemoteTransferProtocol.ReceiptPurpose, engine.Store.Identity.Incarnation, claims.Source, claims.TransferId, claims.Destination, claims.PrincipalId, claims.Fingerprint, targetCommit);
        var receiptToken = engine.Sign(receiptClaims);
        engine.RequireTokenBound(receiptToken);
        var targetRecord = new RemoteTransferTargetReceiptRecord(claims.Source, claims.TransferId, claims.Destination, claims.PrincipalId, claims.Fingerprint, receiptToken, targetCommit);
        var targetBytes = RemoteTransferStorage.SerializedBytes(targetRecord);
        var targetCapacity = RemoteTransferStorage.TargetCapacity(tx, claims.Destination);
        RemoteTransferStorage.RequireCapacity(targetCapacity, targetBytes, engine.Limits);
        _ = engine.Enqueue(tx, principal, partition, claims.Message, now);
        tx.PutRecord(key, targetRecord);
        tx.PutRecord(RemoteTransferStorage.TargetCapacityKey(claims.Destination), RemoteTransferStorage.Add(targetCapacity, targetBytes));
        return RemoteTransferCommands.TransferMutationReceipt(RemoteTransferProtocol.AcceptReceiptKind, claims.Destination, claims.TransferId, targetCommit.Position);
    }

    internal static MutationReceipt ApplyCompleteQueueTransfer(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, CompleteQueueTransfer request)
    {
        RemoteTransferInspection.ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        RemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, request.SourceQueue, Capability.QueuePublish);
        var resource = engine.Resource(tx, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferPublisher(principal, request.SourceQueue, resource);
        if (request.ReceiptToken is null)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferCommands.TransferInvalidMessage);
        }

        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var existing = tx.GetRecord<RemoteTransferIntentRecord>(key) ?? throw Errors.Fail(ErrorCode.NotFound, RemoteTransferCommands.TransferMissingMessage);
        engine.ValidateIntentRecord(tx, existing, request.SourceQueue, request.TransferId);
        if (principal.Id != existing.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferCommands.TransferReceiptInvalidMessage);
        }

        var receipt = engine.Verify<RemoteTransferReceiptClaims>(request.ReceiptToken, engine.Limits.MaxBatchBytes);
        if (receipt is null || receipt.Source is null || receipt.Destination is null || receipt.TargetCommit is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferCommands.TransferReceiptInvalidMessage);
        }

        engine.ValidateReceipt(tx, receipt, existing);
        if (existing.State == QueueTransferState.Delivered)
        {
            if (existing.ReceiptToken != request.ReceiptToken)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferCommands.TransferReceiptInvalidMessage);
            }

            return RemoteTransferCommands.TransferMutationReceipt(RemoteTransferProtocol.CompleteReceiptKind, request.SourceQueue, request.TransferId, RemoteTransferCommands.RemoteTransferCommandsTransferIdentifierSeparatorCount);
        }

        if (existing.State != QueueTransferState.OutputPending)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteTransferCommands.TransferCorruptionMessage);
        }

        var updated = existing with
        {
            State = QueueTransferState.Delivered,
            ReceiptToken = request.ReceiptToken
        };
        var oldBytes = RemoteTransferStorage.SourceAccountedBytes(existing);
        var newBytes = RemoteTransferStorage.SourceAccountedBytes(updated);
        var capacity = RemoteTransferStorage.RequireSourceCounter(tx, request.SourceQueue);
        var nextCapacity = RemoteTransferStorage.Replace(capacity, oldBytes, newBytes, engine.Limits);
        tx.PutRecord(key, updated);
        tx.PutRecord(RemoteTransferStorage.SourceCapacityKey(request.SourceQueue), nextCapacity);
        return RemoteTransferCommands.TransferMutationReceipt(RemoteTransferProtocol.CompleteReceiptKind, request.SourceQueue, request.TransferId, RemoteTransferCommands.RemoteTransferCommandsTransferIdentifierSeparatorCount);
    }

    internal static int ReceiptReservationBytes(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef source, Guid transferId, QueueLaneRef destination, string principalId, string fingerprint)
    {
        var maximumCommit = engine.Token(view, destination.Partition, long.MaxValue);
        var claims = new RemoteTransferReceiptClaims(RemoteTransferProtocol.ReceiptPurpose, engine.Store.Identity.Incarnation, source, transferId, destination, principalId, fingerprint, maximumCommit);
        var bytes = Encoding.UTF8.GetByteCount(engine.Sign(claims));
        if (bytes > engine.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteTransferCommands.TransferCapacityMessage);
        }

        return bytes;
    }

    internal static void RequireExistingIntent(this DatabaseEngine engine, IKeyValueView view, RemoteTransferIntentRecord existing, CreateQueueTransfer request, string principalId, string fingerprint)
    {
        engine.ValidateIntentRecord(view, existing, request.SourceQueue, request.TransferId);
        if (existing.Destination != request.Destination || existing.PrincipalId != principalId || existing.Fingerprint != fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, RemoteTransferCommands.TransferConflictMessage);
        }
    }

    internal static void RequireExistingTargetReceipt(this DatabaseEngine engine, IKeyValueView view, RemoteTransferTargetReceiptRecord existing, RemoteTransferIntentClaims claims)
    {
        if (existing.Source != claims.Source || existing.TransferId != claims.TransferId || existing.Destination != claims.Destination || existing.PrincipalId is null || existing.Fingerprint is null || existing.ReceiptToken is null || existing.PrincipalId != claims.PrincipalId || existing.Fingerprint != claims.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, RemoteTransferCommands.TransferConflictMessage);
        }

        var receipt = engine.Verify<RemoteTransferReceiptClaims>(existing.ReceiptToken, engine.Limits.MaxBatchBytes);
        if (receipt is null || receipt.Source is null || receipt.Destination is null || receipt.TargetCommit is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteTransferCommands.TransferCorruptionMessage);
        }

        engine.ValidateReceiptClaims(view, receipt, claims, existing.TargetCommit);
    }

    internal static MutationReceipt TransferMutationReceipt(string kind, QueueLaneRef lane, Guid transferId, long revision) => new(kind, lane.Queue, transferId.ToString(RemoteTransferProtocol.TransferIdFormat), revision);
}
