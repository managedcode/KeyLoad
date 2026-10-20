using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsQueueDeadlinesQueueDueTransitionApply operations against the caller's database context.</summary>
public static class MessagingCommandsQueueDeadlinesQueueDueTransitionApply
{
    internal static void ApplyQueueDueTransition(IAtomicTransaction transaction, QueueLaneRef lane, QueueDueTransitionPlan plan)
    {
        if (plan.DeadLetter is { } admission)
        {
            var counters = plan.Counters;
            _ = MessagingCommandsQueueOrderingQueuePreparedOrderApply.ApplyQueueDeadLetterAdmission(transaction, lane, admission, ref counters);
            return;
        }

        MessagingCommandsQueueOrderingQueuePreparedOrderApply.ApplyQueuePreparedOrderRemoval(transaction, plan.OrderKeyToDelete);
        if (plan.RemoveBody)
        {
            transaction.Delete(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.MessageBodySpace, lane, plan.Metadata.Id));
        }

        if (plan.AvailabilityKey is { } key)
        {
            transaction.PutRecord(key, plan.Metadata.Id);
        }
    }
}
