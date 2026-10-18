using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteTransferSourceDispatch? CaptureRemoteTransferReceiptDispatch(string principalId,
        InspectQueueTransferReceiptRequest request, ReadExecutionBudget work)
        => CaptureRemoteTransferReceiptDispatchCore(principalId, request, work, bindResourceLifetime: false);

    internal RemoteTransferSourceDispatch? CaptureRemoteTransferReceiptDispatchBound(string principalId,
        InspectQueueTransferReceiptRequest request, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        return CaptureRemoteTransferReceiptDispatchCore(principalId, request, work, bindResourceLifetime: true);
    }

    private RemoteTransferSourceDispatch? CaptureRemoteTransferReceiptDispatchCore(string principalId,
        InspectQueueTransferReceiptRequest request, ReadExecutionBudget work, bool bindResourceLifetime)
    {
        work.Check();
        ValidateTransferPair(request.SourceQueue, request.DestinationQueue);
        if (request.TransferId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Validation, TransferInvalidMessage);
        }
        work.ChargeBytes(NativeSerialization.Measure(request));
        return Store.Read(view => ReadRemoteTransferReceiptDispatch(work.CreateView(view), principalId, request, work, bindResourceLifetime));
    }

    private RemoteTransferSourceDispatch? ReadRemoteTransferReceiptDispatch(IKeyValueView view, string principalId,
        InspectQueueTransferReceiptRequest request, ReadExecutionBudget work, bool bindResourceLifetime = false)
    {
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        RequireTransferAdministrator(principal);
        RequireTransferCapabilities(principal, request.SourceQueue, Capability.QueueInspect);
        RequireTransferCapabilities(principal, request.DestinationQueue, Capability.QueueInspect);
        var source = Resource(view, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
        RequireTransferInspector(principal, request.SourceQueue, source);
        var current = CaptureRemoteTransferTarget(view, request.DestinationQueue);
        if (current is null)
        {
            return null;
        }
        var destination = RemoteTransferDestinationResource(view, request.DestinationQueue);
        RequireTransferInspector(principal, request.DestinationQueue, destination);
        if (bindResourceLifetime)
        {
            _ = BindAuthorizedReadResource(view, request.SourceQueue.Partition, request.SourceQueue.Queue, ResourceKind.WorkQueue);
            destination = RemoteTransferDestinationReadResource(view, request.DestinationQueue);
        }
        var intent = view.GetRecord<RemoteTransferIntentRecord>(RemoteTransferStorage.IntentKey(request.SourceQueue, request.TransferId))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RemoteTransferPeerProtocol.Unavailable);
        ValidateIntentRecord(view, intent, request.SourceQueue, request.TransferId);
        if (intent.PrincipalId != principal.Id || intent.Destination != request.DestinationQueue
            || intent.RemoteTarget is null
            || !PhysicalOwnerEntryValidation.Same(intent.RemoteTarget.DestinationOwner, current.DestinationOwner))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferPeerProtocol.Invalid);
        }
        _ = RemoteTransferStorage.RequireSourceCounter(view, request.SourceQueue);
        var claims = Verify<RemoteTransferIntentClaims>(intent.IntentToken, Limits.MaxBatchBytes);
        ValidateIntentClaims(claims, request.DestinationQueue, principal.Id);
        return CaptureRemoteTransferSourcePolicy(view, principal, claims, intent, source, destination, work);
    }

    private ResourceDefinition RemoteTransferDestinationReadResource(IKeyValueView view, QueueLaneRef destination)
    {
        if (CaptureRemoteTransferTarget(view, destination) is null)
        {
            return BindAuthorizedReadResource(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
        }
        var pair = ReadBoundResourcePair(view, destination.Partition, destination.Queue, ResourceKind.WorkQueue);
        BindAuthenticatedReadResourceLifetime(view, destination.Partition, pair.Definition, pair.Lifetime);
        return pair.Definition;
    }

}
