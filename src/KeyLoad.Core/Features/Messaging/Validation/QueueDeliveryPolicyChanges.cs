using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireQueueDeliveryPolicyChange(IKeyValueView view, ConfigureResourceRequest request,
        ResourceDefinition? previous)
    {
        if (previous?.Kind != ResourceKind.WorkQueue
            || previous.QueuePolicy.OrderingProfile == request.Definition.QueuePolicy.OrderingProfile
                && previous.QueuePolicy.ParkedHeadPolicy == request.Definition.QueuePolicy.ParkedHeadPolicy)
        { return; }
        long examined = QueueOrderProtocol.Initial;
        void Observe(long bytes)
        { examined = checked(examined + bytes); RequireNativeBudget(examined); }
        var metadata = view.VisitRange(KeyCodec.Encode([MessageMetadataSpace, request.TenantId, request.DatabaseId]),
            messagingExecution.QueueScanPageSize, (key, value) =>
            {
                var parts = KeyCodec.Decode(key);
                if (!QueuePolicyResourceKey(parts, request.Definition.Name))
                { return true; }
                var item = NativeSerialization.Deserialize<MessageMetadata>(value);
                if (item.Id != (string)parts[QueueOrderProtocol.MessageKeyComponent]!)
                { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority); }
                var lane = new QueueLaneRef(new(request.TenantId, request.DatabaseId,
                    (string)parts[QueueOrderProtocol.DomainKeyComponent]!, (string)parts[QueueOrderProtocol.PartitionKeyComponent]!), request.Definition.Name);
                _ = Counters(view, lane);
                if (view.ReadValue(QueueKey(MessageBodySpace, lane, item.Id), static _ => { }, Observe))
                { throw Errors.Fail(ErrorCode.Conflict, QueueOrderProtocol.NonemptyChange); }
                if (item.State is not (MessageState.Acked or MessageState.Cancelled or MessageState.Expired))
                { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority); }
                return true;
            }, observer: Observe);
        if (metadata.HasMore)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, QueueOrderProtocol.IncompleteAbsence); }
        RequireQueuePolicyBodyAbsence(view, request, Observe);
    }

    private void RequireQueuePolicyBodyAbsence(IKeyValueView view, ConfigureResourceRequest request, StorageReadObserver observe)
    {
        var bodies = view.VisitRange(KeyCodec.Encode([MessageBodySpace, request.TenantId, request.DatabaseId]),
            messagingExecution.QueueScanPageSize, (key, _) =>
            {
                if (QueuePolicyResourceKey(KeyCodec.Decode(key), request.Definition.Name))
                { throw Errors.Fail(ErrorCode.Conflict, QueueOrderProtocol.NonemptyChange); }
                return true;
            }, observer: observe);
        if (bodies.HasMore)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, QueueOrderProtocol.IncompleteAbsence); }
    }

    private static bool QueuePolicyResourceKey(object?[] parts, string resource)
    {
        if (parts.Length != QueueOrderProtocol.MessageKeyComponents || parts[QueueOrderProtocol.ResourceKeyComponent] is not string queue
            || parts[QueueOrderProtocol.DomainKeyComponent] is not string || parts[QueueOrderProtocol.PartitionKeyComponent] is not string
            || parts[QueueOrderProtocol.MessageKeyComponent] is not string)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority); }
        return string.Equals(queue, resource, StringComparison.Ordinal);
    }
}
