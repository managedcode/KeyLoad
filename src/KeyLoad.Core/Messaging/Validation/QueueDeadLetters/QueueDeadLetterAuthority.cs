using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueueDeadLetterAuthority operations against the caller's database context.</summary>
public static class QueueDeadLetterAuthority
{
    internal static QueueCounters RequireQueueLifecycleCounters(IKeyValueView view, QueueLaneRef lane)
    {
        var counters = view.GetRecord<QueueCounters>(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.QueueCountersSpace, lane)) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter);
        RequireQueueDeadLetterAuthority(view, lane, counters);
        return counters;
    }

    internal static void RequireQueueDeadLetterAuthority(IKeyValueView view, QueueLaneRef lane, QueueCounters counters)
    {
        if (counters.StoredMessages < QueueLifecycleProtocol.Initial || counters.StoredBytes < QueueLifecycleProtocol.Initial || counters.InFlightMessages < QueueLifecycleProtocol.Initial || counters.InFlightBytes < QueueLifecycleProtocol.Initial || counters.InFlightMessages > counters.StoredMessages || counters.InFlightBytes > counters.StoredBytes || (counters.InFlightMessages == QueueLifecycleProtocol.Initial) != (counters.InFlightBytes == QueueLifecycleProtocol.Initial) || counters.NextReadySequence < QueueLifecycleProtocol.Initial || counters.NextOrderSequence < QueueLifecycleProtocol.Initial || counters.DeadLetterMessages < QueueLifecycleProtocol.Initial || counters.DeadLetterBytes < QueueLifecycleProtocol.Initial || counters.NextParkedSequence < QueueLifecycleProtocol.Initial || counters.DeadLetterMessages > counters.StoredMessages || counters.DeadLetterBytes > counters.StoredBytes || counters.InFlightMessages > counters.StoredMessages - counters.DeadLetterMessages || counters.InFlightBytes > counters.StoredBytes - counters.DeadLetterBytes || (counters.DeadLetterMessages == QueueLifecycleProtocol.Initial) != (counters.DeadLetterBytes == QueueLifecycleProtocol.Initial) || counters.DeadLetterMessages > QueueLifecycleProtocol.Initial && counters.NextParkedSequence <= QueueLifecycleProtocol.Initial)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter);
        }

        QueueStoredCapacityAuthority.RequireQueueStoredCapacityAuthority(view, lane, counters);
        // This witness detects pre-admission records; it never reconstructs or resets authoritative usage.
        var existing = view.Scan(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.DeadLetterQueueSpace, lane), QueueLifecycleProtocol.Increment);
        if (existing.Records.IsEmpty != (counters.DeadLetterMessages == QueueLifecycleProtocol.Initial))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter);
        }

        foreach (var record in existing.Records)
        {
            var id = view.GetRecord<string>(record.Key.ToArray()) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingReference);
            if (!record.Key.Span.SequenceEqual(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.DeadLetterQueueSpace, lane, id)))
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingReference);
            }

            var metadata = view.GetRecord<MessageMetadata>(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, id)) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingMetadata);
            if (metadata.Id != id)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingMetadata);
            }

            RequireQueueParkedAuthority(view, lane, metadata);
        }
    }

    internal static void RequireQueueParkedAuthority(IKeyValueView view, QueueLaneRef lane, MessageMetadata metadata)
    {
        var expected = new QueueDeadLetterReference(metadata.Id, metadata.ParkedSequence, metadata.DeliveryGeneration, QueueSweepTransitions.AttemptsExhausted);
        if (metadata.State != MessageState.DeadLettered || metadata.ParkedSequence <= QueueLifecycleProtocol.Initial || metadata.SafeFailureCode != QueueSweepTransitions.AttemptsExhausted || view.GetRecord<string>(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.DeadLetterQueueSpace, lane, metadata.Id)) != metadata.Id || view.GetRecord<QueueDeadLetterReference>(MessagingExecutionMessaging.QueueKey(QueueLifecycleProtocol.ParkedSpace, lane, metadata.ParkedSequence, metadata.Id)) != expected)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingReference);
        }
    }

    internal static void RequireQueuePendingAuthority(IKeyValueView view, QueueLaneRef lane, MessageMetadata metadata)
    {
        var expected = new QueuePendingDeadLetterReference(metadata.Id, metadata.DeliveryGeneration, QueueSweepTransitions.AttemptsExhausted);
        if (metadata.ParkedSequence != QueueLifecycleProtocol.Initial || metadata.SafeFailureCode != QueueSweepTransitions.AttemptsExhausted || view.GetRecord<QueuePendingDeadLetterReference>(MessagingExecutionMessaging.QueueKey(QueueLifecycleProtocol.PendingSpace, lane, metadata.Id)) != expected)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.MissingReference);
        }
    }
}
