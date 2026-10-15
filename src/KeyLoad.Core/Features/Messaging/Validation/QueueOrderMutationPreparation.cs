using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static byte[]? PrepareQueueOrderRemoval(IKeyValueView view, QueueLaneRef lane, QueuePolicy policy,
        MessageBody body, MessageMetadata metadata)
    {
        if (policy.OrderingProfile != QueueOrderingProfile.StrictPerKey)
        {
            return null;
        }
        if (policy.ParkedHeadPolicy == QueueParkedHeadPolicy.Continue
            && metadata.State is MessageState.PendingDeadLetter or MessageState.DeadLettered)
        {
            if (metadata.EnqueueSequence <= QueueOrderProtocol.Initial || metadata.ActiveOrderSequence != QueueOrderProtocol.Initial)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
            }
            return null;
        }
        return RequireQueueOrderReference(view, lane, RequireQueueOrderingKey(body), metadata);
    }

    private static QueueParkOrderPlan PrepareQueueParkOrder(IKeyValueView view, QueueLaneRef lane,
        QueuePolicy policy, MessageBody body, MessageMetadata metadata)
    {
        if (policy.OrderingProfile != QueueOrderingProfile.StrictPerKey)
        {
            return new(null, metadata.ActiveOrderSequence);
        }
        if (metadata.State == MessageState.PendingDeadLetter && policy.ParkedHeadPolicy == QueueParkedHeadPolicy.Continue)
        {
            if (metadata.EnqueueSequence <= QueueOrderProtocol.Initial || metadata.ActiveOrderSequence != QueueOrderProtocol.Initial)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
            }
            return new(null, metadata.ActiveOrderSequence);
        }
        var key = RequireQueueOrderReference(view, lane, RequireQueueOrderingKey(body), metadata);
        return policy.ParkedHeadPolicy == QueueParkedHeadPolicy.Block
            ? new(null, metadata.ActiveOrderSequence)
            : new(key, QueueOrderProtocol.Initial);
    }

    private static QueueRedriveOrderPlan PrepareQueueRedriveOrder(IKeyValueView view, QueueLaneRef lane,
        QueuePolicy policy, MessageBody body, MessageMetadata previous, QueueCounters counters)
    {
        if (policy.OrderingProfile != QueueOrderingProfile.StrictPerKey)
        {
            return new(null, previous.ActiveOrderSequence, counters.NextOrderSequence);
        }
        var key = RequireQueueOrderingKey(body);
        if (policy.ParkedHeadPolicy == QueueParkedHeadPolicy.Block)
        {
            _ = RequireQueueOrderReference(view, lane, key, previous);
            if (counters.NextOrderSequence < previous.ActiveOrderSequence)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
            }
            return new(key, previous.ActiveOrderSequence, counters.NextOrderSequence);
        }
        if (previous.EnqueueSequence <= QueueOrderProtocol.Initial || previous.ActiveOrderSequence != QueueOrderProtocol.Initial
            || counters.NextOrderSequence < previous.EnqueueSequence)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, QueueOrderProtocol.InvalidAuthority);
        }
        if (counters.NextOrderSequence == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleIncrementCapacityDetail);
        }
        var sequence = checked(counters.NextOrderSequence + QueueOrderProtocol.Step);
        return new(key, sequence, sequence);
    }
}
