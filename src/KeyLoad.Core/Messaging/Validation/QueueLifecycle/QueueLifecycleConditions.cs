using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes QueueLifecycleConditions operations against the caller's database context.</summary>
public static class QueueLifecycleConditions
{
    internal static (string Queue, string Id, long Version, long Generation) QueueLifecycleOperands(Mutation mutation) => mutation switch
    {
        RedriveQueueMessage redrive => (redrive.Queue, redrive.MessageId, redrive.ExpectedStateVersion, redrive.ExpectedDeliveryGeneration),
        CancelQueueMessage cancel => (cancel.Queue, cancel.MessageId, cancel.ExpectedStateVersion, cancel.ExpectedDeliveryGeneration),
        ParkPendingQueueMessage park => (park.Queue, park.MessageId, park.ExpectedStateVersion, park.ExpectedDeliveryGeneration),
        _ => throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.InvalidShape)
    };
    internal static void ValidateQueueLifecycleShape(Mutation mutation)
    {
        var (_, id, version, generation) = QueueLifecycleOperands(mutation);
        JsonData.Identifier(id);
        if (version <= QueueLifecycleProtocol.Initial || generation <= QueueLifecycleProtocol.Initial)
        {
            throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.InvalidShape);
        }
    }

    internal static (MessageMetadata Metadata, StoredMessageBody Body) RequireQueueLifecycleState(IKeyValueView view, QueueLaneRef lane, Mutation mutation)
    {
        var (_, id, version, generation) = QueueLifecycleOperands(mutation);
        var metadata = view.GetRecord<MessageMetadata>(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, id)) ?? throw Errors.Fail(ErrorCode.NotFound, QueueLifecycleProtocol.MissingMetadata);
        if (metadata.StateVersion != version || metadata.DeliveryGeneration != generation)
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, QueueLifecycleProtocol.StaleCondition);
        }

        if (metadata.State is MessageState.Acked or MessageState.Cancelled or MessageState.Expired)
        {
            throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState);
        }

        if (metadata.Id != id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }

        var body = (StoredMessageBody.Read(view, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, id))).Value ?? throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingBody);
        if (body.Body.Id != metadata.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingBody);
        }

        if (metadata.State == MessageState.DeadLettered)
        {
            QueueDeadLetterAuthority.RequireQueueParkedAuthority(view, lane, metadata);
        }

        if (metadata.State == MessageState.PendingDeadLetter)
        {
            QueueDeadLetterAuthority.RequireQueuePendingAuthority(view, lane, metadata);
        }

        return (metadata, body);
    }
}
