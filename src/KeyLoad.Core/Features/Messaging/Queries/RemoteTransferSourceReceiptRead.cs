using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteTransferSourceDispatch? CaptureRemoteTransferReceiptDispatch(string principalId,
        InspectQueueTransferReceiptRequest request, ReadExecutionBudget work)
    {
        work.Check();
        ValidateTransferPair(request.SourceQueue, request.DestinationQueue);
        if (request.TransferId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage); }
        work.ChargeBytes(NativeSerialization.Measure(request));
        return Store.Read(view => ReadRemoteTransferReceiptDispatch(work.CreateView(view), principalId, request, work));
    }

    private RemoteTransferSourceDispatch? ReadRemoteTransferReceiptDispatch(IKeyValueView view, string principalId,
        InspectQueueTransferReceiptRequest request, ReadExecutionBudget work)
    {
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        RequireTransferAdministrator(principal);
        var source = Resource(view, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        RequireTransferInspector(principal, request.SourceQueue, source);
        var current = CaptureRemoteTransferTarget(view, request.DestinationQueue);
        if (current is null)
        { return null; }
        var destination = RemoteTransferDestinationResource(view, request.DestinationQueue);
        RequireTransferInspector(principal, request.DestinationQueue, destination);
        var intent = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        ValidateIntentRecord(view, intent, request.SourceQueue, request.TransferId);
        if (intent.PrincipalId != principal.Id || intent.Destination != request.DestinationQueue
            || intent.RemoteTarget is null
            || !PhysicalOwnerEntryValidation.Same(intent.RemoteTarget.DestinationOwner, current.DestinationOwner))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid); }
        _ = RemoteTransferStorage.RequireSourceCounter(view, request.SourceQueue);
        var claims = Verify<RemoteTransferIntentClaims>(intent.IntentToken, Limits.MaxBatchBytes);
        ValidateIntentClaims(claims, request.DestinationQueue, principal.Id);
        return CaptureRemoteTransferSourcePolicy(view, principal, claims, intent, destination, work);
    }
}
