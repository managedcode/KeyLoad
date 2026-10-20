using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommands operations against the caller's database context.</summary>
public static class MessagingCommands
{
    internal static bool FitsQueueDeadLetter(QueuePolicy policy, QueueCounters counters, long bodyBytes)
    {
        var messages = policy.MaxDeadLetterMessages ?? policy.MaxStoredMessages;
        var bytes = policy.MaxDeadLetterBytes ?? policy.MaxStoredBytes;
        if (bodyBytes <= QueueLifecycleProtocol.Initial || counters.DeadLetterMessages < QueueLifecycleProtocol.Initial || counters.DeadLetterBytes < QueueLifecycleProtocol.Initial || counters.NextParkedSequence < QueueLifecycleProtocol.Initial)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidCounter);
        }

        return counters.DeadLetterMessages < messages && counters.DeadLetterBytes <= bytes && bodyBytes <= bytes - counters.DeadLetterBytes;
    }

    internal static MessageMetadata AdmitQueueDeadLetter(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, MessageMetadata metadata, long bodyBytes, ref QueueCounters counters, QueueDeadLetterAdmissionPlan? prepared = null)
    {
        var admission = prepared ?? MessagingValidationQueueDeadLetters.PrepareQueueDeadLetterAdmission(tx, lane, policy, metadata, bodyBytes, counters);
        return MessagingCommandsQueueOrderingQueuePreparedOrderApply.ApplyQueueDeadLetterAdmission(tx, lane, admission, ref counters);
    }
}
