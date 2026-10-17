using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RemoveQueueOrder(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy,
        MessageBody body, MessageMetadata metadata)
    {
        ApplyQueuePreparedOrderRemoval(tx, PrepareQueueOrderRemoval(tx, lane, policy, body, metadata));
    }

}
