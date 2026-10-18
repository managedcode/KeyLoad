using ManagedCode.Communication.Commands;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string QueueDeliveryVersionExhausted = "The queue delivery state version is exhausted.";

    private const int QueueDeliveryTransitionMinimumPositiveCount = 1;
    private const int QueueDeliveryTransitionVersionOne = 1;

    private const int DefaultRenewalSeconds = 30;
    private const string InvalidQueueDeliveryAction = "Queue delivery action is invalid.";

    private CommitReceipt ApplyDeliveryTransition(IAtomicTransaction tx, DeliveryCommand command,
        ResourceDefinition resource, ValidatedQueueLease lease, DateTimeOffset now, long position, Command<DatabaseCommand>? operation = null)
    {
        if (command.Action is not (DeliveryAction.Ack or DeliveryAction.Nack or DeliveryAction.Renew))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidQueueDeliveryAction);
        }
        var metadata = lease.Metadata;
        if (metadata.StateVersion == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueDeliveryVersionExhausted);
        }
        var counters = Counters(tx, command.Lane);
        QueueDeadLetterAdmissionPlan? deadLetter = null;
        if (command.Action == DeliveryAction.Nack && metadata.Attempts >= resource.QueuePolicy.MaxAttempts)
        {
            var released = ReleaseQueueInFlight(counters, lease.BodyBytes);
            deadLetter = PrepareQueueDeadLetterAdmission(tx, command.Lane, resource.QueuePolicy,
                metadata, lease.BodyBytes, released);
        }
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
            updated = metadata with
            {
                LeaseUntil = deadline,
                StateVersion = metadata.StateVersion + QueueDeliveryTransitionVersionOne
            };
            tx.PutRecord(QueueKey(LeasedQueueSpace, command.Lane, deadline, metadata.Id), metadata.Id);
        }
        else
        {
            counters = ReleaseQueueInFlight(counters, lease.BodyBytes);
            updated = CompleteDeliveryAction(tx, command.Lane, resource.QueuePolicy, command.Action,
                metadata, lease.BodyBytes, now, ref counters, operation, deadLetter);
        }
        tx.PutRecord(QueueKey(MessageMetadataSpace, command.Lane, metadata.Id), updated);
        tx.PutRecord(QueueKey(QueueCountersSpace, command.Lane), counters);
        return new(command.CommandId, Token(tx, command.Lane.Partition, position),
            [new(command.Action.ToString(), command.Lane.Queue, metadata.Id, updated.StateVersion)], Durability, AcknowledgedPersistedCopies);
    }

    private MessageMetadata CompleteDeliveryAction(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy,
        DeliveryAction action, MessageMetadata metadata, long bodyBytes, DateTimeOffset now, ref QueueCounters counters,
        Command<DatabaseCommand>? operation, QueueDeadLetterAdmissionPlan? deadLetter = null)
    {
        if (action == DeliveryAction.Ack)
        {
            counters = ReleaseQueueStored(counters, bodyBytes);
            var body = StoredMessageBody.Read(tx, QueueKey(MessageBodySpace, lane, metadata.Id))
                ?? throw Errors.Fail(ErrorCode.Corruption, MissingIndexedBody);
            RemoveQueueOrder(tx, lane, policy, body.Body, metadata);
            tx.Delete(QueueKey(MessageBodySpace, lane, metadata.Id));
            return metadata with
            {
                State = MessageState.Acked,
                ActiveOrderSequence = QueueOrderProtocol.Initial,
                StateVersion = metadata.StateVersion + QueueDeliveryTransitionVersionOne,
                LeaseOwner = null,
                LeaseUntil = null
            };
        }
        if (metadata.Attempts >= policy.MaxAttempts)
        {
            return AdmitQueueDeadLetter(tx, lane, policy, metadata, bodyBytes, ref counters, deadLetter);
        }
        var available = policy.RetryJitter == QueueRetryJitter.None
            ? now.AddMilliseconds(QueueRetryCap(policy, metadata.Attempts))
            : QueueRetryAt(operation ?? throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.MissingDecision),
                lane, policy, metadata, ReadOnlySpan<byte>.Empty);
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
