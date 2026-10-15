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
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidCounter);
        }
        return counters.DeadLetterMessages < messages && counters.DeadLetterBytes <= bytes
            && bodyBytes <= bytes - counters.DeadLetterBytes;
    }

    private static MessageMetadata AdmitQueueDeadLetter(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy,
        MessageMetadata metadata, long bodyBytes, ref QueueCounters counters, QueueDeadLetterAdmissionPlan? prepared = null)
    {
        var admission = prepared ?? PrepareQueueDeadLetterAdmission(tx, lane, policy, metadata, bodyBytes, counters);
        return ApplyQueueDeadLetterAdmission(tx, lane, admission, ref counters);
    }
}
