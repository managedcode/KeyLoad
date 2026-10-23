using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes QueueDueTransitionPreparation operations against the caller's database context.</summary>
public static class QueueDueTransitionPreparation
{
    internal static QueueDueTransitionPlan PrepareQueueDueTransition(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now, MessageMetadata metadata, MessageBody body, long bodyBytes, QueueCounters counters, Command<DatabaseCommand> operation, ReadOnlySpan<byte> dueKey)
    {
        if (metadata.ExpiresAt <= now)
        {
            var remaining = QueueLifecycleAccounting.ReleaseQueueStored(counters, bodyBytes);
            var order = QueueOrderMutationPreparation.PrepareQueueOrderRemoval(view, lane, policy, body, metadata);
            RequireQueueDueStateCapacity(metadata);
            return new(metadata with { State = MessageState.Expired, ActiveOrderSequence = QueueOrderProtocol.Initial, StateVersion = checked(metadata.StateVersion + QueueSweepTransitions.QueueSweepTransitionsVersionOne), LeaseOwner = null, LeaseUntil = null }, remaining, order, null, true, null);
        }

        if (metadata.Attempts >= policy.MaxAttempts)
        {
            var admission = QueueDeadLetters.PrepareQueueDeadLetterAdmission(view, lane, policy, metadata, bodyBytes, counters);
            return new(admission.Metadata, admission.Counters, null, null, false, admission);
        }

        if (metadata.State == MessageState.Leased && policy.RetryJitter == QueueRetryJitter.Full)
        {
            var available = engine.QueueRetryAt(operation, lane, policy, metadata, dueKey);
            RequireQueueDueStateCapacity(metadata);
            return new(metadata with { State = MessageState.Scheduled, NotBefore = available, StateVersion = checked(metadata.StateVersion + QueueSweepTransitions.QueueSweepTransitionsVersionOne), LeaseOwner = null, LeaseUntil = null, SafeFailureCode = QueueSweepTransitions.RetryRequested }, counters, null, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.ScheduledQueueSpace, lane, available, metadata.Id), false, null);
        }

        RequireQueueDueStateCapacity(metadata);
        if (counters.NextReadySequence == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacity.QueueLifecycleIncrementCapacityDetail);
        }

        var sequence = checked(counters.NextReadySequence + QueueSweepTransitions.QueueSweepTransitionsAdjacentElementOffset);
        return new(metadata with { State = MessageState.Ready, ReadySequence = sequence, StateVersion = checked(metadata.StateVersion + QueueSweepTransitions.QueueSweepTransitionsVersionOne), LeaseOwner = null, LeaseUntil = null, NotBefore = null }, counters with { NextReadySequence = sequence }, null, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.ReadyQueueSpace, lane, sequence, metadata.Id), false, null);
    }

    internal static void RequireQueueDueStateCapacity(MessageMetadata metadata)
    {
        if (metadata.StateVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacity.QueueLifecycleIncrementCapacityDetail);
        }
    }
}
