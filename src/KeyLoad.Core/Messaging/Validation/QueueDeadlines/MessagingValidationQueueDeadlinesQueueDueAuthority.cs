using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationQueueDeadlinesQueueDueAuthority operations against the caller's database context.</summary>
public static class MessagingValidationQueueDeadlinesQueueDueAuthority
{
    internal static void RequireCurrentQueueDueEntry(QueueLaneRef lane, MessageMetadata metadata, DateTimeOffset now, bool leased, ReadOnlySpan<byte> actualKey)
    {
        var deadline = leased ? metadata.LeaseUntil : metadata.NotBefore;
        var expectedState = leased ? MessageState.Leased : MessageState.Scheduled;
        if (metadata.State != expectedState || deadline is null || deadline > now || !actualKey.SequenceEqual(MessagingExecutionMessaging.QueueKey(leased ? MessagingCommandsQueueSweepTransitions.LeasedQueueSpace : MessagingCommandsQueueSweepTransitions.ScheduledQueueSpace, lane, deadline.Value, metadata.Id)))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueRetryProtocol.StaleDecision);
        }
    }
}
