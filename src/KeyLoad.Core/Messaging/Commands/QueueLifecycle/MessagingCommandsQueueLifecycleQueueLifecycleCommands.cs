using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsQueueLifecycleQueueLifecycleCommands operations against the caller's database context.</summary>
public static class MessagingCommandsQueueLifecycleQueueLifecycleCommands
{
    internal static MutationReceipt ApplyRedriveQueueMessage(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, RedriveQueueMessage request, DateTimeOffset now)
    {
        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = engine.Resource(tx, partition, lane.Queue, ResourceKind.WorkQueue);
        if (resource.Paused)
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, MessagingCommandsQueueSweepTransitions.QueuePaused);
        }

        var (metadata, body) = MessagingValidationQueueLifecycleQueueLifecycleConditions.RequireQueueLifecycleState(tx, lane, request);
        if (metadata.State is not (MessageState.PendingDeadLetter or MessageState.DeadLettered))
        {
            throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState);
        }

        if (metadata.ExpiresAt <= now || metadata.ExpiresAt <= request.NotBefore)
        {
            throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.ExpiredRedrive);
        }

        var removal = MessagingValidationQueueLifecycleQueueLifecycleRemovalPreparation.PrepareQueueLifecycleRemoval(tx, lane, metadata, MessagingValidationQueueDeadLettersQueueDeadLetterAuthority.RequireQueueLifecycleCounters(tx, lane), body.Bytes);
        var counters = removal.Counters;
        var scheduled = request.NotBefore > now;
        var order = MessagingValidationQueueOrderingQueueOrderMutationPreparation.PrepareQueueRedriveOrder(tx, lane, resource.QueuePolicy, body.Body, metadata, counters);
        MessagingValidationQueueLifecycleQueueLifecycleIncrementCapacity.RequireQueueLifecycleIdentityCapacity(metadata);
        MessagingValidationQueueLifecycleQueueLifecycleIncrementCapacity.RequireQueueRedriveReadyCapacity(counters, scheduled);
        MessagingCommandsQueueLifecycleQueueLifecycleRemovalApply.ApplyQueueLifecycleRemoval(tx, removal);
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
        updated = MessagingCommandsQueueOrderingQueuePreparedOrderApply.ApplyQueueRedriveOrder(tx, lane, order, updated, ref counters);
        tx.PutRecord(scheduled ? MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.ScheduledQueueSpace, lane, request.NotBefore!.Value, metadata.Id) : MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.ReadyQueueSpace, lane, sequence, metadata.Id), metadata.Id);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.QueueCountersSpace, lane), scheduled ? counters : counters with { NextReadySequence = sequence });
        return new(QueueLifecycleProtocol.RedriveKind, lane.Queue, metadata.Id, updated.StateVersion);
    }

    internal static MutationReceipt ApplyCancelQueueMessage(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, CancelQueueMessage request)
    {
        var lane = new QueueLaneRef(partition, request.Queue);
        var (metadata, body) = MessagingValidationQueueLifecycleQueueLifecycleConditions.RequireQueueLifecycleState(tx, lane, request);
        var removal = MessagingValidationQueueLifecycleQueueLifecycleRemovalPreparation.PrepareQueueLifecycleRemoval(tx, lane, metadata, MessagingValidationQueueDeadLettersQueueDeadLetterAuthority.RequireQueueLifecycleCounters(tx, lane), body.Bytes);
        var resource = engine.Resource(tx, partition, lane.Queue, ResourceKind.WorkQueue);
        var order = MessagingValidationQueueOrderingQueueOrderMutationPreparation.PrepareQueueOrderRemoval(tx, lane, resource.QueuePolicy, body.Body, metadata);
        var counters = MessagingCommandsQueueLifecycleQueueLifecycleAccounting.ReleaseQueueStored(removal.Counters, body.Bytes);
        MessagingValidationQueueLifecycleQueueLifecycleIncrementCapacity.RequireQueueLifecycleIdentityCapacity(metadata);
        MessagingCommandsQueueLifecycleQueueLifecycleRemovalApply.ApplyQueueLifecycleRemoval(tx, removal);
        MessagingCommandsQueueOrderingQueuePreparedOrderApply.ApplyQueuePreparedOrderRemoval(tx, order);
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
        tx.Delete(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.MessageBodySpace, lane, metadata.Id));
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.QueueCountersSpace, lane), counters);
        return new(QueueLifecycleProtocol.CancelKind, lane.Queue, metadata.Id, updated.StateVersion);
    }

    internal static MutationReceipt ApplyParkPendingQueueMessage(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, ParkPendingQueueMessage request, DateTimeOffset now)
    {
        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = engine.Resource(tx, partition, lane.Queue, ResourceKind.WorkQueue);
        var (metadata, body) = MessagingValidationQueueLifecycleQueueLifecycleConditions.RequireQueueLifecycleState(tx, lane, request);
        if (metadata.State != MessageState.PendingDeadLetter)
        {
            throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState);
        }

        if (metadata.ExpiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.ExpiredRedrive);
        }

        var counters = MessagingValidationQueueDeadLettersQueueDeadLetterAuthority.RequireQueueLifecycleCounters(tx, lane);
        if (!MessagingCommands.FitsQueueDeadLetter(resource.QueuePolicy, counters, body.Bytes))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleProtocol.FullDeadLetter);
        }

        var removal = MessagingValidationQueueLifecycleQueueLifecycleRemovalPreparation.PrepareQueueLifecycleRemoval(tx, lane, metadata, counters, body.Bytes);
        var admission = MessagingValidationQueueDeadLetters.PrepareQueueDeadLetterAdmission(tx, lane, resource.QueuePolicy, metadata, body.Bytes, counters);
        MessagingCommandsQueueLifecycleQueueLifecycleRemovalApply.ApplyQueueLifecycleRemoval(tx, removal);
        var updated = MessagingCommandsQueueOrderingQueuePreparedOrderApply.ApplyQueueDeadLetterAdmission(tx, lane, admission, ref counters);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.QueueCountersSpace, lane), counters);
        return new(QueueLifecycleProtocol.ParkKind, lane.Queue, metadata.Id, updated.StateVersion);
    }
}
