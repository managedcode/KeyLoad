using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueueStoredCapacityAuthority operations against the caller's database context.</summary>
public static class QueueStoredCapacityAuthority
{
    internal static void RequireQueueStoredCapacityAuthority(IKeyValueView view, QueueLaneRef lane, QueueCounters counters)
    {
        var empty = counters.StoredMessages == QueueLifecycleProtocol.Initial;
        var witness = view.VisitRange(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane), QueueLifecycleProtocol.Increment, static (_, _) => false);
        if (empty != (counters.StoredBytes == QueueLifecycleProtocol.Initial) || (witness.Records == QueueLifecycleProtocol.Initial) != empty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter);
        }
    }
}
