using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsQueueOrdering operations against the caller's database context.</summary>
public static class MessagingCommandsQueueOrdering
{
    internal static (MessageMetadata Metadata, QueueCounters Counters) AdmitQueueOrder(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, MessageBody body, MessageMetadata metadata, QueueCounters counters)
    {
        if (policy.OrderingProfile != QueueOrderingProfile.StrictPerKey)
        {
            return (metadata, counters);
        }

        var orderingKey = RequireQueueOrderingKey(body);
        if (counters.NextOrderSequence < QueueOrderProtocol.Initial)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
        }

        var sequence = checked(counters.NextOrderSequence + QueueOrderProtocol.Step);
        var ordered = metadata with
        {
            EnqueueSequence = sequence,
            ActiveOrderSequence = sequence
        };
        PutQueueOrder(tx, lane, orderingKey, ordered);
        return (ordered, counters with { NextOrderSequence = sequence });
    }

    internal static string RequireQueueOrderingKey(MessageBody body)
    {
        if (string.IsNullOrEmpty(body.OrderingKey))
        {
            throw Errors.Fail(ErrorCode.Validation, QueueOrderProtocol.MissingKey);
        }

        JsonData.Identifier(body.OrderingKey);
        return body.OrderingKey;
    }

    internal static void PutQueueOrder(IAtomicTransaction tx, QueueLaneRef lane, string key, MessageMetadata metadata) => tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueOrderProtocol.Space, lane, key, metadata.ActiveOrderSequence, metadata.Id), new QueueOrderReference(metadata.Id, metadata.ActiveOrderSequence, metadata.DeliveryGeneration));
}
