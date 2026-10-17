using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void ApplyQueueDueTransition(IAtomicTransaction transaction, QueueLaneRef lane,
        QueueDueTransitionPlan plan)
    {
        if (plan.DeadLetter is { } admission)
        {
            var counters = plan.Counters;
            _ = ApplyQueueDeadLetterAdmission(transaction, lane, admission, ref counters);
            return;
        }
        ApplyQueuePreparedOrderRemoval(transaction, plan.OrderKeyToDelete);
        if (plan.RemoveBody)
        {
            transaction.Delete(QueueKey(MessageBodySpace, lane, plan.Metadata.Id));
        }
        if (plan.AvailabilityKey is { } key)
        {
            transaction.PutRecord(key, plan.Metadata.Id);
        }
    }
}
