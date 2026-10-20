using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsQueueOrderingQueueOrderTransitions operations against the caller's database context.</summary>
public static class MessagingCommandsQueueOrderingQueueOrderTransitions
{
    internal static void RemoveQueueOrder(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, MessageBody body, MessageMetadata metadata)
    {
        MessagingCommandsQueueOrderingQueuePreparedOrderApply.ApplyQueuePreparedOrderRemoval(tx, MessagingValidationQueueOrderingQueueOrderMutationPreparation.PrepareQueueOrderRemoval(tx, lane, policy, body, metadata));
    }
}
