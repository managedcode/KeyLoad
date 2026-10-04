using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string TransferOutcomeMissingMessage = "The committed queue transfer effect is unavailable.";

    internal void AuthorizeQueueTransferRequest(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, Mutation mutation)
    {
        switch (mutation)
        {
            case CreateQueueTransfer create:
                AuthorizeCreateQueueTransfer(view, principal, partition, create);
                break;
            case AcceptQueueTransfer accept:
                AuthorizeAcceptQueueTransfer(view, principal, partition, accept);
                break;
            case CompleteQueueTransfer complete:
                AuthorizeCompleteQueueTransfer(view, principal, partition, complete);
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
    }

    internal void ReauthorizeQueueTransfer(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, Mutation mutation)
    {
        switch (mutation)
        {
            case CreateQueueTransfer create:
                ReauthorizeCreateQueueTransfer(view, principal, partition, create);
                break;
            case AcceptQueueTransfer accept:
                ReauthorizeAcceptQueueTransfer(view, principal, partition, accept);
                break;
            case CompleteQueueTransfer complete:
                ReauthorizeCompleteQueueTransfer(view, principal, partition, complete);
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
    }

    private void ReauthorizeCreateQueueTransfer(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, CreateQueueTransfer request)
    {
        AuthorizeCreateQueueTransfer(view, principal, partition, request);
        if (request.Message is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
        var fingerprint = JsonData.Fingerprint(request.Message);
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var record = view.GetRecord<RemoteTransferIntentRecord>(key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, TransferOutcomeMissingMessage);
        RequireExistingIntent(record, request, principal.Id, fingerprint);
        _ = RemoteTransferStorage.RequireSourceCounter(view, request.SourceQueue);
    }

    private void ReauthorizeAcceptQueueTransfer(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, AcceptQueueTransfer request)
    {
        var claims = AuthorizeAcceptQueueTransfer(view, principal, partition, request);
        var key = RemoteTransferStorage.TargetReceiptKey(claims.Source, claims.TransferId, claims.Destination);
        var record = view.GetRecord<RemoteTransferTargetReceiptRecord>(key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, TransferOutcomeMissingMessage);
        RequireExistingTargetReceipt(record, claims);
        _ = RemoteTransferStorage.RequireTargetCounter(view, claims.Destination);
    }

    private RemoteTransferIntentClaims AuthorizeAcceptQueueTransfer(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, AcceptQueueTransfer request)
    {
        ValidateTransferDestination(request.DestinationQueue, partition);
        RequireTransferAdministrator(principal);
        if (request.IntentToken is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
        var claims = Verify<RemoteTransferIntentClaims>(request.IntentToken, Limits.MaxBatchBytes);
        ValidateIntentClaims(claims, request.DestinationQueue, principal.Id);
        var resource = Resource(view, request.DestinationQueue.Partition, request.DestinationQueue.Queue, ResourceKind.WorkQueue);
        RequireTransferPublisher(principal, request.DestinationQueue, resource);
        return claims;
    }

    private void ReauthorizeCompleteQueueTransfer(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, CompleteQueueTransfer request)
    {
        AuthorizeCompleteQueueTransfer(view, principal, partition, request);
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var record = view.GetRecord<RemoteTransferIntentRecord>(key)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, TransferOutcomeMissingMessage);
        ValidateIntentRecord(record, request.SourceQueue, request.TransferId);
        if (record.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
        }
        var claims = Verify<RemoteTransferReceiptClaims>(request.ReceiptToken, Limits.MaxBatchBytes);
        ValidateReceipt(claims, record);
        if (record.State != QueueTransferState.Delivered || record.ReceiptToken != request.ReceiptToken)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, TransferOutcomeMissingMessage);
        }
        _ = RemoteTransferStorage.RequireSourceCounter(view, request.SourceQueue);
    }

    private void AuthorizeCreateQueueTransfer(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, CreateQueueTransfer request)
    {
        ValidateTransferSource(request, partition);
        RequireTransferAdministrator(principal);
        var sourceResource = Resource(view, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        var targetResource = Resource(view, request.Destination.Partition, request.Destination.Queue, ResourceKind.WorkQueue);
        RequireTransferPublisher(principal, request.SourceQueue, sourceResource);
        RequireTransferPublisher(principal, request.Destination, targetResource);
        if (request.Message is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
    }

    private RemoteTransferReceiptClaims AuthorizeCompleteQueueTransfer(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, CompleteQueueTransfer request)
    {
        ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        RequireTransferAdministrator(principal);
        var resource = Resource(view, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        RequireTransferPublisher(principal, request.SourceQueue, resource);
        if (request.ReceiptToken is null)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
        var claims = Verify<RemoteTransferReceiptClaims>(request.ReceiptToken, Limits.MaxBatchBytes);
        ValidateReceiptRequestScope(claims, request.SourceQueue, request.TransferId, principal.Id);
        return claims;
    }

    private void ValidateReceiptRequestScope(RemoteTransferReceiptClaims claims, QueueLaneRef source,
        Guid transferId, string principalId)
    {
        if (claims is null || claims.Source is null || claims.Destination is null || claims.TargetCommit is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
        }
        ValidateTransferPair(claims.Source, claims.Destination);
        if (claims.Purpose != RemoteTransferProtocol.ReceiptPurpose || claims.Incarnation != Store.Identity.Incarnation
            || claims.Source != source || claims.TransferId != transferId || claims.PrincipalId != principalId
            || string.IsNullOrEmpty(claims.Fingerprint) || claims.TargetCommit.Incarnation != Store.Identity.Incarnation
            || claims.TargetCommit.AtomicPartitionId != claims.Destination.Partition.AtomicPartitionId
            || claims.TargetCommit.Position < 1 || claims.TargetCommit.OwnershipEpoch != 1)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TransferReceiptInvalidMessage);
        }
    }
}
