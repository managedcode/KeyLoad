using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueuePreparedOrderApply operations against the caller's database context.</summary>
public static class QueuePreparedOrderApply
{
    internal static void ApplyQueuePreparedOrderRemoval(IAtomicTransaction transaction, byte[]? key)
    {
        if (key is not null)
        {
            transaction.Delete(key);
        }
    }

    internal static MessageMetadata ApplyQueueRedriveOrder(IAtomicTransaction transaction, QueueLaneRef lane, QueueRedriveOrderPlan plan, MessageMetadata updated, ref QueueCounters counters)
    {
        if (plan.OrderingKey is null)
        {
            return updated;
        }

        updated = updated with
        {
            ActiveOrderSequence = plan.ActiveOrderSequence
        };
        MessagingCommandsQueueOrdering.PutQueueOrder(transaction, lane, plan.OrderingKey, updated);
        counters = counters with
        {
            NextOrderSequence = plan.NextOrderSequence
        };
        return updated;
    }

    internal static MessageMetadata ApplyQueueDeadLetterAdmission(IAtomicTransaction transaction, QueueLaneRef lane, QueueDeadLetterAdmissionPlan plan, ref QueueCounters counters)
    {
        ApplyQueuePreparedOrderRemoval(transaction, plan.Order.KeyToDelete);
        var metadata = plan.Metadata;
        if (metadata.State == MessageState.PendingDeadLetter)
        {
            transaction.PutRecord(MessagingExecutionMessaging.QueueKey(QueueLifecycleProtocol.PendingSpace, lane, metadata.Id), new QueuePendingDeadLetterReference(metadata.Id, metadata.DeliveryGeneration, QueueSweepTransitions.AttemptsExhausted));
        }
        else
        {
            transaction.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.DeadLetterQueueSpace, lane, metadata.Id), metadata.Id);
            transaction.PutRecord(MessagingExecutionMessaging.QueueKey(QueueLifecycleProtocol.ParkedSpace, lane, metadata.ParkedSequence, metadata.Id), new QueueDeadLetterReference(metadata.Id, metadata.ParkedSequence, metadata.DeliveryGeneration, QueueSweepTransitions.AttemptsExhausted));
        }

        counters = plan.Counters;
        return metadata;
    }
}
