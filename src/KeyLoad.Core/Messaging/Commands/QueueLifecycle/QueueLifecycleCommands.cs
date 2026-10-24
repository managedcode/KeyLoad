using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueueLifecycleCommands operations against the caller's database context.</summary>
public static class QueueLifecycleCommands
{
    internal static MutationReceipt ApplyRedriveQueueMessage(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, RedriveQueueMessage request, DateTimeOffset now)
    {
        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = engine.Resource(tx, partition, lane.Queue, ResourceKind.WorkQueue);
        if (resource.Paused)
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, QueueSweepTransitions.QueuePaused);
        }

        var (metadata, body) = QueueLifecycleConditions.RequireQueueLifecycleState(tx, lane, request);
        if (metadata.State is not (MessageState.PendingDeadLetter or MessageState.DeadLettered))
        {
            throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState);
        }

        if (metadata.ExpiresAt <= now || metadata.ExpiresAt <= request.NotBefore)
        {
            throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.ExpiredRedrive);
        }

        var removal = QueueLifecycleRemovalPreparation.PrepareQueueLifecycleRemoval(tx, lane, metadata, QueueDeadLetterAuthority.RequireQueueLifecycleCounters(tx, lane), body.Bytes);
        var counters = removal.Counters;
        var scheduled = request.NotBefore > now;
        var order = QueueOrderMutationPreparation.PrepareQueueRedriveOrder(tx, lane, resource.QueuePolicy, body.Body, metadata, counters);
        QueueLifecycleIncrementCapacity.RequireQueueLifecycleIdentityCapacity(metadata);
        QueueLifecycleIncrementCapacity.RequireQueueRedriveReadyCapacity(counters, scheduled);
        QueueLifecycleRemovalApply.ApplyQueueLifecycleRemoval(tx, removal);
        var sequence = scheduled ? QueueLifecycleProtocol.Initial : checked(counters.NextReadySequence + QueueLifecycleProtocol.Increment);
        var updated = metadata with
        {
            State = scheduled ? MessageState.Scheduled : MessageState.Ready,
            StateVersion = checked(metadata.StateVersion + QueueLifecycleProtocol.Increment),
            DeliveryGeneration = checked(metadata.DeliveryGeneration + QueueLifecycleProtocol.Increment),
            LeaseVersion = checked(metadata.LeaseVersion + QueueLifecycleProtocol.Increment),
            Attempts = QueueLifecycleProtocol.Initial,
            ReadySequence = sequence,
            NotBefore = request.NotBefore,
            LeaseOwner = null,
            LeaseUntil = null,
            SafeFailureCode = null,
            ParkedSequence = QueueLifecycleProtocol.Initial
        };
        updated = QueuePreparedOrderApply.ApplyQueueRedriveOrder(tx, lane, order, updated, ref counters);
        tx.PutRecord(scheduled ? MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.ScheduledQueueSpace, lane, request.NotBefore!.Value, metadata.Id) : MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.ReadyQueueSpace, lane, sequence, metadata.Id), metadata.Id);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.QueueCountersSpace, lane), scheduled ? counters : counters with { NextReadySequence = sequence });
        return new(QueueLifecycleProtocol.RedriveKind, lane.Queue, metadata.Id, updated.StateVersion);
    }

    internal static MutationReceipt ApplyCancelQueueMessage(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, CancelQueueMessage request)
    {
        var lane = new QueueLaneRef(partition, request.Queue);
        var (metadata, body) = QueueLifecycleConditions.RequireQueueLifecycleState(tx, lane, request);
        var removal = QueueLifecycleRemovalPreparation.PrepareQueueLifecycleRemoval(tx, lane, metadata, QueueDeadLetterAuthority.RequireQueueLifecycleCounters(tx, lane), body.Bytes);
        var resource = engine.Resource(tx, partition, lane.Queue, ResourceKind.WorkQueue);
        var order = QueueOrderMutationPreparation.PrepareQueueOrderRemoval(tx, lane, resource.QueuePolicy, body.Body, metadata);
        var counters = QueueLifecycleAccounting.ReleaseQueueStored(removal.Counters, body.Bytes);
        QueueLifecycleIncrementCapacity.RequireQueueLifecycleIdentityCapacity(metadata);
        QueueLifecycleRemovalApply.ApplyQueueLifecycleRemoval(tx, removal);
        QueuePreparedOrderApply.ApplyQueuePreparedOrderRemoval(tx, order);
        var updated = metadata with
        {
            State = MessageState.Cancelled,
            StateVersion = checked(metadata.StateVersion + QueueLifecycleProtocol.Increment),
            DeliveryGeneration = checked(metadata.DeliveryGeneration + QueueLifecycleProtocol.Increment),
            LeaseVersion = checked(metadata.LeaseVersion + QueueLifecycleProtocol.Increment),
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = null,
            ParkedSequence = QueueLifecycleProtocol.Initial,
            ActiveOrderSequence = QueueOrderProtocol.Initial
        };
        tx.Delete(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, metadata.Id));
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.QueueCountersSpace, lane), counters);
        return new(QueueLifecycleProtocol.CancelKind, lane.Queue, metadata.Id, updated.StateVersion);
    }

    internal static MutationReceipt ApplyParkPendingQueueMessage(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, ParkPendingQueueMessage request, DateTimeOffset now)
    {
        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = engine.Resource(tx, partition, lane.Queue, ResourceKind.WorkQueue);
        var (metadata, body) = QueueLifecycleConditions.RequireQueueLifecycleState(tx, lane, request);
        if (metadata.State != MessageState.PendingDeadLetter)
        {
            throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState);
        }

        if (metadata.ExpiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.ExpiredRedrive);
        }

        var counters = QueueDeadLetterAuthority.RequireQueueLifecycleCounters(tx, lane);
        if (!MessagingCommands.FitsQueueDeadLetter(resource.QueuePolicy, counters, body.Bytes))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleProtocol.FullDeadLetter);
        }

        var removal = QueueLifecycleRemovalPreparation.PrepareQueueLifecycleRemoval(tx, lane, metadata, counters, body.Bytes);
        var admission = QueueDeadLetters.PrepareQueueDeadLetterAdmission(tx, lane, resource.QueuePolicy, metadata, body.Bytes, counters);
        QueueLifecycleRemovalApply.ApplyQueueLifecycleRemoval(tx, removal);
        var updated = QueuePreparedOrderApply.ApplyQueueDeadLetterAdmission(tx, lane, admission, ref counters);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.QueueCountersSpace, lane), counters);
        return new(QueueLifecycleProtocol.ParkKind, lane.Queue, metadata.Id, updated.StateVersion);
    }
}
