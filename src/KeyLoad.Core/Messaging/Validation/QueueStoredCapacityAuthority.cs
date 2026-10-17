using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireQueueStoredCapacityAuthority(IKeyValueView view, QueueLaneRef lane, QueueCounters counters)
    {
        var empty = counters.StoredMessages == QueueLifecycleProtocol.Initial;
        var witness = view.VisitRange(QueueKey(MessageBodySpace, lane), QueueLifecycleProtocol.Increment, static (_, _) => false);
        if (empty != (counters.StoredBytes == QueueLifecycleProtocol.Initial)
            || (witness.Records == QueueLifecycleProtocol.Initial) != empty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter);
        }
    }
}
