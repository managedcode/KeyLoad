using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void ApplyQueuePreparedOrderRemoval(IAtomicTransaction transaction, byte[]? key)
    {
        if (key is not null)
        {
            transaction.Delete(key);
        }
    }

    private static MessageMetadata ApplyQueueRedriveOrder(IAtomicTransaction transaction, QueueLaneRef lane,
        QueueRedriveOrderPlan plan, MessageMetadata updated, ref QueueCounters counters)
    {
        if (plan.OrderingKey is null)
        {
            return updated;
        }
        updated = updated with
        {
            ActiveOrderSequence = plan.ActiveOrderSequence
        };
        PutQueueOrder(transaction, lane, plan.OrderingKey, updated);
        counters = counters with
        {
            NextOrderSequence = plan.NextOrderSequence
        };
        return updated;
    }

    private static MessageMetadata ApplyQueueDeadLetterAdmission(IAtomicTransaction transaction, QueueLaneRef lane,
        QueueDeadLetterAdmissionPlan plan, ref QueueCounters counters)
    {
        ApplyQueuePreparedOrderRemoval(transaction, plan.Order.KeyToDelete);
        var metadata = plan.Metadata;
        if (metadata.State == MessageState.PendingDeadLetter)
        {
            transaction.PutRecord(QueueKey(QueueLifecycleProtocol.PendingSpace, lane, metadata.Id),
                new QueuePendingDeadLetterReference(metadata.Id, metadata.DeliveryGeneration, AttemptsExhausted));
        }
        else
        {
            transaction.PutRecord(QueueKey(DeadLetterQueueSpace, lane, metadata.Id), metadata.Id);
            transaction.PutRecord(QueueKey(QueueLifecycleProtocol.ParkedSpace, lane, metadata.ParkedSequence, metadata.Id),
                new QueueDeadLetterReference(metadata.Id, metadata.ParkedSequence, metadata.DeliveryGeneration, AttemptsExhausted));
        }
        counters = plan.Counters;
        return metadata;
    }
}
