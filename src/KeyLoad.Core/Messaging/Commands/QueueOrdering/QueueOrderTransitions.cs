using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueueOrderTransitions operations against the caller's database context.</summary>
public static class QueueOrderTransitions
{
    internal static void RemoveQueueOrder(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, MessageBody body, MessageMetadata metadata)
    {
        QueuePreparedOrderApply.ApplyQueuePreparedOrderRemoval(tx, QueueOrderMutationPreparation.PrepareQueueOrderRemoval(tx, lane, policy, body, metadata));
    }
}
