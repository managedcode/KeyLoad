using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsQueueLifecycle operations against the caller's database context.</summary>
public static class MessagingCommandsQueueLifecycle
{
    internal const string QueueDeliveryVersionExhausted = "The queue delivery state version is exhausted.";
    internal const int QueueDeliveryTransitionMinimumPositiveCount = 1;
    internal const int QueueDeliveryTransitionVersionOne = 1;
    internal const int DefaultRenewalSeconds = 30;
    internal const string InvalidQueueDeliveryAction = "Queue delivery action is invalid.";
    internal static CommitReceipt ApplyDeliveryTransition(this DatabaseEngine engine, IAtomicTransaction tx, DeliveryCommand command, ResourceDefinition resource, ValidatedQueueLease lease, DateTimeOffset now, long position, Command<DatabaseCommand>? operation = null)
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

        var counters = MessagingExecutionMessaging.Counters(tx, command.Lane);
        QueueDeadLetterAdmissionPlan? deadLetter = null;
        if (command.Action == DeliveryAction.Nack && metadata.Attempts >= resource.QueuePolicy.MaxAttempts)
        {
            var released = QueueLifecycleAccounting.ReleaseQueueInFlight(counters, lease.BodyBytes);
            deadLetter = QueueDeadLetters.PrepareQueueDeadLetterAdmission(tx, command.Lane, resource.QueuePolicy, metadata, lease.BodyBytes, released);
        }

        tx.Delete(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.LeasedQueueSpace, command.Lane, metadata.LeaseUntil!.Value, metadata.Id));
        MessageMetadata updated;
        if (command.Action == DeliveryAction.Renew)
        {
            var seconds = command.LeaseSeconds ?? DefaultRenewalSeconds;
            if (seconds < QueueDeliveryTransitionMinimumPositiveCount || seconds > resource.QueuePolicy.MaxLeaseSeconds)
            {
                throw Errors.Fail(ErrorCode.Validation, QueueSweepTransitions.InvalidRenewal);
            }

            var deadline = now.AddSeconds(seconds);
            updated = metadata with
            {
                LeaseUntil = deadline,
                StateVersion = metadata.StateVersion + QueueDeliveryTransitionVersionOne
            };
            tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.LeasedQueueSpace, command.Lane, deadline, metadata.Id), metadata.Id);
        }
        else
        {
            counters = QueueLifecycleAccounting.ReleaseQueueInFlight(counters, lease.BodyBytes);
            updated = engine.CompleteDeliveryAction(tx, command.Lane, resource.QueuePolicy, command.Action, metadata, lease.BodyBytes, now, ref counters, operation, deadLetter);
        }

        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, command.Lane, metadata.Id), updated);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.QueueCountersSpace, command.Lane), counters);
        return new(command.CommandId, engine.Token(tx, command.Lane.Partition, position), [new(command.Action.ToString(), command.Lane.Queue, metadata.Id, updated.StateVersion)], engine.Durability, engine.AcknowledgedPersistedCopies);
    }

    internal static MessageMetadata CompleteDeliveryAction(this DatabaseEngine engine, IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, DeliveryAction action, MessageMetadata metadata, long bodyBytes, DateTimeOffset now, ref QueueCounters counters, Command<DatabaseCommand>? operation, QueueDeadLetterAdmissionPlan? deadLetter = null)
    {
        if (action == DeliveryAction.Ack)
        {
            counters = QueueLifecycleAccounting.ReleaseQueueStored(counters, bodyBytes);
            var body = StoredMessageBody.Read(tx, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, metadata.Id)) ?? throw Errors.Fail(ErrorCode.Corruption, QueueSweepTransitions.MissingIndexedBody);
            QueueOrderTransitions.RemoveQueueOrder(tx, lane, policy, body.Body, metadata);
            tx.Delete(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, metadata.Id));
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
            return MessagingCommands.AdmitQueueDeadLetter(tx, lane, policy, metadata, bodyBytes, ref counters, deadLetter);
        }

        var available = policy.RetryJitter == QueueRetryJitter.None ? now.AddMilliseconds(engine.QueueRetryCap(policy, metadata.Attempts)) : engine.QueueRetryAt(operation ?? throw Errors.Fail(ErrorCode.Corruption, QueueRetryProtocol.MissingDecision), lane, policy, metadata, ReadOnlySpan<byte>.Empty);
        tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.ScheduledQueueSpace, lane, available, metadata.Id), metadata.Id);
        return metadata with
        {
            State = MessageState.Scheduled,
            StateVersion = metadata.StateVersion + QueueDeliveryTransitionVersionOne,
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = available,
            SafeFailureCode = QueueSweepTransitions.RetryRequested
        };
    }
}
