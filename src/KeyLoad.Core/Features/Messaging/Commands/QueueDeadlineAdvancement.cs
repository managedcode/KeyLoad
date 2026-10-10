using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private MutationReceipt ApplyQueueDeadline(IAtomicTransaction tx, PartitionRef partition,
        AdvanceQueueDeadline request, DateTimeOffset now, ReplicatedOperation? operation)
    {
        if (operation is null || operation.Kind != OperationKind.Batch || operation.EvaluatedAt != now)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, QueueDeadlineProtocol.BatchRequired); }
        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = Resource(tx, partition, request.Queue, ResourceKind.WorkQueue);
        if (resource.Paused || DispatchPaused(tx))
        { throw Errors.Fail(ErrorCode.DispatchPaused, DispatchPausedMessage); }
        var metadata = RequireQueueDeadlineState(tx, lane, request, now);
        if (request.Kind == QueueDeadlineKind.ExpireMessage)
        { return ExpireQueueDeadlineMessage(tx, lane, resource.QueuePolicy, metadata); }
        var leased = request.Kind == QueueDeadlineKind.ExpireLease;
        var key = QueueKey(leased ? LeasedQueueSpace : ScheduledQueueSpace, lane, request.ExpectedDeadline, metadata.Id);
        var value = tx.ReadOwnedValue(key) ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        var counters = ApplyDueEntry(tx, lane, resource.QueuePolicy, now, leased, new(key, value),
            RequireQueueLifecycleCounters(tx, lane), operation);
        tx.PutRecord(QueueKey(QueueCountersSpace, lane), counters);
        var updated = tx.GetRecord<MessageMetadata>(QueueKey(MessageMetadataSpace, lane, metadata.Id))
            ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingMetadata);
        return new(QueueDeadlineProtocol.ReceiptKind, lane.Queue, metadata.Id, updated.StateVersion);
    }

    private static MutationReceipt ExpireQueueDeadlineMessage(IAtomicTransaction tx, QueueLaneRef lane,
        QueuePolicy policy, MessageMetadata metadata)
    {
        var body = StoredMessageBody.Read(tx, QueueKey(MessageBodySpace, lane, metadata.Id))
            ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingBody);
        if (body.Body.Id != metadata.Id)
        { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingBody); }
        var counters = RemoveQueueLifecycleReferences(tx, lane, metadata, RequireQueueLifecycleCounters(tx, lane), body.Bytes);
        counters = ReleaseQueueStored(counters, body.Bytes);
        RemoveQueueOrder(tx, lane, policy, body.Body, metadata);
        var updated = metadata with
        {
            State = MessageState.Expired,
            StateVersion = checked(metadata.StateVersion + QueueDeadlineProtocol.Step),
            ActiveOrderSequence = QueueDeadlineProtocol.Initial,
            LeaseOwner = null,
            LeaseUntil = null
        };
        tx.Delete(QueueKey(MessageBodySpace, lane, metadata.Id));
        tx.PutRecord(QueueKey(MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(QueueKey(QueueCountersSpace, lane), counters);
        return new(QueueDeadlineProtocol.ReceiptKind, lane.Queue, metadata.Id, updated.StateVersion);
    }
}
