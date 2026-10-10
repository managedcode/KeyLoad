using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void DeleteQueueStringReference(IAtomicTransaction tx, byte[] key, string id)
    {
        if (tx.GetRecord<string>(key) != id)
        { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference); }
        tx.Delete(key);
    }

    private static QueueCounters RemoveQueueLifecycleReferences(IAtomicTransaction tx, QueueLaneRef lane,
        MessageMetadata metadata, QueueCounters counters, long bodyBytes)
    {
        switch (metadata.State)
        {
            case MessageState.Ready:
                DeleteQueueStringReference(tx, QueueKey(ReadyQueueSpace, lane, metadata.ReadySequence, metadata.Id), metadata.Id);
                break;
            case MessageState.Scheduled:
                DeleteQueueStringReference(tx, QueueKey(ScheduledQueueSpace, lane, metadata.NotBefore
                    ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference), metadata.Id), metadata.Id);
                break;
            case MessageState.Leased:
                DeleteQueueStringReference(tx, QueueKey(LeasedQueueSpace, lane, metadata.LeaseUntil
                    ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference), metadata.Id), metadata.Id);
                counters = ReleaseQueueInFlight(counters, bodyBytes);
                break;
            case MessageState.PendingDeadLetter:
                DeleteQueuePendingReference(tx, lane, metadata);
                break;
            case MessageState.DeadLettered:
                DeleteQueueParkedReference(tx, lane, metadata);
                counters = ReleaseQueueDeadLetter(counters, bodyBytes);
                break;
            default:
                throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState);
        }
        return counters;
    }

    private static void DeleteQueuePendingReference(IAtomicTransaction tx, QueueLaneRef lane, MessageMetadata metadata)
    {
        var key = QueueKey(QueueLifecycleProtocol.PendingSpace, lane, metadata.Id);
        var expected = new QueuePendingDeadLetterReference(metadata.Id, metadata.DeliveryGeneration, AttemptsExhausted);
        if (tx.GetRecord<QueuePendingDeadLetterReference>(key) != expected)
        { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference); }
        tx.Delete(key);
    }

    private static void DeleteQueueParkedReference(IAtomicTransaction tx, QueueLaneRef lane, MessageMetadata metadata)
    {
        var key = QueueKey(QueueLifecycleProtocol.ParkedSpace, lane, metadata.ParkedSequence, metadata.Id);
        var expected = new QueueDeadLetterReference(metadata.Id, metadata.ParkedSequence, metadata.DeliveryGeneration, AttemptsExhausted);
        if (metadata.ParkedSequence <= QueueLifecycleProtocol.Initial || tx.GetRecord<QueueDeadLetterReference>(key) != expected)
        { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference); }
        DeleteQueueStringReference(tx, QueueKey(DeadLetterQueueSpace, lane, metadata.Id), metadata.Id);
        tx.Delete(key);
    }
}
