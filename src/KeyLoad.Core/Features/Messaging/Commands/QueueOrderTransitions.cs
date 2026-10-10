using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RemoveQueueOrder(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy,
        MessageBody body, MessageMetadata metadata)
    {
        if (policy.OrderingProfile != QueueOrderingProfile.StrictPerKey)
        { return; }
        if (policy.ParkedHeadPolicy == QueueParkedHeadPolicy.Continue
            && metadata.State is MessageState.PendingDeadLetter or MessageState.DeadLettered)
        {
            if (metadata.EnqueueSequence <= QueueOrderProtocol.Initial || metadata.ActiveOrderSequence != QueueOrderProtocol.Initial)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority); }
            return;
        }
        var key = RequireQueueOrderReference(tx, lane, RequireQueueOrderingKey(body), metadata);
        tx.Delete(key);
    }

    private static MessageMetadata ParkQueueOrder(IAtomicTransaction tx, QueueLaneRef lane,
        QueuePolicy policy, MessageBody body, MessageMetadata metadata)
    {
        if (policy.OrderingProfile != QueueOrderingProfile.StrictPerKey)
        { return metadata; }
        var key = RequireQueueOrderingKey(body);
        _ = RequireQueueOrderReference(tx, lane, key, metadata);
        if (policy.ParkedHeadPolicy == QueueParkedHeadPolicy.Block)
        { return metadata; }
        tx.Delete(QueueKey(QueueOrderProtocol.Space, lane, key, metadata.ActiveOrderSequence, metadata.Id));
        return metadata with { ActiveOrderSequence = QueueOrderProtocol.Initial };
    }

    private static MessageMetadata RedriveQueueOrder(IAtomicTransaction tx, QueueLaneRef lane,
        QueuePolicy policy, MessageBody body, MessageMetadata previous, MessageMetadata updated, ref QueueCounters counters)
    {
        if (policy.OrderingProfile != QueueOrderingProfile.StrictPerKey)
        { return updated; }
        var key = RequireQueueOrderingKey(body);
        if (policy.ParkedHeadPolicy == QueueParkedHeadPolicy.Block)
        {
            _ = RequireQueueOrderReference(tx, lane, key, previous);
            if (counters.NextOrderSequence < previous.ActiveOrderSequence)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority); }
            PutQueueOrder(tx, lane, key, updated);
            return updated;
        }
        if (previous.EnqueueSequence <= QueueOrderProtocol.Initial || previous.ActiveOrderSequence != QueueOrderProtocol.Initial
            || counters.NextOrderSequence < previous.EnqueueSequence)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority); }
        var sequence = checked(counters.NextOrderSequence + QueueOrderProtocol.Step);
        var ordered = updated with { ActiveOrderSequence = sequence };
        PutQueueOrder(tx, lane, key, ordered);
        counters = counters with { NextOrderSequence = sequence };
        return ordered;
    }
}
