using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationQueueDeadlinesQueueDueTransitionPreparation operations against the caller's database context.</summary>
public static class MessagingValidationQueueDeadlinesQueueDueTransitionPreparation
{
    internal static QueueDueTransitionPlan PrepareQueueDueTransition(this DatabaseEngine engine, IKeyValueView view, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now, MessageMetadata metadata, MessageBody body, long bodyBytes, QueueCounters counters, Command<DatabaseCommand> operation, ReadOnlySpan<byte> dueKey)
    {
        if (metadata.ExpiresAt <= now)
        {
            var remaining = MessagingCommandsQueueLifecycleQueueLifecycleAccounting.ReleaseQueueStored(counters, bodyBytes);
            var order = MessagingValidationQueueOrderingQueueOrderMutationPreparation.PrepareQueueOrderRemoval(view, lane, policy, body, metadata);
            RequireQueueDueStateCapacity(metadata);
            return new(metadata with { State = MessageState.Expired, ActiveOrderSequence = QueueOrderProtocol.Initial, StateVersion = checked(metadata.StateVersion + MessagingCommandsQueueSweepTransitions.QueueSweepTransitionsVersionOne), LeaseOwner = null, LeaseUntil = null }, remaining, order, null, true, null);
        }

        if (metadata.Attempts >= policy.MaxAttempts)
        {
            var admission = MessagingValidationQueueDeadLetters.PrepareQueueDeadLetterAdmission(view, lane, policy, metadata, bodyBytes, counters);
            return new(admission.Metadata, admission.Counters, null, null, false, admission);
        }

        if (metadata.State == MessageState.Leased && policy.RetryJitter == QueueRetryJitter.Full)
        {
            var available = engine.QueueRetryAt(operation, lane, policy, metadata, dueKey);
            RequireQueueDueStateCapacity(metadata);
            return new(metadata with { State = MessageState.Scheduled, NotBefore = available, StateVersion = checked(metadata.StateVersion + MessagingCommandsQueueSweepTransitions.QueueSweepTransitionsVersionOne), LeaseOwner = null, LeaseUntil = null, SafeFailureCode = MessagingCommandsQueueSweepTransitions.RetryRequested }, counters, null, MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.ScheduledQueueSpace, lane, available, metadata.Id), false, null);
        }

        RequireQueueDueStateCapacity(metadata);
        if (counters.NextReadySequence == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, MessagingValidationQueueLifecycleQueueLifecycleIncrementCapacity.QueueLifecycleIncrementCapacityDetail);
        }

        var sequence = checked(counters.NextReadySequence + MessagingCommandsQueueSweepTransitions.QueueSweepTransitionsAdjacentElementOffset);
        return new(metadata with { State = MessageState.Ready, ReadySequence = sequence, StateVersion = checked(metadata.StateVersion + MessagingCommandsQueueSweepTransitions.QueueSweepTransitionsVersionOne), LeaseOwner = null, LeaseUntil = null, NotBefore = null }, counters with { NextReadySequence = sequence }, null, MessagingExecutionMessaging.QueueKey(MessagingCommandsQueueSweepTransitions.ReadyQueueSpace, lane, sequence, metadata.Id), false, null);
    }

    internal static void RequireQueueDueStateCapacity(MessageMetadata metadata)
    {
        if (metadata.StateVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, MessagingValidationQueueLifecycleQueueLifecycleIncrementCapacity.QueueLifecycleIncrementCapacityDetail);
        }
    }
}
