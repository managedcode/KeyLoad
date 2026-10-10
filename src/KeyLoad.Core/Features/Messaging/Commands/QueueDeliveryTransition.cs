using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int QueueDeliveryTransitionMinimumPositiveCount = 1;
    private const int QueueDeliveryTransitionVersionOne = 1;
    private const int QueueDeliveryTransitionAdjacentElementOffset = 1;
    private const int QueueDeliveryTransitionRetryExponentialBase = 2;

    private const int DefaultRenewalSeconds = 30;

    private CommitReceipt ApplyDeliveryTransition(IAtomicTransaction tx, DeliveryCommand command,
        ResourceDefinition resource, ValidatedQueueLease lease, DateTimeOffset now, long position)
    {
        var metadata = lease.Metadata;
        var counters = Counters(tx, command.Lane);
        tx.Delete(QueueKey(LeasedQueueSpace, command.Lane, metadata.LeaseUntil!.Value, metadata.Id));
        MessageMetadata updated;
        if (command.Action == DeliveryAction.Renew)
        {
            var seconds = command.LeaseSeconds ?? DefaultRenewalSeconds;
            if (seconds < QueueDeliveryTransitionMinimumPositiveCount || seconds > resource.QueuePolicy.MaxLeaseSeconds)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidRenewal);
            }
            var deadline = now.AddSeconds(seconds);
            updated = metadata with { LeaseUntil = deadline, StateVersion = metadata.StateVersion + QueueDeliveryTransitionVersionOne };
            tx.PutRecord(QueueKey(LeasedQueueSpace, command.Lane, deadline, metadata.Id), metadata.Id);
        }
        else
        {
            counters = counters with
            {
                InFlightMessages = counters.InFlightMessages - QueueDeliveryTransitionAdjacentElementOffset,
                InFlightBytes = counters.InFlightBytes - lease.BodyBytes
            };
            updated = CompleteDeliveryAction(tx, command.Lane, resource.QueuePolicy, command.Action,
                metadata, lease.BodyBytes, now, ref counters);
        }
        tx.PutRecord(QueueKey(MessageMetadataSpace, command.Lane, metadata.Id), updated);
        tx.PutRecord(QueueKey(QueueCountersSpace, command.Lane), counters);
        return new(command.CommandId, Token(tx, command.Lane.Partition, position),
            [new(command.Action.ToString(), command.Lane.Queue, metadata.Id, updated.StateVersion)], Durability);
    }

    private MessageMetadata CompleteDeliveryAction(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy,
        DeliveryAction action, MessageMetadata metadata, long bodyBytes, DateTimeOffset now, ref QueueCounters counters)
    {
        if (action == DeliveryAction.Ack)
        {
            counters = counters with
            {
                StoredMessages = counters.StoredMessages - QueueDeliveryTransitionAdjacentElementOffset,
                StoredBytes = counters.StoredBytes - bodyBytes
            };
            tx.Delete(QueueKey(MessageBodySpace, lane, metadata.Id));
            return metadata with
            {
                State = MessageState.Acked,
                StateVersion = metadata.StateVersion + QueueDeliveryTransitionVersionOne,
                LeaseOwner = null,
                LeaseUntil = null
            };
        }
        if (metadata.Attempts >= policy.MaxAttempts)
        {
            return AdmitQueueDeadLetter(tx, lane, policy, metadata, bodyBytes, ref counters);
        }
        var delay = Math.Min(policy.RetryMaxMilliseconds,
            policy.RetryBaseMilliseconds * Math.Pow(QueueDeliveryTransitionRetryExponentialBase, Math.Min(metadata.Attempts - QueueDeliveryTransitionAdjacentElementOffset, messagingExecution.MaximumRetryExponent)));
        var available = now.AddMilliseconds(delay);
        tx.PutRecord(QueueKey(ScheduledQueueSpace, lane, available, metadata.Id), metadata.Id);
        return metadata with
        {
            State = MessageState.Scheduled,
            StateVersion = metadata.StateVersion + QueueDeliveryTransitionVersionOne,
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = available,
            SafeFailureCode = RetryRequested
        };
    }
}
