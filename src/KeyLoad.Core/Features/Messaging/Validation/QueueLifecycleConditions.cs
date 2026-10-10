using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static (string Queue, string Id, long Version, long Generation) QueueLifecycleOperands(Mutation mutation)
        => mutation switch
        {
            RedriveQueueMessage redrive => (redrive.Queue, redrive.MessageId, redrive.ExpectedStateVersion, redrive.ExpectedDeliveryGeneration),
            CancelQueueMessage cancel => (cancel.Queue, cancel.MessageId, cancel.ExpectedStateVersion, cancel.ExpectedDeliveryGeneration),
            ParkPendingQueueMessage park => (park.Queue, park.MessageId, park.ExpectedStateVersion, park.ExpectedDeliveryGeneration),
            _ => throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.InvalidShape)
        };

    private static void ValidateQueueLifecycleShape(Mutation mutation)
    {
        var (_, id, version, generation) = QueueLifecycleOperands(mutation);
        JsonData.Identifier(id);
        if (version <= QueueLifecycleProtocol.Initial || generation <= QueueLifecycleProtocol.Initial)
        { throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.InvalidShape); }
    }

    private static (MessageMetadata Metadata, StoredMessageBody Body) RequireQueueLifecycleState(IKeyValueView view,
        QueueLaneRef lane, Mutation mutation)
    {
        var (_, id, version, generation) = QueueLifecycleOperands(mutation);
        var metadata = view.GetRecord<MessageMetadata>(QueueKey(MessageMetadataSpace, lane, id))
            ?? throw Errors.Fail(ErrorCode.NotFound, QueueLifecycleProtocol.MissingMetadata);
        if (metadata.StateVersion != version || metadata.DeliveryGeneration != generation)
        { throw Errors.Fail(ErrorCode.RevisionConflict, QueueLifecycleProtocol.StaleCondition); }
        if (metadata.State is MessageState.Acked or MessageState.Cancelled or MessageState.Expired)
        { throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState); }
        var body = StoredMessageBody.Read(view, QueueKey(MessageBodySpace, lane, id))
            ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingBody);
        if (body.Body.Id != metadata.Id)
        { throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingBody); }
        if (metadata.State == MessageState.DeadLettered)
        { RequireQueueParkedAuthority(view, lane, metadata); }
        if (metadata.State == MessageState.PendingDeadLetter)
        { RequireQueuePendingAuthority(view, lane, metadata); }
        return (metadata, body);
    }
}
