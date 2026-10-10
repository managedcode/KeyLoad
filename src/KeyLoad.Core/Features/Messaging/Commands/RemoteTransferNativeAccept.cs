using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private MutationReceipt ApplyAcceptRemoteTransferOrLocal(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, AcceptQueueTransfer request, DateTimeOffset now, long position,
        ReplicatedOperation? operation)
    {
        var proof = operation is null ? null : ReadRemoteTransferNativeProof(operation);
        if (proof is null)
        { return ApplyAcceptQueueTransfer(tx, principal, partition, request, now, position); }
        RequireRemoteTransferPeerAtView(tx, principal, proof);
        ValidateTransferDestination(request.DestinationQueue, partition);
        var claims = proof.Call.IntentClaims;
        var origin = new RemoteTransferRemoteOrigin(proof.Call.SourceOwner, claims.PrincipalId,
            RemoteTransferCoordinationIdentity.IntentDigest(request.IntentToken));
        var key = RemoteTransferStorage.TargetReceiptKey(claims.Source, claims.TransferId, claims.Destination);
        var existing = tx.GetRecord<RemoteTransferTargetReceiptRecord>(key);
        if (existing is not null)
        {
            RequireExistingTargetReceipt(tx, existing, claims);
            RequireRemoteTransferOrigin(existing.RemoteOrigin, origin);
            RemoteTransferStorage.RequireTargetCounter(tx, claims.Destination);
            return TransferMutationReceipt(RemoteTransferProtocol.AcceptReceiptKind, claims.Destination,
                claims.TransferId, existing.TargetCommit.Position);
        }
        ValidateTransferMessage(claims.Message, claims.Destination, now);
        var targetCommit = Token(tx, partition, position);
        var receiptClaims = new RemoteTransferReceiptClaims(RemoteTransferProtocol.ReceiptPurpose,
            Store.Identity.Incarnation, claims.Source, claims.TransferId, claims.Destination, claims.PrincipalId,
            claims.Fingerprint, targetCommit);
        var receiptToken = Sign(receiptClaims);
        RequireTokenBound(receiptToken);
        var record = new RemoteTransferTargetReceiptRecord(claims.Source, claims.TransferId, claims.Destination,
            claims.PrincipalId, claims.Fingerprint, receiptToken, targetCommit)
        { RemoteOrigin = origin };
        var bytes = RemoteTransferStorage.SerializedBytes(record);
        var capacity = RemoteTransferStorage.TargetCapacity(tx, claims.Destination);
        RemoteTransferStorage.RequireCapacity(capacity, bytes, Limits);
        _ = Enqueue(tx, principal, partition, claims.Message, now);
        tx.PutRecord(key, record);
        tx.PutRecord(RemoteTransferStorage.TargetCapacityKey(claims.Destination), RemoteTransferStorage.Add(capacity, bytes));
        return TransferMutationReceipt(RemoteTransferProtocol.AcceptReceiptKind, claims.Destination,
            claims.TransferId, targetCommit.Position);
    }

    private static void RequireRemoteTransferOrigin(RemoteTransferRemoteOrigin? actual,
        RemoteTransferRemoteOrigin expected)
    {
        if (actual is null || !PhysicalOwnerEntryValidation.Same(actual.SourceOwner, expected.SourceOwner)
            || actual.LogicalPrincipalId != expected.LogicalPrincipalId || actual.IntentDigest != expected.IntentDigest)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferPeerProtocol.Invalid); }
    }
}
