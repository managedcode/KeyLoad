using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueueLeases operations against the caller's database context.</summary>
public static class QueueLeases
{
    internal const int MinimumCurrentLeaseAttempts = 1;
    internal const int MinimumCurrentLeaseStateVersion = 2;
    internal const long MinimumCurrentLeaseReadySequence = 1;
    internal const string InvalidCurrentLeaseMetadata = "The retained queue lease metadata is invalid.";
    internal static void RequireQueueLeaseAuthority(IKeyValueView view, QueueLaneRef lane, string messageId, MessageMetadata metadata, MessageBody body)
    {
        if (metadata.Id != messageId || body.Id != messageId)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueSweepTransitions.MissingLeasedBody);
        }

        if (metadata.Attempts < MinimumCurrentLeaseAttempts || metadata.StateVersion < MinimumCurrentLeaseStateVersion || metadata.ReadySequence < MinimumCurrentLeaseReadySequence)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCurrentLeaseMetadata);
        }

        if (metadata.LeaseUntil is not { } deadline || view.GetRecord<string>(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.LeasedQueueSpace, lane, deadline, messageId)) != messageId)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }
    }
}
