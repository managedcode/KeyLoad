using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferReauthorization operations against the caller's database context.</summary>
public static class RemoteTransferReauthorization
{
    internal const string TransferOutcomeMissingMessage = "The committed queue transfer effect is unavailable.";
    internal static void AuthorizeQueueTransferRequest(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        switch (mutation)
        {
            case AdvanceQueueTransferAttempt advance:
                engine.AuthorizeAdvanceQueueTransferAttempt(view, principal, partition, advance);
                break;
            case AdvanceQueueTransferRepair repair:
                engine.AuthorizeAdvanceQueueTransferRepair(view, principal, partition, repair);
                break;
            case CreateQueueTransfer create:
                engine.AuthorizeCreateQueueTransfer(view, principal, partition, create);
                break;
            case AcceptQueueTransfer accept:
                engine.AuthorizeAcceptQueueTransfer(view, principal, partition, accept);
                break;
            case CompleteQueueTransfer complete:
                engine.AuthorizeCompleteQueueTransfer(view, principal, partition, complete);
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, RemoteTransferCommands.TransferInvalidMessage);
        }
    }

    internal static void ReauthorizeQueueTransfer(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, Mutation mutation)
    {
        switch (mutation)
        {
            case AdvanceQueueTransferAttempt advance:
                engine.ReauthorizeAdvanceQueueTransferAttempt(view, principal, partition, advance);
                break;
            case AdvanceQueueTransferRepair repair:
                engine.ReauthorizeAdvanceQueueTransferRepair(view, principal, partition, repair);
                break;
            case CreateQueueTransfer create:
                engine.ReauthorizeCreateQueueTransfer(view, principal, partition, create);
                break;
            case AcceptQueueTransfer accept:
                engine.ReauthorizeAcceptQueueTransfer(view, principal, partition, accept);
                break;
            case CompleteQueueTransfer complete:
                engine.ReauthorizeCompleteQueueTransfer(view, principal, partition, complete);
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, RemoteTransferCommands.TransferInvalidMessage);
        }
    }

    internal static void ReauthorizeCreateQueueTransfer(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, CreateQueueTransfer request)
    {
        engine.AuthorizeCreateQueueTransfer(view, principal, partition, request);
        if (request.Message is null)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferCommands.TransferInvalidMessage);
        }

        var fingerprint = JsonData.Fingerprint(request.Message);
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var record = view.GetRecord<RemoteTransferIntentRecord>(key) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, TransferOutcomeMissingMessage);
        engine.RequireExistingIntent(view, record, request, principal.Id, fingerprint);
        _ = RemoteTransferStorage.RequireSourceCounter(view, request.SourceQueue);
    }

    internal static void ReauthorizeAcceptQueueTransfer(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, AcceptQueueTransfer request)
    {
        var claims = engine.AuthorizeAcceptQueueTransfer(view, principal, partition, request);
        var key = RemoteTransferStorage.TargetReceiptKey(claims.Source, claims.TransferId, claims.Destination);
        var record = view.GetRecord<RemoteTransferTargetReceiptRecord>(key) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, TransferOutcomeMissingMessage);
        engine.RequireExistingTargetReceipt(view, record, claims);
        _ = RemoteTransferStorage.RequireTargetCounter(view, claims.Destination);
    }

    internal static RemoteTransferIntentClaims AuthorizeAcceptQueueTransfer(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, AcceptQueueTransfer request)
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
        var resource = engine.Resource(view, request.DestinationQueue.Partition, request.DestinationQueue.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferPublisher(principal, request.DestinationQueue, resource);
        return claims;
    }

    internal static void ReauthorizeCompleteQueueTransfer(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, CompleteQueueTransfer request)
    {
        engine.AuthorizeCompleteQueueTransfer(view, principal, partition, request);
        var key = RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId);
        var record = view.GetRecord<RemoteTransferIntentRecord>(key) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, TransferOutcomeMissingMessage);
        engine.ValidateIntentRecord(view, record, request.SourceQueue, request.TransferId);
        if (record.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferCommands.TransferReceiptInvalidMessage);
        }

        var claims = engine.Verify<RemoteTransferReceiptClaims>(request.ReceiptToken, engine.Limits.MaxBatchBytes);
        engine.ValidateReceipt(view, claims, record);
        if (record.State != QueueTransferState.Delivered || record.ReceiptToken != request.ReceiptToken)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, TransferOutcomeMissingMessage);
        }

        _ = RemoteTransferStorage.RequireSourceCounter(view, request.SourceQueue);
    }

    internal static void AuthorizeCreateQueueTransfer(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, CreateQueueTransfer request)
    {
        RemoteTransferValidation.ValidateTransferSource(request, partition);
        RemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, request.SourceQueue, Capability.QueuePublish);
        engine.RequireTransferCapabilities(principal, request.Destination, Capability.QueuePublish);
        var sourceResource = engine.Resource(view, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        var targetResource = engine.RemoteTransferDestinationResource(view, request.Destination);
        engine.RequireTransferPublisher(principal, request.SourceQueue, sourceResource);
        engine.RequireTransferPublisher(principal, request.Destination, targetResource);
        if (request.Message is null)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferCommands.TransferInvalidMessage);
        }
    }

    internal static void AuthorizeCompleteQueueTransfer(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, CompleteQueueTransfer request)
    {
        RemoteTransferInspection.ValidateTransferSource(request.SourceQueue, request.TransferId, partition);
        RemoteTransferAuthorization.RequireTransferAdministrator(principal);
        engine.RequireTransferCapabilities(principal, request.SourceQueue, Capability.QueuePublish);
        var resource = engine.Resource(view, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        engine.RequireTransferPublisher(principal, request.SourceQueue, resource);
        if (request.ReceiptToken is null)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteTransferCommands.TransferInvalidMessage);
        }

        var claims = engine.Verify<RemoteTransferReceiptClaims>(request.ReceiptToken, engine.Limits.MaxBatchBytes);
        engine.ValidateReceiptRequestScope(view, claims, request.SourceQueue, request.TransferId, principal.Id);
    }

    internal static void ValidateReceiptRequestScope(this DatabaseEngine engine, IKeyValueView view, RemoteTransferReceiptClaims claims, QueueLaneRef source, Guid transferId, string principalId)
    {
        if (claims is null || claims.Source is null || claims.Destination is null || claims.TargetCommit is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferCommands.TransferReceiptInvalidMessage);
        }

        RemoteTransferValidation.ValidateTransferPair(claims.Source, claims.Destination);
        if (claims.Purpose != RemoteTransferProtocol.ReceiptPurpose || claims.Incarnation != engine.Store.Identity.Incarnation || claims.Source != source || claims.TransferId != transferId || claims.PrincipalId != principalId || string.IsNullOrEmpty(claims.Fingerprint))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferCommands.TransferReceiptInvalidMessage);
        }

        engine.ValidateCommitToken(view, claims.Destination.Partition, claims.TargetCommit, ErrorCode.TokenInvalidated, RemoteTransferCommands.TransferReceiptInvalidMessage);
    }
}
