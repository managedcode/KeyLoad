using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MinimumCurrentLeaseAttempts = 1;
    private const int MinimumCurrentLeaseStateVersion = 2;
    private const long MinimumCurrentLeaseReadySequence = 1;
    private const string InvalidCurrentLeaseMetadata = "The retained queue lease metadata is invalid.";

    private static void RequireQueueLeaseAuthority(IKeyValueView view, QueueLaneRef lane,
        string messageId, MessageMetadata metadata, MessageBody body)
    {
        if (metadata.Id != messageId || body.Id != messageId)
        {
            throw Errors.Fail(ErrorCode.Corruption, MissingLeasedBody);
        }
        if (metadata.Attempts < MinimumCurrentLeaseAttempts
            || metadata.StateVersion < MinimumCurrentLeaseStateVersion
            || metadata.ReadySequence < MinimumCurrentLeaseReadySequence)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidCurrentLeaseMetadata);
        }
        if (metadata.LeaseUntil is not { } deadline
            || view.GetRecord<string>(QueueKey(LeasedQueueSpace, lane, deadline, messageId)) != messageId)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }
    }
}
