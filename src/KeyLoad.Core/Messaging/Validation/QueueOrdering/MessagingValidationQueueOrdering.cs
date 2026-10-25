using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingValidationQueueOrdering operations against the caller's database context.</summary>
public static class MessagingValidationQueueOrdering
{
    internal static bool RequireStrictQueueHead(IKeyValueView view, QueueLaneRef lane, QueuePolicy policy, MessageBody body, MessageMetadata metadata)
    {
        if (policy.OrderingProfile != QueueOrderingProfile.StrictPerKey)
        {
            return true;
        }

        var orderingKey = MessagingCommandsQueueOrdering.RequireQueueOrderingKey(body);
        RequireQueueOrderReference(view, lane, orderingKey, metadata);
        var prefix = MessagingExecutionMessaging.QueueKey(QueueOrderProtocol.Space, lane, orderingKey);
        var page = view.Scan(prefix, QueueOrderProtocol.Step);
        if (page.Records.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.MissingAuthority);
        }

        var first = page.Records[QueueOrderProtocol.Initial];
        var reference = view.GetRecord<QueueOrderReference>(first.Key.ToArray()) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.MissingAuthority);
        var head = view.GetRecord<MessageMetadata>(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, reference.MessageId)) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.MissingAuthority);
        var headBody = (StoredMessageBody.Read(view, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, head.Id))).Value ?? throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.MissingAuthority);
        if (headBody.Body.Id != head.Id || MessagingCommandsQueueOrdering.RequireQueueOrderingKey(headBody.Body) != orderingKey || head.State is not (MessageState.Ready or MessageState.Scheduled or MessageState.Leased or MessageState.PendingDeadLetter or MessageState.DeadLettered) || policy.ParkedHeadPolicy == QueueParkedHeadPolicy.Continue && head.State is MessageState.PendingDeadLetter or MessageState.DeadLettered)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
        }

        RequireQueueOrderReference(view, lane, orderingKey, head);
        if (MessagingExecutionMessaging.Counters(view, lane).NextOrderSequence < head.ActiveOrderSequence)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
        }

        if (reference != new QueueOrderReference(head.Id, head.ActiveOrderSequence, head.DeliveryGeneration) || !first.Key.Span.SequenceEqual(MessagingExecutionMessaging.QueueKey(QueueOrderProtocol.Space, lane, orderingKey, head.ActiveOrderSequence, head.Id)))
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
        }

        return head.Id == metadata.Id;
    }

    internal static byte[] RequireQueueOrderReference(IKeyValueView view, QueueLaneRef lane, string orderingKey, MessageMetadata metadata)
    {
        if (metadata.EnqueueSequence <= QueueOrderProtocol.Initial || metadata.ActiveOrderSequence < metadata.EnqueueSequence)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
        }

        var key = MessagingExecutionMessaging.QueueKey(QueueOrderProtocol.Space, lane, orderingKey, metadata.ActiveOrderSequence, metadata.Id);
        var expected = new QueueOrderReference(metadata.Id, metadata.ActiveOrderSequence, metadata.DeliveryGeneration);
        if (view.GetRecord<QueueOrderReference>(key) != expected)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
        }

        return key;
    }
}
