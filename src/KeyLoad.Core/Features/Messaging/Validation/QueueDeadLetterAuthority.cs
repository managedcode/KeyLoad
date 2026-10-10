using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static QueueCounters RequireQueueLifecycleCounters(IKeyValueView view, QueueLaneRef lane)
    {
        var counters = view.GetRecord<QueueCounters>(QueueKey(QueueCountersSpace, lane))
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter);
        RequireQueueDeadLetterAuthority(view, lane, counters);
        return counters;
    }

    private static void RequireQueueDeadLetterAuthority(IKeyValueView view, QueueLaneRef lane, QueueCounters counters)
    {
        if (counters.StoredMessages < QueueLifecycleProtocol.Initial || counters.StoredBytes < QueueLifecycleProtocol.Initial
            || counters.DeadLetterMessages < QueueLifecycleProtocol.Initial || counters.DeadLetterBytes < QueueLifecycleProtocol.Initial
            || counters.NextParkedSequence < QueueLifecycleProtocol.Initial || counters.DeadLetterMessages > counters.StoredMessages
            || counters.DeadLetterBytes > counters.StoredBytes
            || (counters.DeadLetterMessages == QueueLifecycleProtocol.Initial) != (counters.DeadLetterBytes == QueueLifecycleProtocol.Initial)
            || counters.DeadLetterMessages > QueueLifecycleProtocol.Initial && counters.NextParkedSequence <= QueueLifecycleProtocol.Initial)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter); }
        // This witness detects pre-admission records; it never reconstructs or resets authoritative usage.
        var existing = view.Scan(QueueKey(DeadLetterQueueSpace, lane), QueueLifecycleProtocol.Increment);
        if (existing.Records.IsEmpty != (counters.DeadLetterMessages == QueueLifecycleProtocol.Initial))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter); }
        foreach (var record in existing.Records)
        {
            var id = view.GetRecord<string>(record.Key.ToArray())
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingReference);
            var metadata = view.GetRecord<MessageMetadata>(QueueKey(MessageMetadataSpace, lane, id))
                ?? throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingMetadata);
            RequireQueueParkedAuthority(view, lane, metadata);
        }
    }

    private static void RequireQueueParkedAuthority(IKeyValueView view, QueueLaneRef lane, MessageMetadata metadata)
    {
        var expected = new QueueDeadLetterReference(metadata.Id, metadata.ParkedSequence,
            metadata.DeliveryGeneration, AttemptsExhausted);
        if (metadata.State != MessageState.DeadLettered || metadata.ParkedSequence <= QueueLifecycleProtocol.Initial
            || metadata.SafeFailureCode != AttemptsExhausted
            || view.GetRecord<string>(QueueKey(DeadLetterQueueSpace, lane, metadata.Id)) != metadata.Id
            || view.GetRecord<QueueDeadLetterReference>(QueueKey(QueueLifecycleProtocol.ParkedSpace, lane,
                metadata.ParkedSequence, metadata.Id)) != expected)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingReference); }
    }

    private static void RequireQueuePendingAuthority(IKeyValueView view, QueueLaneRef lane, MessageMetadata metadata)
    {
        var expected = new QueuePendingDeadLetterReference(metadata.Id, metadata.DeliveryGeneration, AttemptsExhausted);
        if (metadata.ParkedSequence != QueueLifecycleProtocol.Initial || metadata.SafeFailureCode != AttemptsExhausted
            || view.GetRecord<QueuePendingDeadLetterReference>(QueueKey(QueueLifecycleProtocol.PendingSpace, lane, metadata.Id)) != expected)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingReference); }
    }
}
