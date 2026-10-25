using System.Collections.Immutable;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Owns the QueueDeliveryExecution operation contract.</summary>
public static class QueueDeliveryExecution
{
    internal static ReceiveResult Receive(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, ReceiveRequest request, DateTimeOffset now, long position, Command<DatabaseCommand> operation)
    {
        var resource = engine.Resource(tx, request.Lane.Partition, request.Lane.Queue, ResourceKind.WorkQueue);
        var policy = resource.QueuePolicy;
        if (engine.DispatchPaused(tx) || resource.Paused)
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, QueueSweepTransitions.DispatchPausedMessage);
        }

        if (request.MaxMessages < MessagingExecutionMessaging.MessagingMinimumPositiveCount || request.MaxMessages > engine.MessagingExecution.MaximumReceiveMessages || request.MaxBytes is < MessagingExecutionMessaging.MessagingMinimumPositiveCount || request.LeaseSeconds < MessagingExecutionMessaging.MessagingMinimumPositiveCount || request.LeaseSeconds > policy.MaxLeaseSeconds)
        {
            throw Errors.Fail(ErrorCode.Validation, QueueSweepTransitions.InvalidReceiveBudget);
        }

        engine.Sweep(tx, request.Lane, policy, now, operation);
        return engine.ClaimReadyMessages(tx, principal, request, resource, now, position);
    }

    internal static ValidatedQueueLease Lease(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, QueueLaneRef lane, string token, DateTimeOffset now) => engine.Lease(view, principal, lane, engine.Verify<DeliveryClaims>(token), now);
    internal static ValidatedQueueLease Lease(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, QueueLaneRef lane, DeliveryClaims claims, DateTimeOffset now)
    {
        if (claims.Incarnation != engine.Store.Identity.Incarnation || claims.Lane != lane || claims.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, QueueSweepTransitions.InvalidDeliveryTokenScope);
        }

        var metadata = view.GetRecord<MessageMetadata>(MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, claims.MessageId)) ?? throw Errors.Fail(ErrorCode.NotFound, QueueSweepTransitions.MissingLeasedMessage);
        if (metadata.State != MessageState.Leased || metadata.LeaseOwner != principal.Id || metadata.LeaseVersion != claims.LeaseVersion || metadata.DeliveryGeneration != claims.DeliveryGeneration)
        {
            throw Errors.Fail(ErrorCode.StaleLease, QueueSweepTransitions.StaleDeliveryLease);
        }

        if (metadata.LeaseUntil <= now)
        {
            throw Errors.Fail(ErrorCode.LeaseExpired, QueueSweepTransitions.ExpiredDeliveryLease);
        }

        var body = (StoredMessageBody.Read(view, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, claims.MessageId))).Value ?? throw Errors.Fail(ErrorCode.Corruption, QueueSweepTransitions.MissingLeasedBody);
        QueueLeases.RequireQueueLeaseAuthority(view, lane, claims.MessageId, metadata, body.Body);
        return new(claims, metadata, body.Bytes);
    }

    internal static CommitReceipt CompleteDelivery(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, DeliveryCommand command, DateTimeOffset now, long position, Command<DatabaseCommand> operation)
    {
        var resource = engine.Resource(tx, command.Lane.Partition, command.Lane.Queue, ResourceKind.WorkQueue);
        var lease = engine.Lease(tx, principal, command.Lane, command.Token, now);
        return engine.ApplyDeliveryTransition(tx, command, resource, lease, now, position, operation);
    }

    internal static CommitReceipt CompleteProcessing(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, ProcessingRequest request, DateTimeOffset now, long position)
    {
        JsonData.Identifier(request.HandlerScope);
        if (request.ExecutionGeneration < MessagingExecutionMessaging.MessagingMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.Validation, QueueSweepTransitions.InvalidHandlerGeneration);
        }

        var claims = engine.Verify<DeliveryClaims>(request.Token);
        if (claims.Incarnation != engine.Store.Identity.Incarnation || claims.Lane != request.Lane || claims.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, QueueSweepTransitions.InvalidDeliveryTokenScope);
        }

        var inboxKey = MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.InboxQueueSpace, request.Lane, claims.MessageId, request.HandlerScope, request.ExecutionGeneration);
        var fingerprint = JsonData.Fingerprint(new { request.Lane, claims.MessageId, request.HandlerScope, request.ExecutionGeneration, request.Effects, principal.Id });
        if (tx.GetRecord<InboxRecord>(inboxKey) is { } completed)
        {
            if (completed.Fingerprint != fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, QueueSweepTransitions.InboxEffectConflict);
            }

            return completed.Receipt;
        }

        var lease = engine.Lease(tx, principal, request.Lane, claims, now);
        var resource = engine.Resource(tx, request.Lane.Partition, request.Lane.Queue, ResourceKind.WorkQueue);
        var ack = engine.ApplyDeliveryTransition(tx, new(request.CommandId, request.Lane, request.Token, DeliveryAction.Ack), resource, lease, now, position);
        var effects = engine.ApplyMutations(tx, principal, request.Lane.Partition, request.Effects, now, position);
        var receipt = ack with
        {
            Mutations = effects.Concat(ack.Mutations).ToImmutableArray()
        };
        tx.PutRecord(inboxKey, new InboxRecord(fingerprint, receipt));
        return receipt;
    }

    /// <summary>Reads authorized queue metadata and projected message content.</summary>
    /// <param name = "principalId">Persisted principal identifier.</param>
    /// <param name = "lane">Queue lane identity.</param>
    /// <param name = "id">Message identity.</param>
    /// <returns>Visible message details, or null when unavailable.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static MessageInspection? InspectMessage(this DatabaseEngine engine, string principalId, QueueLaneRef lane, string id)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Store.Read(view => (engine.InspectMessageRead(view, principalId, lane, id)).Value);
    }

    internal static (bool HasValue, MessageInspection? Value) InspectMessageRead(this DatabaseEngine engine, IKeyValueView view, string principalId, QueueLaneRef lane, string id)
    {
        var principal = engine.Principal(view, principalId, engine.Clock.GetUtcNow());
        engine.Authorization.Require(principal, lane.Partition, lane.Queue, Capability.QueueInspect);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        var metadata = view.GetRecord<MessageMetadata>(MessagingExecutionMessaging.QueueKey(MessagingExecutionMessaging.MessagingMessageMetadataKeySpace, lane, id));
        if (metadata is null)
        {
            if (view.ReadOwnedValue(MessagingExecutionMessaging.QueueKey(MessagingExecutionMessaging.MessagingMessageBodyKeySpace, lane, id)) is not null)
            {
                throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingMetadata);
            }

            return (false, default(MessageInspection?));
        }

        var retainedDeadLetter = metadata.State is MessageState.DeadLettered or MessageState.PendingDeadLetter;
        if (retainedDeadLetter)
        {
            engine.Authorization.Require(principal, lane.Partition, lane.Queue, Capability.DeadLettersRead);
        }

        if (metadata.Id != id)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingReference);
        }

        if (metadata.State is not (MessageState.Scheduled or MessageState.Ready or MessageState.Leased or MessageState.Acked or MessageState.DeadLettered or MessageState.Cancelled or MessageState.Expired or MessageState.PendingDeadLetter))
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.InvalidPersistedState);
        }

        if (retainedDeadLetter)
        {
            _ = QueueDeadLetterAuthority.RequireQueueLifecycleCounters(view, lane);
            if (metadata.State == MessageState.DeadLettered)
            {
                QueueDeadLetterAuthority.RequireQueueParkedAuthority(view, lane, metadata);
            }
            else
            {
                QueueDeadLetterAuthority.RequireQueuePendingAuthority(view, lane, metadata);
            }
        }

        var body = view.GetRecord<MessageBody>(MessagingExecutionMessaging.QueueKey(MessagingExecutionMessaging.MessagingMessageBodyKeySpace, lane, id));
        var retainedBody = metadata.State is MessageState.Ready or MessageState.Scheduled or MessageState.Leased or MessageState.DeadLettered or MessageState.PendingDeadLetter;
        var terminalState = metadata.State is MessageState.Acked or MessageState.Cancelled or MessageState.Expired;
        if (retainedBody && (body is null || body.Id != id) || terminalState && body is not null)
        {
            throw Errors.Fail(ErrorCode.Corruption, QueueLifecycleProtocol.MissingBody);
        }

        return (true, ((MessageInspection?)(new MessageInspection(metadata, body is null ? null : engine.Authorization.Project(principal, resource.FieldPolicies, body.PayloadJson, out _), body is null ? null : engine.Authorization.Project(principal, resource.HeaderPolicies, body.HeadersJson, out _)))));
    }
}
