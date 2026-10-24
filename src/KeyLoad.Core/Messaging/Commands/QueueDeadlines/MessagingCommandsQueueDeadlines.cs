using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsQueueDeadlines operations against the caller's database context.</summary>
public static class MessagingCommandsQueueDeadlines
{
    internal static MutationReceipt ApplyQueueDeadline(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, AdvanceQueueDeadline request, DateTimeOffset now, Command<DatabaseCommand>? operation)
    {
        if (operation is null || operation.Value!.Kind != OperationKind.Batch || operation.Timestamp != now)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, QueueDeadlineProtocol.BatchRequired);
        }

        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = engine.Resource(tx, partition, request.Queue, ResourceKind.WorkQueue);
        if (resource.Paused || engine.DispatchPaused(tx))
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, QueueSweepTransitions.DispatchPausedMessage);
        }

        var metadata = MessagingValidationQueueDeadlines.RequireQueueDeadlineState(tx, lane, request, now);
        if (request.Kind == QueueDeadlineKind.ExpireMessage)
        {
            return ExpireQueueDeadlineMessage(tx, lane, resource.QueuePolicy, metadata);
        }

        var leased = request.Kind == QueueDeadlineKind.ExpireLease;
        var key = MessagingExecutionMessaging.QueueKey(leased ? QueueSweepTransitions.LeasedQueueSpace : QueueSweepTransitions.ScheduledQueueSpace, lane, request.ExpectedDeadline, metadata.Id);
        var value = tx.ReadOwnedValue(key) ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        var counters = engine.ApplyDueEntry(tx, lane, resource.QueuePolicy, now, leased, new(key, value), QueueDeadLetterAuthority.RequireQueueLifecycleCounters(tx, lane), operation);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.QueueCountersSpace, lane), counters);
        var updated = tx.GetRecord<MessageMetadata>(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, metadata.Id)) ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingMetadata);
        return new(QueueDeadlineProtocol.ReceiptKind, lane.Queue, metadata.Id, updated.StateVersion);
    }

    internal static MutationReceipt ExpireQueueDeadlineMessage(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, MessageMetadata metadata)
    {
        var body = StoredMessageBody.Read(tx, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, metadata.Id)) ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingBody);
        if (body.Body.Id != metadata.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingBody);
        }

        var removal = QueueLifecycleRemovalPreparation.PrepareQueueLifecycleRemoval(tx, lane, metadata, QueueDeadLetterAuthority.RequireQueueLifecycleCounters(tx, lane), body.Bytes);
        var counters = QueueLifecycleAccounting.ReleaseQueueStored(removal.Counters, body.Bytes);
        var order = QueueOrderMutationPreparation.PrepareQueueOrderRemoval(tx, lane, policy, body.Body, metadata);
        QueueDueTransitionPreparation.RequireQueueDueStateCapacity(metadata);
        QueueLifecycleRemovalApply.ApplyQueueLifecycleRemoval(tx, removal);
        QueuePreparedOrderApply.ApplyQueuePreparedOrderRemoval(tx, order);
        var updated = metadata with
        {
            State = MessageState.Expired,
            StateVersion = checked(metadata.StateVersion + QueueDeadlineProtocol.Step),
            ActiveOrderSequence = QueueDeadlineProtocol.Initial,
            LeaseOwner = null,
            LeaseUntil = null
        };
        tx.Delete(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, metadata.Id));
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.QueueCountersSpace, lane), counters);
        return new(QueueDeadlineProtocol.ReceiptKind, lane.Queue, metadata.Id, updated.StateVersion);
    }
}
