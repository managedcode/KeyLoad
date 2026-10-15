using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static QueueDeadLetterAdmissionPlan PrepareQueueDeadLetterAdmission(IKeyValueView view, QueueLaneRef lane,
        QueuePolicy policy, MessageMetadata metadata, long bodyBytes, QueueCounters counters)
    {
        var body = StoredMessageBody.Read(view, QueueKey(MessageBodySpace, lane, metadata.Id))
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingIndexedBody);
        var order = PrepareQueueParkOrder(view, lane, policy, body.Body, metadata);
        var fits = FitsQueueDeadLetter(policy, counters, bodyBytes);
        if (metadata.StateVersion == long.MaxValue || fits && counters.NextParkedSequence == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacityDetail);
        }
        var updated = metadata with
        {
            ActiveOrderSequence = order.ActiveOrderSequence,
            StateVersion = checked(metadata.StateVersion + QueueLifecycleProtocol.Increment),
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = null,
            SafeFailureCode = AttemptsExhausted
        };
        if (!fits)
        {
            return new(updated with
            {
                State = MessageState.PendingDeadLetter,
                ParkedSequence = QueueLifecycleProtocol.Initial
            }, counters, order);
        }
        var sequence = checked(counters.NextParkedSequence + QueueLifecycleProtocol.Increment);
        return new(updated with
        {
            State = MessageState.DeadLettered,
            ParkedSequence = sequence
        }, counters with
        {
            DeadLetterMessages = checked(counters.DeadLetterMessages + QueueLifecycleProtocol.Increment),
            DeadLetterBytes = checked(counters.DeadLetterBytes + bodyBytes),
            NextParkedSequence = sequence
        }, order);
    }
}
