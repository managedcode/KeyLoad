using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private QueueDueTransitionPlan PrepareQueueDueTransition(IKeyValueView view, QueueLaneRef lane,
        QueuePolicy policy, DateTimeOffset now, MessageMetadata metadata, MessageBody body, long bodyBytes,
        QueueCounters counters, ReplicatedOperation operation, ReadOnlySpan<byte> dueKey)
    {
        if (metadata.ExpiresAt <= now)
        {
            var remaining = ReleaseQueueStored(counters, bodyBytes);
            var order = PrepareQueueOrderRemoval(view, lane, policy, body, metadata);
            RequireQueueDueStateCapacity(metadata);
            return new(metadata with
            {
                State = MessageState.Expired,
                ActiveOrderSequence = QueueOrderProtocol.Initial,
                StateVersion = checked(metadata.StateVersion + QueueSweepTransitionsVersionOne),
                LeaseOwner = null,
                LeaseUntil = null
            }, remaining, order, null, true, null);
        }
        if (metadata.Attempts >= policy.MaxAttempts)
        {
            var admission = PrepareQueueDeadLetterAdmission(view, lane, policy, metadata, bodyBytes, counters);
            return new(admission.Metadata, admission.Counters, null, null, false, admission);
        }
        if (metadata.State == MessageState.Leased && policy.RetryJitter == QueueRetryJitter.Full)
        {
            var available = QueueRetryAt(operation, lane, policy, metadata, dueKey);
            RequireQueueDueStateCapacity(metadata);
            return new(metadata with
            {
                State = MessageState.Scheduled,
                NotBefore = available,
                StateVersion = checked(metadata.StateVersion + QueueSweepTransitionsVersionOne),
                LeaseOwner = null,
                LeaseUntil = null,
                SafeFailureCode = RetryRequested
            }, counters, null, QueueKey(ScheduledQueueSpace, lane, available, metadata.Id), false, null);
        }
        RequireQueueDueStateCapacity(metadata);
        if (counters.NextReadySequence == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacityDetail);
        }
        var sequence = checked(counters.NextReadySequence + QueueSweepTransitionsAdjacentElementOffset);
        return new(metadata with
        {
            State = MessageState.Ready,
            ReadySequence = sequence,
            StateVersion = checked(metadata.StateVersion + QueueSweepTransitionsVersionOne),
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = null
        }, counters with
        {
            NextReadySequence = sequence
        }, null, QueueKey(ReadyQueueSpace, lane, sequence, metadata.Id), false, null);
    }

    private static void RequireQueueDueStateCapacity(MessageMetadata metadata)
    {
        if (metadata.StateVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacityDetail);
        }
    }
}
