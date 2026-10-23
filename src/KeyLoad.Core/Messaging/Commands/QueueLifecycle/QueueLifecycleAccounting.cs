using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;
/// <summary>Executes QueueLifecycleAccounting operations against the caller's database context.</summary>
public static class QueueLifecycleAccounting
{
    internal static QueueCounters ReleaseQueueInFlight(QueueCounters counters, long bytes)
    {
        if (bytes <= QueueLifecycleProtocol.Initial || counters.InFlightMessages <= QueueLifecycleProtocol.Initial || counters.InFlightBytes < bytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidCounter);
        }

        return counters with
        {
            InFlightMessages = counters.InFlightMessages - QueueLifecycleProtocol.Increment,
            InFlightBytes = counters.InFlightBytes - bytes
        };
    }

    internal static QueueCounters ReleaseQueueDeadLetter(QueueCounters counters, long bytes)
    {
        if (bytes <= QueueLifecycleProtocol.Initial || counters.DeadLetterMessages <= QueueLifecycleProtocol.Initial || counters.DeadLetterBytes < bytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidCounter);
        }

        return counters with
        {
            DeadLetterMessages = counters.DeadLetterMessages - QueueLifecycleProtocol.Increment,
            DeadLetterBytes = counters.DeadLetterBytes - bytes
        };
    }

    internal static QueueCounters ReleaseQueueStored(QueueCounters counters, long bytes)
    {
        if (bytes <= QueueLifecycleProtocol.Initial || counters.StoredMessages <= QueueLifecycleProtocol.Initial || counters.StoredBytes < bytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidCounter);
        }

        return counters with
        {
            StoredMessages = counters.StoredMessages - QueueLifecycleProtocol.Increment,
            StoredBytes = counters.StoredBytes - bytes
        };
    }
}
