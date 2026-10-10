using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static QueueCounters ReleaseQueueInFlight(QueueCounters counters, long bytes)
    {
        if (bytes <= QueueLifecycleProtocol.Initial || counters.InFlightMessages <= QueueLifecycleProtocol.Initial || counters.InFlightBytes < bytes)
        { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidCounter); }
        return counters with
        {
            InFlightMessages = counters.InFlightMessages - QueueLifecycleProtocol.Increment,
            InFlightBytes = counters.InFlightBytes - bytes
        };
    }

    private static QueueCounters ReleaseQueueDeadLetter(QueueCounters counters, long bytes)
    {
        if (bytes <= QueueLifecycleProtocol.Initial || counters.DeadLetterMessages <= QueueLifecycleProtocol.Initial || counters.DeadLetterBytes < bytes)
        { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidCounter); }
        return counters with
        {
            DeadLetterMessages = counters.DeadLetterMessages - QueueLifecycleProtocol.Increment,
            DeadLetterBytes = counters.DeadLetterBytes - bytes
        };
    }

    private static QueueCounters ReleaseQueueStored(QueueCounters counters, long bytes)
    {
        if (bytes <= QueueLifecycleProtocol.Initial || counters.StoredMessages <= QueueLifecycleProtocol.Initial || counters.StoredBytes < bytes)
        { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidCounter); }
        return counters with
        {
            StoredMessages = counters.StoredMessages - QueueLifecycleProtocol.Increment,
            StoredBytes = counters.StoredBytes - bytes
        };
    }
}
