using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private MutationReceipt ApplyRedriveQueueMessage(IAtomicTransaction tx, PartitionRef partition,
        RedriveQueueMessage request, DateTimeOffset now)
    {
        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = Resource(tx, partition, lane.Queue, ResourceKind.WorkQueue);
        if (resource.Paused)
        { throw Errors.Fail(ErrorCode.DispatchPaused, QueuePaused); }
        var (metadata, body) = RequireQueueLifecycleState(tx, lane, request);
        if (metadata.State is not (MessageState.PendingDeadLetter or MessageState.DeadLettered))
        { throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState); }
        if (metadata.ExpiresAt <= now || metadata.ExpiresAt <= request.NotBefore)
        { throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.ExpiredRedrive); }
        var counters = RemoveQueueLifecycleReferences(tx, lane, metadata, RequireQueueLifecycleCounters(tx, lane), body.Bytes);
        var scheduled = request.NotBefore > now;
        var sequence = scheduled ? QueueLifecycleProtocol.Initial : checked(counters.NextReadySequence + QueueLifecycleProtocol.Increment);
        var updated = metadata with
        {
            State = scheduled ? MessageState.Scheduled : MessageState.Ready,
            StateVersion = checked(metadata.StateVersion + QueueLifecycleProtocol.Increment),
            DeliveryGeneration = checked(metadata.DeliveryGeneration + QueueLifecycleProtocol.Increment),
            LeaseVersion = checked(metadata.LeaseVersion + QueueLifecycleProtocol.Increment),
            Attempts = QueueLifecycleProtocol.Initial,
            ReadySequence = sequence,
            NotBefore = request.NotBefore,
            LeaseOwner = null,
            LeaseUntil = null,
            SafeFailureCode = null,
            ParkedSequence = QueueLifecycleProtocol.Initial
        };
        tx.PutRecord(scheduled ? QueueKey(ScheduledQueueSpace, lane, request.NotBefore!.Value, metadata.Id)
            : QueueKey(ReadyQueueSpace, lane, sequence, metadata.Id), metadata.Id);
        tx.PutRecord(QueueKey(MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(QueueKey(QueueCountersSpace, lane), scheduled ? counters : counters with { NextReadySequence = sequence });
        return new(QueueLifecycleProtocol.RedriveKind, lane.Queue, metadata.Id, updated.StateVersion);
    }

    private static MutationReceipt ApplyCancelQueueMessage(IAtomicTransaction tx, PartitionRef partition, CancelQueueMessage request)
    {
        var lane = new QueueLaneRef(partition, request.Queue);
        var (metadata, body) = RequireQueueLifecycleState(tx, lane, request);
        var counters = RemoveQueueLifecycleReferences(tx, lane, metadata, RequireQueueLifecycleCounters(tx, lane), body.Bytes);
        counters = ReleaseQueueStored(counters, body.Bytes);
        var updated = metadata with
        {
            State = MessageState.Cancelled,
            StateVersion = checked(metadata.StateVersion + QueueLifecycleProtocol.Increment),
            DeliveryGeneration = checked(metadata.DeliveryGeneration + QueueLifecycleProtocol.Increment),
            LeaseVersion = checked(metadata.LeaseVersion + QueueLifecycleProtocol.Increment),
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = null,
            ParkedSequence = QueueLifecycleProtocol.Initial
        };
        tx.Delete(QueueKey(MessageBodySpace, lane, metadata.Id));
        tx.PutRecord(QueueKey(MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(QueueKey(QueueCountersSpace, lane), counters);
        return new(QueueLifecycleProtocol.CancelKind, lane.Queue, metadata.Id, updated.StateVersion);
    }

    private MutationReceipt ApplyParkPendingQueueMessage(IAtomicTransaction tx, PartitionRef partition,
        ParkPendingQueueMessage request, DateTimeOffset now)
    {
        var lane = new QueueLaneRef(partition, request.Queue);
        var resource = Resource(tx, partition, lane.Queue, ResourceKind.WorkQueue);
        var (metadata, body) = RequireQueueLifecycleState(tx, lane, request);
        if (metadata.State != MessageState.PendingDeadLetter)
        { throw Errors.Fail(ErrorCode.Conflict, QueueLifecycleProtocol.InvalidState); }
        if (metadata.ExpiresAt <= now)
        { throw Errors.Fail(ErrorCode.Validation, QueueLifecycleProtocol.ExpiredRedrive); }
        var counters = RequireQueueLifecycleCounters(tx, lane);
        if (!FitsQueueDeadLetter(resource.QueuePolicy, counters, body.Bytes))
        { throw Errors.Fail(ErrorCode.ResourceExhausted, QueueLifecycleProtocol.FullDeadLetter); }
        DeleteQueuePendingReference(tx, lane, metadata);
        var updated = AdmitQueueDeadLetter(tx, lane, resource.QueuePolicy, metadata, body.Bytes, ref counters);
        tx.PutRecord(QueueKey(MessageMetadataSpace, lane, metadata.Id), updated);
        tx.PutRecord(QueueKey(QueueCountersSpace, lane), counters);
        return new(QueueLifecycleProtocol.ParkKind, lane.Queue, metadata.Id, updated.StateVersion);
    }
}
