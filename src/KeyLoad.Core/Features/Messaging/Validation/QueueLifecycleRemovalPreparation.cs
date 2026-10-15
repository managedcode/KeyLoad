using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static QueueLifecycleRemovalPlan PrepareQueueLifecycleRemoval(IKeyValueView view, QueueLaneRef lane,
        MessageMetadata metadata, QueueCounters counters, long bodyBytes)
    {
        switch (metadata.State)
        {
            case MessageState.Ready:
                return PrepareQueueStringRemoval(view,
                    QueueKey(ReadyQueueSpace, lane, metadata.ReadySequence, metadata.Id), metadata.Id, counters);
            case MessageState.Scheduled:
                return PrepareQueueStringRemoval(view, QueueKey(ScheduledQueueSpace, lane, metadata.NotBefore
                    ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference), metadata.Id),
                    metadata.Id, counters);
            case MessageState.Leased:
                var leased = PrepareQueueStringRemoval(view, QueueKey(LeasedQueueSpace, lane, metadata.LeaseUntil
                    ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference), metadata.Id),
                    metadata.Id, counters);
                return leased with
                {
                    Counters = ReleaseQueueInFlight(counters, bodyBytes)
                };
            case MessageState.PendingDeadLetter:
                return PrepareQueuePendingRemoval(view, lane, metadata, counters);
            case MessageState.DeadLettered:
                return PrepareQueueParkedRemoval(view, lane, metadata, counters, bodyBytes);
            default:
                throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState);
        }
    }

    private static QueueLifecycleRemovalPlan PrepareQueueStringRemoval(IKeyValueView view, byte[] key,
        string id, QueueCounters counters)
    {
        if (view.GetRecord<string>(key) != id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }
        return new(key, null, counters);
    }

    private static QueueLifecycleRemovalPlan PrepareQueuePendingRemoval(IKeyValueView view, QueueLaneRef lane,
        MessageMetadata metadata, QueueCounters counters)
    {
        var key = QueueKey(QueueLifecycleProtocol.PendingSpace, lane, metadata.Id);
        var expected = new QueuePendingDeadLetterReference(metadata.Id, metadata.DeliveryGeneration, AttemptsExhausted);
        if (view.GetRecord<QueuePendingDeadLetterReference>(key) != expected)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }
        return new(key, null, counters);
    }

    private static QueueLifecycleRemovalPlan PrepareQueueParkedRemoval(IKeyValueView view, QueueLaneRef lane,
        MessageMetadata metadata, QueueCounters counters, long bodyBytes)
    {
        var parked = QueueKey(QueueLifecycleProtocol.ParkedSpace, lane, metadata.ParkedSequence, metadata.Id);
        var expected = new QueueDeadLetterReference(metadata.Id, metadata.ParkedSequence,
            metadata.DeliveryGeneration, AttemptsExhausted);
        if (metadata.ParkedSequence <= QueueLifecycleProtocol.Initial
            || view.GetRecord<QueueDeadLetterReference>(parked) != expected)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }
        var primary = PrepareQueueStringRemoval(view, QueueKey(DeadLetterQueueSpace, lane, metadata.Id),
            metadata.Id, counters);
        return primary with
        {
            SecondaryKey = parked,
            Counters = ReleaseQueueDeadLetter(counters, bodyBytes)
        };
    }
}
