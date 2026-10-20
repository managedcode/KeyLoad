using System.Text;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Owns the RemoteTransferValidation operation contract.</summary>
internal static class RemoteTransferValidation
{
    internal static void ValidateReceipt(this DatabaseEngine engine, IKeyValueView view, RemoteTransferReceiptClaims receipt, RemoteTransferIntentRecord intent)
    {
        if (receipt.Purpose != RemoteTransferProtocol.ReceiptPurpose || receipt.Incarnation != engine.Store.Identity.Incarnation || receipt.Source != intent.Source || receipt.TransferId != intent.TransferId || receipt.Destination != intent.Destination || receipt.PrincipalId != intent.PrincipalId || receipt.Fingerprint != intent.Fingerprint || receipt.TargetCommit.Incarnation != engine.Store.Identity.Incarnation || receipt.TargetCommit.AtomicPartitionId != intent.Destination.Partition.AtomicPartitionId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferReceiptInvalidMessage);
        }

        engine.ValidateCommitToken(view, intent.Destination.Partition, receipt.TargetCommit, ErrorCode.TokenInvalidated, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferReceiptInvalidMessage);
    }

    internal static void ValidateReceiptClaims(this DatabaseEngine engine, IKeyValueView view, RemoteTransferReceiptClaims receipt, RemoteTransferIntentClaims intent, CommitToken targetCommit)
    {
        if (receipt.Purpose != RemoteTransferProtocol.ReceiptPurpose || receipt.Incarnation != engine.Store.Identity.Incarnation || receipt.Source != intent.Source || receipt.TransferId != intent.TransferId || receipt.Destination != intent.Destination || receipt.PrincipalId != intent.PrincipalId || receipt.Fingerprint != intent.Fingerprint || receipt.TargetCommit != targetCommit || targetCommit.Incarnation != engine.Store.Identity.Incarnation || targetCommit.AtomicPartitionId != intent.Destination.Partition.AtomicPartitionId)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferCorruptionMessage);
        }

        engine.ValidateCommitToken(view, intent.Destination.Partition, targetCommit, ErrorCode.Corruption, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferCorruptionMessage);
    }

    internal static void ValidateIntentClaims(this DatabaseEngine engine, RemoteTransferIntentClaims claims, QueueLaneRef destination, string principalId)
    {
        if (claims is null || claims.Source is null || claims.Destination is null || claims.Message is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferReceiptInvalidMessage);
        }

        RemoteTransferValidation.ValidateTransferPair(claims.Source, claims.Destination);
        if (claims.Purpose != RemoteTransferProtocol.IntentPurpose || claims.Incarnation != engine.Store.Identity.Incarnation || claims.Destination != destination || claims.PrincipalId != principalId || claims.TransferId == Guid.Empty || claims.Message.Queue != claims.Destination.Queue || JsonData.Fingerprint(claims.Message) != claims.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferReceiptInvalidMessage);
        }
    }

    internal static void ValidateTransferMessage(this DatabaseEngine engine, EnqueueMessage message, QueueLaneRef destination, DateTimeOffset now)
    {
        if (message is null || message.Queue != destination.Queue)
        {
            throw Errors.Fail(ErrorCode.Validation, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferInvalidMessage);
        }

        JsonData.Identifier(message.MessageId);
        _ = JsonData.Validate(message.PayloadJson, engine.Limits);
        _ = JsonData.Validate(message.HeadersJson, engine.Limits);
        if (message.ExpiresAt <= now || message.ExpiresAt <= message.NotBefore)
        {
            throw Errors.Fail(ErrorCode.Validation, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferInvalidMessage);
        }
    }

    internal static void RequireTokenBound(this DatabaseEngine engine, string token)
    {
        if (Encoding.UTF8.GetByteCount(token) > engine.Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferCapacityMessage);
        }
    }

    internal static void ValidateTransferSource(CreateQueueTransfer request, PartitionRef partition)
    {
        ArgumentNullException.ThrowIfNull(request);
        MessagingExecutionRemoteTransferRemoteTransferInspection.ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        RemoteTransferValidation.ValidateTransferPair(request.SourceQueue, request.Destination);
    }

    internal static void ValidateTransferDestination(QueueLaneRef destination, PartitionRef partition)
    {
        MessagingExecutionRemoteTransferRemoteTransferInspection.ValidateTransferLane(destination);
        EngineResourceAccess.ValidatePartition(partition);
        if (destination.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.Validation, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferInvalidMessage);
        }
    }

    internal static void ValidateTransferPair(QueueLaneRef source, QueueLaneRef destination)
    {
        MessagingExecutionRemoteTransferRemoteTransferInspection.ValidateTransferLane(source);
        MessagingExecutionRemoteTransferRemoteTransferInspection.ValidateTransferLane(destination);
        if (source.Partition.TenantId != destination.Partition.TenantId || source.Partition.DatabaseId != destination.Partition.DatabaseId || source.Partition.AtomicPartitionId == destination.Partition.AtomicPartitionId)
        {
            throw Errors.Fail(ErrorCode.Validation, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferInvalidMessage);
        }
    }

    internal static void ValidateIntentRecord(this DatabaseEngine engine, IKeyValueView view, RemoteTransferIntentRecord record, QueueLaneRef source, Guid transferId)
    {
        if (RemoteTransferValidation.HasInvalidIntentRecordShape(record, source, transferId))
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferCorruptionMessage);
        }

        var claims = engine.Verify<RemoteTransferIntentClaims>(record.IntentToken!, engine.Limits.MaxBatchBytes);
        if (claims is null || claims.Source is null || claims.Destination is null || claims.Message is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferCorruptionMessage);
        }

        engine.ValidateStoredIntentClaims(claims, record);
        engine.RequireRemoteTransferAttemptHistory(record, claims);
        engine.RequireRemoteTransferRepairHistory(view, record, claims);
        RemoteTransferValidation.ValidateIntentRecordState(record);
        RemoteTransferAttemptStateValidation.Require(record.Attempts);
        engine.ValidateReceiptToken(view, record);
    }

    internal static bool HasInvalidIntentRecordShape(RemoteTransferIntentRecord record, QueueLaneRef source, Guid transferId) => record.Source != source || record.TransferId != transferId || record.TransferId == Guid.Empty || record.Destination is null || record.PrincipalId is null || record.Message is null || record.IntentToken is null || record.ReceiptReservationBytes < MessagingCommandsRemoteTransferRemoteTransferCommands.RemoteTransferCommandsNoRetainedBytes || record.Fingerprint is null;
    internal static void ValidateIntentRecordState(RemoteTransferIntentRecord record)
    {
        if (record.State is not (QueueTransferState.OutputPending or QueueTransferState.Delivered) || record.State == QueueTransferState.OutputPending && record.ReceiptToken is not null || record.State == QueueTransferState.Delivered && record.ReceiptToken is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferCorruptionMessage);
        }
    }

    internal static void ValidateReceiptToken(this DatabaseEngine engine, IKeyValueView view, RemoteTransferIntentRecord record)
    {
        if (record.ReceiptToken is { } receiptToken)
        {
            var receipt = engine.Verify<RemoteTransferReceiptClaims>(receiptToken, engine.Limits.MaxBatchBytes);
            if (receipt is null || receipt.Source is null || receipt.Destination is null || receipt.TargetCommit is null)
            {
                throw Errors.Fail(ErrorCode.Corruption, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferCorruptionMessage);
            }

            engine.ValidateReceipt(view, receipt, record);
        }
    }

    internal static void ValidateStoredIntentClaims(this DatabaseEngine engine, RemoteTransferIntentClaims claims, RemoteTransferIntentRecord record)
    {
        if (claims is null || claims.Source is null || claims.Destination is null || claims.Message is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferCorruptionMessage);
        }

        RemoteTransferValidation.ValidateTransferPair(record.Source, record.Destination);
        if (claims.Purpose != RemoteTransferProtocol.IntentPurpose || claims.Incarnation != engine.Store.Identity.Incarnation || claims.Source != record.Source || claims.TransferId != record.TransferId || claims.Destination != record.Destination || claims.PrincipalId != record.PrincipalId || claims.Message != record.Message || claims.Fingerprint != record.Fingerprint || JsonData.Fingerprint(record.Message) != record.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, MessagingCommandsRemoteTransferRemoteTransferCommands.TransferCorruptionMessage);
        }
    }
}
