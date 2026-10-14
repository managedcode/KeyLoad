using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireQueueLeaseAuthority(IKeyValueView view, QueueLaneRef lane,
        string messageId, MessageMetadata metadata, MessageBody body)
    {
        if (metadata.Id != messageId || body.Id != messageId)
        {
            throw Errors.Fail(ErrorCode.Corruption, MissingLeasedBody);
        }
        if (metadata.LeaseUntil is not { } deadline
            || view.GetRecord<string>(QueueKey(LeasedQueueSpace, lane, deadline, messageId)) != messageId)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }
    }
}
