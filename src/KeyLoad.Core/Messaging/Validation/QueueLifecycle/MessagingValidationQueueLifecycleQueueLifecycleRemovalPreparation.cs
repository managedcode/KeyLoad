using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationQueueLifecycleQueueLifecycleRemovalPreparation operations against the caller's database context.</summary>
public static class MessagingValidationQueueLifecycleQueueLifecycleRemovalPreparation
{
    internal static QueueLifecycleRemovalPlan PrepareQueueLifecycleRemoval(IKeyValueView view, QueueLaneRef lane, MessageMetadata metadata, QueueCounters counters, long bodyBytes)
    {
        switch (metadata.State)
        {
            case MessageState.Ready:
                return PrepareQueueStringRemoval(view, MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.ReadyQueueSpace, lane, metadata.ReadySequence, metadata.Id), metadata.Id, counters);
            case MessageState.Scheduled:
                return PrepareQueueStringRemoval(view, MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.ScheduledQueueSpace, lane, metadata.NotBefore ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference), metadata.Id), metadata.Id, counters);
            case MessageState.Leased:
                var leased = PrepareQueueStringRemoval(view, MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.LeasedQueueSpace, lane, metadata.LeaseUntil ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference), metadata.Id), metadata.Id, counters);
                return leased with
                {
                    Counters = MessagingCommandsQueueLifecycleQueueLifecycleAccounting.ReleaseQueueInFlight(counters, bodyBytes)
                };
            case MessageState.PendingDeadLetter:
                return PrepareQueuePendingRemoval(view, lane, metadata, counters);
            case MessageState.DeadLettered:
                return PrepareQueueParkedRemoval(view, lane, metadata, counters, bodyBytes);
            default:
                throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState);
        }
    }

    internal static QueueLifecycleRemovalPlan PrepareQueueStringRemoval(IKeyValueView view, byte[] key, string id, QueueCounters counters)
    {
        if (view.GetRecord<string>(key) != id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }

        return new(key, null, counters);
    }

    internal static QueueLifecycleRemovalPlan PrepareQueuePendingRemoval(IKeyValueView view, QueueLaneRef lane, MessageMetadata metadata, QueueCounters counters)
    {
        var key = MessagingExecutionMessaging.QueueKey(QueueLifecycleProtocol.PendingSpace, lane, metadata.Id);
        var expected = new QueuePendingDeadLetterReference(metadata.Id, metadata.DeliveryGeneration, MessagingCommandsQueueSweepTransitions.AttemptsExhausted);
        if (view.GetRecord<QueuePendingDeadLetterReference>(key) != expected)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }

        return new(key, null, counters);
    }

    internal static QueueLifecycleRemovalPlan PrepareQueueParkedRemoval(IKeyValueView view, QueueLaneRef lane, MessageMetadata metadata, QueueCounters counters, long bodyBytes)
    {
        var parked = MessagingExecutionMessaging.QueueKey(QueueLifecycleProtocol.ParkedSpace, lane, metadata.ParkedSequence, metadata.Id);
        var expected = new QueueDeadLetterReference(metadata.Id, metadata.ParkedSequence, metadata.DeliveryGeneration, MessagingCommandsQueueSweepTransitions.AttemptsExhausted);
        if (metadata.ParkedSequence <= QueueLifecycleProtocol.Initial || view.GetRecord<QueueDeadLetterReference>(parked) != expected)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }

        var primary = PrepareQueueStringRemoval(view, MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.DeadLetterQueueSpace, lane, metadata.Id), metadata.Id, counters);
        return primary with
        {
            SecondaryKey = parked,
            Counters = MessagingCommandsQueueLifecycleQueueLifecycleAccounting.ReleaseQueueDeadLetter(counters, bodyBytes)
        };
    }
}
