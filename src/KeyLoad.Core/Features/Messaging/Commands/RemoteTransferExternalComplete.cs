using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private MutationReceipt ApplyCompleteRemoteQueueTransfer(IAtomicTransaction tx, PrincipalRecord principal,
        CompleteQueueTransfer request, RemoteTransferIntentRecord record)
    {
        if (record.PrincipalId != principal.Id)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        ValidateRemoteTransferIntentReceipt(tx, record, request.ReceiptToken);
        if (record.State == QueueTransferState.Delivered)
        {
            if (record.ReceiptToken != request.ReceiptToken)
            { throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage); }
            return TransferMutationReceipt(RemoteTransferProtocol.CompleteReceiptKind, request.SourceQueue,
                request.TransferId, RemoteTransferCommandsTransferIdentifierSeparatorCount);
        }
        if (record.State != QueueTransferState.OutputPending)
        { throw Errors.Fail(ErrorCode.Corruption, TransferCorruptionMessage); }
        var updated = record with { State = QueueTransferState.Delivered, ReceiptToken = request.ReceiptToken };
        var capacity = RemoteTransferStorage.RequireSourceCounter(tx, request.SourceQueue);
        var next = RemoteTransferStorage.Replace(capacity, RemoteTransferStorage.SourceAccountedBytes(record),
            RemoteTransferStorage.SourceAccountedBytes(updated), Limits);
        tx.PutRecord(RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId), updated);
        tx.PutRecord(RemoteTransferStorage.SourceCapacityKey(request.SourceQueue), next);
        return TransferMutationReceipt(RemoteTransferProtocol.CompleteReceiptKind, request.SourceQueue,
            request.TransferId, RemoteTransferCommandsTransferIdentifierSeparatorCount);
    }

    private void ValidateRemoteTransferIntentReceipt(IKeyValueView view, RemoteTransferIntentRecord record,
        string token)
    {
        var target = record.RemoteTarget
            ?? throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid);
        ValidateRemoteTransferOriginalSourceCommit(view, record);
        var receipt = Verify<RemoteTransferExternalReceipt>(token, Limits.MaxBatchBytes);
        var currentTarget = CaptureRemoteTransferTarget(view, record.Destination);
        if (receipt is null || receipt.TargetCommit is null || receipt.SourceCut is null
            || receipt.SourceOwner is null || receipt.DestinationOwner is null
            || receipt.Purpose != RemoteTransferPeerProtocol.ExternalReceiptPurpose
            || receipt.SourceCut != target.OriginalSourceCommit
            || receipt.Source != record.Source || receipt.Destination != record.Destination
            || receipt.TransferId != record.TransferId || receipt.LogicalPrincipalId != record.PrincipalId
            || receipt.Fingerprint != record.Fingerprint || string.IsNullOrEmpty(receipt.OriginalTargetReceiptToken)
            || currentTarget is null || !PhysicalOwnerEntryValidation.Same(currentTarget.DestinationOwner, target.DestinationOwner)
            || !PhysicalOwnerEntryValidation.Same(receipt.DestinationOwner, target.DestinationOwner)
            || configuredPhysicalOwner is null
            || !PhysicalOwnerEntryValidation.SameOwner(receipt.SourceOwner.Owner, configuredPhysicalOwner)
            || receipt.TargetCommit.Incarnation != target.DestinationOwner.Owner.Incarnation
            || receipt.TargetCommit.AtomicPartitionId != record.Destination.Partition.AtomicPartitionId
            || receipt.TargetCommit.Position < RemoteTransferPeerProtocol.EmptyEncodedBytes
            || receipt.TargetCommit.OwnershipEpoch < RemoteTransferPeerProtocol.MinimumPolicyEpoch
            || receipt.OriginalTargetReceiptDigest != Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(receipt.OriginalTargetReceiptToken)))
            || Encoding.UTF8.GetByteCount(token) > record.ReceiptReservationBytes)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        ValidateCommitToken(view, record.Source.Partition, receipt.SourceCut,
            ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid);
    }
}
