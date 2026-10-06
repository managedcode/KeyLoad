using System.Text;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int RemoteTransferCommandsSingleElementCount = 1;
    private const int RemoteTransferCommandsTransferIdentifierSeparatorCount = 2;
    private const int RemoteTransferCommandsNoRetainedBytes = 0;

    private const string TransferInvalidMessage = "The queue transfer request is invalid.";
    private const string TransferConflictMessage = "The transfer identity was reused with different content.";
    private const string TransferMissingMessage = "The source transfer intent is unavailable.";
    private const string TransferReceiptInvalidMessage = "The destination receipt does not match the source intent.";
    private const string TransferCorruptionMessage = "Persisted queue transfer state is inconsistent.";
    private const string TransferCapacityMessage = "The retained queue transfer capacity is exhausted.";

    internal MutationReceipt ApplyCreateQueueTransfer(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, CreateQueueTransfer request, DateTimeOffset now)
    {
        ValidateTransferSource(request, partition);
        RequireTransferAdministrator(principal);
        var sourceResource = Resource(tx, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        var targetResource = Resource(tx, request.Destination.Partition, request.Destination.Queue, ResourceKind.WorkQueue);
        RequireTransferPublisher(principal, request.SourceQueue, sourceResource);
        RequireTransferPublisher(principal, request.Destination, targetResource);

        if (request.Message is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
        var fingerprint = JsonData.Fingerprint(request.Message);
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var existing = tx.GetRecord<RemoteTransferIntentRecord>(key);
        if (existing is not null)
        {
            RequireExistingIntent(tx, existing, request, principal.Id, fingerprint);
            RemoteTransferStorage.RequireSourceCounter(tx, request.SourceQueue);
            return TransferMutationReceipt(RemoteTransferProtocol.CreateReceiptKind, request.SourceQueue, request.TransferId, RemoteTransferCommandsSingleElementCount);
        }

        ValidateTransferMessage(request.Message, request.Destination, now);
        var claims = new RemoteTransferIntentClaims(RemoteTransferProtocol.IntentPurpose, Store.Identity.Incarnation,
            request.SourceQueue, request.TransferId, request.Destination, principal.Id, request.Message, fingerprint);
        var intentToken = Sign(claims);
        var reservationBytes = ReceiptReservationBytes(tx, request.SourceQueue, request.TransferId, request.Destination, principal.Id, fingerprint);
        RequireTokenBound(intentToken);
        var record = new RemoteTransferIntentRecord(request.SourceQueue, request.TransferId, request.Destination, principal.Id,
            request.Message, fingerprint, QueueTransferState.OutputPending, intentToken, null, reservationBytes);
        var recordBytes = RemoteTransferStorage.SourceAccountedBytes(record);
        var capacity = RemoteTransferStorage.SourceCapacity(tx, request.SourceQueue);
        RemoteTransferStorage.RequireCapacity(capacity, recordBytes, Limits);
        tx.PutRecord(key, record);
        tx.PutRecord(RemoteTransferStorage.SourceCapacityKey(request.SourceQueue), RemoteTransferStorage.Add(capacity, recordBytes));
        return TransferMutationReceipt(RemoteTransferProtocol.CreateReceiptKind, request.SourceQueue, request.TransferId, RemoteTransferCommandsSingleElementCount);
    }

    internal MutationReceipt ApplyAcceptQueueTransfer(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, AcceptQueueTransfer request, DateTimeOffset now, long position)
    {
        ValidateTransferDestination(request.DestinationQueue, partition);
        RequireTransferAdministrator(principal);
        if (request.IntentToken is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
        var claims = Verify<RemoteTransferIntentClaims>(request.IntentToken, Limits.MaxBatchBytes);
        ValidateIntentClaims(claims, request.DestinationQueue, principal.Id);
        var resource = Resource(tx, request.DestinationQueue.Partition, request.DestinationQueue.Queue, ResourceKind.WorkQueue);
        RequireTransferPublisher(principal, request.DestinationQueue, resource);

        var key = RemoteTransferStorage.TargetReceiptKey(claims.Source, claims.TransferId, claims.Destination);
        var existing = tx.GetRecord<RemoteTransferTargetReceiptRecord>(key);
        if (existing is not null)
        {
            RequireExistingTargetReceipt(tx, existing, claims);
            RemoteTransferStorage.RequireTargetCounter(tx, claims.Destination);
            return TransferMutationReceipt(RemoteTransferProtocol.AcceptReceiptKind, claims.Destination,
                claims.TransferId, existing.TargetCommit.Position);
        }

        ValidateTransferMessage(claims.Message, claims.Destination, now);
        var targetCommit = Token(tx, partition, position);
        var receiptClaims = new RemoteTransferReceiptClaims(RemoteTransferProtocol.ReceiptPurpose, Store.Identity.Incarnation,
            claims.Source, claims.TransferId, claims.Destination, claims.PrincipalId, claims.Fingerprint, targetCommit);
        var receiptToken = Sign(receiptClaims);
        RequireTokenBound(receiptToken);
        var targetRecord = new RemoteTransferTargetReceiptRecord(claims.Source, claims.TransferId, claims.Destination,
            claims.PrincipalId, claims.Fingerprint, receiptToken, targetCommit);
        var targetBytes = RemoteTransferStorage.SerializedBytes(targetRecord);
        var targetCapacity = RemoteTransferStorage.TargetCapacity(tx, claims.Destination);
        RemoteTransferStorage.RequireCapacity(targetCapacity, targetBytes, Limits);

        _ = Enqueue(tx, principal, partition, claims.Message, now);
        tx.PutRecord(key, targetRecord);
        tx.PutRecord(RemoteTransferStorage.TargetCapacityKey(claims.Destination), RemoteTransferStorage.Add(targetCapacity, targetBytes));
        return TransferMutationReceipt(RemoteTransferProtocol.AcceptReceiptKind, claims.Destination, claims.TransferId, targetCommit.Position);
    }

    internal MutationReceipt ApplyCompleteQueueTransfer(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, CompleteQueueTransfer request)
    {
        ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        RequireTransferAdministrator(principal);
        var resource = Resource(tx, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        RequireTransferPublisher(principal, request.SourceQueue, resource);
        if (request.ReceiptToken is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var existing = tx.GetRecord<RemoteTransferIntentRecord>(key)
            ?? throw Errors.Fail(ErrorCode.NotFound, TransferMissingMessage);
        ValidateIntentRecord(tx, existing, request.SourceQueue, request.TransferId);
        if (principal.Id != existing.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
        }

        var receipt = Verify<RemoteTransferReceiptClaims>(request.ReceiptToken, Limits.MaxBatchBytes);
        if (receipt is null || receipt.Source is null || receipt.Destination is null || receipt.TargetCommit is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
        }
        ValidateReceipt(tx, receipt, existing);
        if (existing.State == QueueTransferState.Delivered)
        {
            if (existing.ReceiptToken != request.ReceiptToken)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
            }
            return TransferMutationReceipt(RemoteTransferProtocol.CompleteReceiptKind, request.SourceQueue, request.TransferId, RemoteTransferCommandsTransferIdentifierSeparatorCount);
        }

        if (existing.State != QueueTransferState.OutputPending)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage);
        }

        var updated = existing with { State = QueueTransferState.Delivered, ReceiptToken = request.ReceiptToken };
        var oldBytes = RemoteTransferStorage.SourceAccountedBytes(existing);
        var newBytes = RemoteTransferStorage.SourceAccountedBytes(updated);
        var capacity = RemoteTransferStorage.RequireSourceCounter(tx, request.SourceQueue);
        var nextCapacity = RemoteTransferStorage.Replace(capacity, oldBytes, newBytes, Limits);
        tx.PutRecord(key, updated);
        tx.PutRecord(RemoteTransferStorage.SourceCapacityKey(request.SourceQueue), nextCapacity);
        return TransferMutationReceipt(RemoteTransferProtocol.CompleteReceiptKind, request.SourceQueue, request.TransferId, RemoteTransferCommandsTransferIdentifierSeparatorCount);
    }

    private int ReceiptReservationBytes(IKeyValueView view, QueueLaneRef source, Guid transferId, QueueLaneRef destination,
        string principalId, string fingerprint)
    {
        var maximumCommit = Token(view, destination.Partition, long.MaxValue);
        var claims = new RemoteTransferReceiptClaims(RemoteTransferProtocol.ReceiptPurpose, Store.Identity.Incarnation,
            source, transferId, destination, principalId, fingerprint, maximumCommit);
        var bytes = Encoding.UTF8.GetByteCount(Sign(claims));
        if (bytes > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TransferCapacityMessage);
        }
        return bytes;
    }

    private void RequireExistingIntent(IKeyValueView view, RemoteTransferIntentRecord existing, CreateQueueTransfer request,
        string principalId, string fingerprint)
    {
        ValidateIntentRecord(view, existing, request.SourceQueue, request.TransferId);
        if (existing.Destination != request.Destination || existing.PrincipalId != principalId || existing.Fingerprint != fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, TransferConflictMessage);
        }
    }

    private void RequireExistingTargetReceipt(IKeyValueView view, RemoteTransferTargetReceiptRecord existing, RemoteTransferIntentClaims claims)
    {
        if (existing.Source != claims.Source || existing.TransferId != claims.TransferId || existing.Destination != claims.Destination
            || existing.PrincipalId is null || existing.Fingerprint is null || existing.ReceiptToken is null
            || existing.PrincipalId != claims.PrincipalId || existing.Fingerprint != claims.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, TransferConflictMessage);
        }
        var receipt = Verify<RemoteTransferReceiptClaims>(existing.ReceiptToken, Limits.MaxBatchBytes);
        if (receipt is null || receipt.Source is null || receipt.Destination is null || receipt.TargetCommit is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage);
        }
        ValidateReceiptClaims(view, receipt, claims, existing.TargetCommit);
    }

    private void ValidateReceipt(IKeyValueView view, RemoteTransferReceiptClaims receipt, RemoteTransferIntentRecord intent)
    {
        if (receipt.Purpose != RemoteTransferProtocol.ReceiptPurpose || receipt.Incarnation != Store.Identity.Incarnation
            || receipt.Source != intent.Source || receipt.TransferId != intent.TransferId || receipt.Destination != intent.Destination
            || receipt.PrincipalId != intent.PrincipalId || receipt.Fingerprint != intent.Fingerprint
            || receipt.TargetCommit.Incarnation != Store.Identity.Incarnation
            || receipt.TargetCommit.AtomicPartitionId != intent.Destination.Partition.AtomicPartitionId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
        }
        ValidateCommitToken(view, intent.Destination.Partition, receipt.TargetCommit,
            ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
    }

    private void ValidateReceiptClaims(IKeyValueView view, RemoteTransferReceiptClaims receipt, RemoteTransferIntentClaims intent, CommitToken targetCommit)
    {
        if (receipt.Purpose != RemoteTransferProtocol.ReceiptPurpose || receipt.Incarnation != Store.Identity.Incarnation
            || receipt.Source != intent.Source || receipt.TransferId != intent.TransferId || receipt.Destination != intent.Destination
            || receipt.PrincipalId != intent.PrincipalId || receipt.Fingerprint != intent.Fingerprint || receipt.TargetCommit != targetCommit
            || targetCommit.Incarnation != Store.Identity.Incarnation
            || targetCommit.AtomicPartitionId != intent.Destination.Partition.AtomicPartitionId)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage);
        }
        ValidateCommitToken(view, intent.Destination.Partition, targetCommit,
            ErrorCode.Corruption, TransferCorruptionMessage);
    }

    private void ValidateIntentClaims(RemoteTransferIntentClaims claims, QueueLaneRef destination, string principalId)
    {
        if (claims is null || claims.Source is null || claims.Destination is null || claims.Message is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
        }
        ValidateTransferPair(claims.Source, claims.Destination);
        if (claims.Purpose != RemoteTransferProtocol.IntentPurpose || claims.Incarnation != Store.Identity.Incarnation
            || claims.Destination != destination || claims.PrincipalId != principalId || claims.TransferId == Guid.Empty
            || claims.Message.Queue != claims.Destination.Queue
            || JsonData.Fingerprint(claims.Message) != claims.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
        }
    }

    private void ValidateTransferMessage(EnqueueMessage message, QueueLaneRef destination, DateTimeOffset now)
    {
        if (message is null || message.Queue != destination.Queue)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
        JsonData.Identifier(message.MessageId);
        _ = JsonData.Validate(message.PayloadJson, Limits);
        _ = JsonData.Validate(message.HeadersJson, Limits);
        if (message.ExpiresAt <= now || message.ExpiresAt <= message.NotBefore)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
    }

    private void RequireTokenBound(string token)
    {
        if (Encoding.UTF8.GetByteCount(token) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TransferCapacityMessage);
        }
    }

    private static MutationReceipt TransferMutationReceipt(string kind, QueueLaneRef lane, Guid transferId, long revision)
        => new(kind, lane.Queue, transferId.ToString(RemoteTransferProtocol.TransferIdFormat), revision);

    private static void ValidateTransferSource(CreateQueueTransfer request, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        ValidateTransferPair(request.SourceQueue, request.Destination);
    }

    private static void ValidateTransferDestination(QueueLaneRef destination, PartitionRef partition)
    {
        ValidateTransferLane(destination);
        ValidatePartition(partition);
        if (destination.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
    }

    private static void ValidateTransferPair(QueueLaneRef source, QueueLaneRef destination)
    {
        ValidateTransferLane(source);
        ValidateTransferLane(destination);
        if (source.Partition.TenantId != destination.Partition.TenantId
            || source.Partition.DatabaseId != destination.Partition.DatabaseId
            || source.Partition.AtomicPartitionId == destination.Partition.AtomicPartitionId)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
    }

    private void ValidateIntentRecord(IKeyValueView view, RemoteTransferIntentRecord record, QueueLaneRef source, Guid transferId)
    {
        if (HasInvalidIntentRecordShape(record, source, transferId))
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage);
        }
        var claims = Verify<RemoteTransferIntentClaims>(record.IntentToken!, Limits.MaxBatchBytes);
        if (claims is null || claims.Source is null || claims.Destination is null || claims.Message is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage);
        }
        ValidateStoredIntentClaims(claims, record);
        ValidateIntentRecordState(record);
        ValidateReceiptToken(view, record);
    }

    private static bool HasInvalidIntentRecordShape(RemoteTransferIntentRecord record,
        QueueLaneRef source, Guid transferId)
        => record.Source != source || record.TransferId != transferId || record.TransferId == Guid.Empty
            || record.Destination is null || record.PrincipalId is null || record.Message is null || record.IntentToken is null
            || record.ReceiptReservationBytes < RemoteTransferCommandsNoRetainedBytes || record.Fingerprint is null;

    private static void ValidateIntentRecordState(RemoteTransferIntentRecord record)
    {
        if (record.State is not (QueueTransferState.OutputPending or QueueTransferState.Delivered)
            || record.State == QueueTransferState.OutputPending && record.ReceiptToken is not null
            || record.State == QueueTransferState.Delivered && record.ReceiptToken is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage);
        }
    }

    private void ValidateReceiptToken(IKeyValueView view, RemoteTransferIntentRecord record)
    {
        if (record.ReceiptToken is { } receiptToken)
        {
            var receipt = Verify<RemoteTransferReceiptClaims>(receiptToken, Limits.MaxBatchBytes);
            if (receipt is null || receipt.Source is null || receipt.Destination is null || receipt.TargetCommit is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage);
            }
            ValidateReceipt(view, receipt, record);
        }
    }

    private void ValidateStoredIntentClaims(RemoteTransferIntentClaims claims, RemoteTransferIntentRecord record)
    {
        if (claims is null || claims.Source is null || claims.Destination is null || claims.Message is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage);
        }
        ValidateTransferPair(record.Source, record.Destination);
        if (claims.Purpose != RemoteTransferProtocol.IntentPurpose || claims.Incarnation != Store.Identity.Incarnation
            || claims.Source != record.Source || claims.TransferId != record.TransferId || claims.Destination != record.Destination
            || claims.PrincipalId != record.PrincipalId || claims.Message != record.Message || claims.Fingerprint != record.Fingerprint
            || JsonData.Fingerprint(record.Message) != record.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage);
        }
    }
}
