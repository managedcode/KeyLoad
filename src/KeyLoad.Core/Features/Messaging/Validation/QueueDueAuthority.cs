using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireCurrentQueueDueEntry(QueueLaneRef lane, MessageMetadata metadata,
        DateTimeOffset now, bool leased, ReadOnlySpan<byte> actualKey)
    {
        var deadline = leased ? metadata.LeaseUntil : metadata.NotBefore;
        var expectedState = leased ? MessageState.Leased : MessageState.Scheduled;
        if (metadata.State != expectedState || deadline is null || deadline > now
            || !actualKey.SequenceEqual(QueueKey(leased ? LeasedQueueSpace : ScheduledQueueSpace,
                lane, deadline.Value, metadata.Id)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueRetryProtocol.StaleDecision); }
    }
}
