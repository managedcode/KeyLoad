using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationQueueStoredCapacityAuthority operations against the caller's database context.</summary>
public static class MessagingValidationQueueStoredCapacityAuthority
{
    internal static void RequireQueueStoredCapacityAuthority(IKeyValueView view, QueueLaneRef lane, QueueCounters counters)
    {
        var empty = counters.StoredMessages == QueueLifecycleProtocol.Initial;
        var witness = view.VisitRange(MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.MessageBodySpace, lane), QueueLifecycleProtocol.Increment, static (_, _) => false);
        if (empty != (counters.StoredBytes == QueueLifecycleProtocol.Initial) || (witness.Records == QueueLifecycleProtocol.Initial) != empty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter);
        }
    }
}
