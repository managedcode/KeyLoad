using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueueDeadLetters operations against the caller's database context.</summary>
public static class QueueDeadLetters
{
    internal static QueueDeadLetterAdmissionPlan PrepareQueueDeadLetterAdmission(IKeyValueView view, QueueLaneRef lane, QueuePolicy policy, MessageMetadata metadata, long bodyBytes, QueueCounters counters)
    {
        var body = (StoredMessageBody.Read(view, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, metadata.Id))).Value ?? throw Errors.Fail(ErrorCode.Corruption, QueueSweepTransitions.MissingIndexedBody);
        var order = QueueOrderMutationPreparation.PrepareQueueParkOrder(view, lane, policy, body.Body, metadata);
        var fits = MessagingCommands.FitsQueueDeadLetter(policy, counters, bodyBytes);
        if (metadata.StateVersion == long.MaxValue || fits && counters.NextParkedSequence == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacity.QueueLifecycleIncrementCapacityDetail);
        }

        var updated = metadata with
        {
            ActiveOrderSequence = order.ActiveOrderSequence,
            StateVersion = checked(metadata.StateVersion + QueueLifecycleProtocol.Increment),
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = null,
            SafeFailureCode = QueueSweepTransitions.AttemptsExhausted
        };
        if (!fits)
        {
            return new(updated with { State = MessageState.PendingDeadLetter, ParkedSequence = QueueLifecycleProtocol.Initial }, counters, order);
        }

        var sequence = checked(counters.NextParkedSequence + QueueLifecycleProtocol.Increment);
        return new(updated with { State = MessageState.DeadLettered, ParkedSequence = sequence }, counters with { DeadLetterMessages = checked(counters.DeadLetterMessages + QueueLifecycleProtocol.Increment), DeadLetterBytes = checked(counters.DeadLetterBytes + bodyBytes), NextParkedSequence = sequence }, order);
    }
}
