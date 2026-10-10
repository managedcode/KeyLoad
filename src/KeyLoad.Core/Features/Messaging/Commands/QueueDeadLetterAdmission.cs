using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static bool FitsQueueDeadLetter(QueuePolicy policy, QueueCounters counters, long bodyBytes)
    {
        var messages = policy.MaxDeadLetterMessages ?? policy.MaxStoredMessages;
        var bytes = policy.MaxDeadLetterBytes ?? policy.MaxStoredBytes;
        if (bodyBytes <= QueueLifecycleProtocol.Initial || counters.DeadLetterMessages < QueueLifecycleProtocol.Initial
            || counters.DeadLetterBytes < QueueLifecycleProtocol.Initial || counters.NextParkedSequence < QueueLifecycleProtocol.Initial)
        { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidCounter); }
        return counters.DeadLetterMessages < messages && counters.DeadLetterBytes <= bytes
            && bodyBytes <= bytes - counters.DeadLetterBytes;
    }

    private static MessageMetadata AdmitQueueDeadLetter(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy,
        MessageMetadata metadata, long bodyBytes, ref QueueCounters counters)
    {
        var updated = metadata with
        {
            StateVersion = checked(metadata.StateVersion + QueueLifecycleProtocol.Increment),
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = null,
            SafeFailureCode = AttemptsExhausted
        };
        if (!FitsQueueDeadLetter(policy, counters, bodyBytes))
        {
            tx.PutRecord(QueueKey(QueueLifecycleProtocol.PendingSpace, lane, metadata.Id),
                new QueuePendingDeadLetterReference(metadata.Id, metadata.DeliveryGeneration, AttemptsExhausted));
            return updated with { State = MessageState.PendingDeadLetter, ParkedSequence = QueueLifecycleProtocol.Initial };
        }
        var sequence = checked(counters.NextParkedSequence + QueueLifecycleProtocol.Increment);
        var admitted = counters with
        {
            DeadLetterMessages = checked(counters.DeadLetterMessages + QueueLifecycleProtocol.Increment),
            DeadLetterBytes = checked(counters.DeadLetterBytes + bodyBytes),
            NextParkedSequence = sequence
        };
        tx.PutRecord(QueueKey(DeadLetterQueueSpace, lane, metadata.Id), metadata.Id);
        tx.PutRecord(QueueKey(QueueLifecycleProtocol.ParkedSpace, lane, sequence, metadata.Id),
            new QueueDeadLetterReference(metadata.Id, sequence, metadata.DeliveryGeneration, AttemptsExhausted));
        counters = admitted;
        return updated with { State = MessageState.DeadLettered, ParkedSequence = sequence };
    }
}
