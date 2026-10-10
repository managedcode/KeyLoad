using System.Collections.Immutable;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MessagingQueueCountersKeySpace = "queue-counters";
    private const int MessagingInitialSequence = 0;
    private const string MessagingSystemKeySpace = "system";
    private const string MessagingDispatchPausedKey = "dispatch-paused";
    private const int MessagingSingleElementCount = 1;
    private const int MessagingAdjacentElementOffset = 1;
    private const int MessagingEmptyElementCount = 0;
    private const int MessagingMinimumPositiveCount = 1;
    private const string MessagingMessageMetadataKeySpace = "message-meta";
    private const string MessagingMessageBodyKeySpace = "message-body";

    private static byte[] QueueKey(string space, QueueLaneRef lane, params object?[] tail)
        => KeySpace.Partition(space, lane.Partition, new object?[] { lane.Queue }.Concat(tail).ToArray());
    private static QueueCounters Counters(IKeyValueView view, QueueLaneRef lane)
    {
        var counters = view.GetRecord<QueueCounters>(QueueKey(MessagingQueueCountersKeySpace, lane));
        if (counters is null)
        {
            if (!view.Scan(QueueKey(MessagingMessageMetadataKeySpace, lane), MessagingSingleElementCount).Records.IsEmpty
                || !view.Scan(QueueKey(MessagingMessageBodyKeySpace, lane), MessagingSingleElementCount).Records.IsEmpty)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter); }
            counters = new(MessagingInitialSequence, MessagingInitialSequence, MessagingInitialSequence, MessagingInitialSequence, MessagingInitialSequence);
        }
        RequireQueueDeadLetterAuthority(view, lane, counters);
        return counters;
    }
    private bool DispatchPaused(IKeyValueView view) => view.ReadOwnedValue(KeyCodec.Encode(MessagingSystemKeySpace, MessagingDispatchPausedKey)) is { } value
        ? NativeSerialization.Deserialize<bool>(value) : Store.Identity.DispatchPaused;
    private MutationReceipt Enqueue(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, EnqueueMessage message, DateTimeOffset now)
    {
        JsonData.Identifier(message.MessageId);
        var resource = Resource(tx, partition, message.Queue, ResourceKind.WorkQueue);
        if (resource.Paused)
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, QueuePaused);
        }
        var lane = new QueueLaneRef(partition, message.Queue);
        if (tx.ReadOwnedValue(QueueKey(MessageMetadataSpace, lane, message.MessageId)) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, DuplicateMessageId);
        }
        if (message.ExpiresAt <= now || message.ExpiresAt <= message.NotBefore)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMessageExpiry);
        }
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }
        foreach (var policy in resource.HeaderPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
        var body = new MessageBody(message.MessageId, JsonData.Validate(message.PayloadJson, Limits),
            JsonData.Validate(message.HeadersJson, Limits), message.OrderingKey, JsonData.Fingerprint(message));
        PersistEnqueuedMessage(tx, lane, message, resource, body, now);
        return new(EnqueueReceiptKind, message.Queue, message.MessageId, MessagingSingleElementCount);
    }

    private static void PersistEnqueuedMessage(IAtomicTransaction tx, QueueLaneRef lane, EnqueueMessage message,
        ResourceDefinition resource, MessageBody body, DateTimeOffset now)
    {
        var counters = Counters(tx, lane);
        var payload = NativeSerialization.Serialize(body);
        var size = payload.LongLength;
        if (counters.StoredMessages >= resource.QueuePolicy.MaxStoredMessages
            || size > resource.QueuePolicy.MaxStoredBytes - counters.StoredBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, StoredQuotaExhausted);
        }
        var scheduled = message.NotBefore > now;
        var readySequence = scheduled ? MessagingInitialSequence : checked(counters.NextReadySequence + MessagingAdjacentElementOffset);
        var metadata = new MessageMetadata(message.MessageId, scheduled ? MessageState.Scheduled : MessageState.Ready,
            MessagingEmptyElementCount, MessagingSingleElementCount, readySequence, message.NotBefore, message.ExpiresAt);
        tx.Put(QueueKey(MessageBodySpace, lane, message.MessageId), payload);
        tx.PutRecord(QueueKey(MessageMetadataSpace, lane, message.MessageId), metadata);
        tx.PutRecord(scheduled ? QueueKey(ScheduledQueueSpace, lane, message.NotBefore!.Value, message.MessageId)
            : QueueKey(ReadyQueueSpace, lane, readySequence, message.MessageId), message.MessageId);
        tx.PutRecord(QueueKey(QueueCountersSpace, lane), counters with
        {
            StoredMessages = counters.StoredMessages + MessagingAdjacentElementOffset,
            StoredBytes = counters.StoredBytes + size,
            NextReadySequence = scheduled ? counters.NextReadySequence : readySequence
        });
    }

    private void Sweep(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now)
        => SweepDueEntries(tx, lane, policy, now);

    private ReceiveResult Receive(IAtomicTransaction tx, PrincipalRecord principal, ReceiveRequest request, DateTimeOffset now, long position)
    {
        var resource = Resource(tx, request.Lane.Partition, request.Lane.Queue, ResourceKind.WorkQueue);
        var policy = resource.QueuePolicy;
        if (DispatchPaused(tx) || resource.Paused)
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, DispatchPausedMessage);
        }
        if (request.MaxMessages < MessagingMinimumPositiveCount || request.MaxMessages > messagingExecution.MaximumReceiveMessages || request.MaxBytes is < MessagingMinimumPositiveCount || request.MaxBytes > Limits.MaxBatchBytes
            || request.LeaseSeconds < MessagingMinimumPositiveCount || request.LeaseSeconds > policy.MaxLeaseSeconds)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidReceiveBudget);
        }
        Sweep(tx, request.Lane, policy, now);
        return ClaimReadyMessages(tx, principal, request, resource, now, position);
    }

    /// <summary>Signs versioned native claim bytes with the persisted store signing key.</summary>
    /// <typeparam name="T">Claim type.</typeparam>
    /// <param name="claims">Claim value to sign.</param>
    /// <returns>Signed URL-safe token.</returns>
    public string Sign<T>(T claims)
        => CoreNativeClaims.Sign(Store.Identity.SigningKey.Span, claims);
    /// <summary>Verifies a signed token and restores its native claims.</summary>
    /// <typeparam name="T">Claim type.</typeparam>
    /// <param name="token">Signed token.</param>
    /// <returns>Verified claims admitted by the captured native execution policy.</returns>
    public T Verify<T>(string token) => Verify<T>(token, ClaimsExecution.MaximumTokenCharacters);

    /// <summary>Verifies a signed token using an explicitly bounded operand limit.</summary>
    /// <typeparam name="T">Claim type.</typeparam>
    /// <param name="token">Signed token.</param>
    /// <param name="maximumCharacters">Maximum accepted token length.</param>
    /// <returns>Verified claims.</returns>
    public T Verify<T>(string token, int maximumCharacters)
        => CoreNativeClaims.Verify<T>(Store.Identity.SigningKey.Span, token, maximumCharacters);
    private ValidatedQueueLease Lease(IKeyValueView view, PrincipalRecord principal,
        QueueLaneRef lane, string token, DateTimeOffset now)
        => Lease(view, principal, lane, Verify<DeliveryClaims>(token), now);

    private ValidatedQueueLease Lease(IKeyValueView view, PrincipalRecord principal,
        QueueLaneRef lane, DeliveryClaims claims, DateTimeOffset now)
    {
        if (claims.Incarnation != Store.Identity.Incarnation || claims.Lane != lane || claims.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidDeliveryTokenScope);
        }
        var metadata = view.GetRecord<MessageMetadata>(QueueKey(MessageMetadataSpace, lane, claims.MessageId))
            ?? throw Errors.Fail(ErrorCode.NotFound, MissingLeasedMessage);
        if (metadata.State != MessageState.Leased || metadata.LeaseOwner != principal.Id || metadata.LeaseVersion != claims.LeaseVersion
            || metadata.DeliveryGeneration != claims.DeliveryGeneration)
        {
            throw Errors.Fail(ErrorCode.StaleLease, StaleDeliveryLease);
        }
        if (metadata.LeaseUntil <= now)
        {
            throw Errors.Fail(ErrorCode.LeaseExpired, ExpiredDeliveryLease);
        }
        var body = StoredMessageBody.Read(view, QueueKey(MessageBodySpace, lane, claims.MessageId))
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingLeasedBody);
        return new(claims, metadata, body.Bytes);
    }
    private CommitReceipt CompleteDelivery(IAtomicTransaction tx, PrincipalRecord principal, DeliveryCommand command, DateTimeOffset now, long position)
    {
        var resource = Resource(tx, command.Lane.Partition, command.Lane.Queue, ResourceKind.WorkQueue);
        var lease = Lease(tx, principal, command.Lane, command.Token, now);
        return ApplyDeliveryTransition(tx, command, resource, lease, now, position);
    }

    private CommitReceipt CompleteProcessing(IAtomicTransaction tx, PrincipalRecord principal, ProcessingRequest request, DateTimeOffset now, long position)
    {
        JsonData.Identifier(request.HandlerScope);
        if (request.ExecutionGeneration < MessagingMinimumPositiveCount)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidHandlerGeneration);
        }
        var claims = Verify<DeliveryClaims>(request.Token);
        if (claims.Incarnation != Store.Identity.Incarnation || claims.Lane != request.Lane || claims.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidDeliveryTokenScope);
        }
        var inboxKey = QueueKey(InboxQueueSpace, request.Lane, claims.MessageId, request.HandlerScope, request.ExecutionGeneration);
        var fingerprint = JsonData.Fingerprint(new { request.Lane, claims.MessageId, request.HandlerScope, request.ExecutionGeneration, request.Effects, principal.Id });
        if (tx.GetRecord<InboxRecord>(inboxKey) is { } completed)
        {
            if (completed.Fingerprint != fingerprint)
            {
                throw Errors.Fail(ErrorCode.Conflict, InboxEffectConflict);
            }
            return completed.Receipt;
        }
        var lease = Lease(tx, principal, request.Lane, claims, now);
        var resource = Resource(tx, request.Lane.Partition, request.Lane.Queue, ResourceKind.WorkQueue);
        var ack = ApplyDeliveryTransition(tx,
            new(request.CommandId, request.Lane, request.Token, DeliveryAction.Ack), resource, lease, now, position);
        var effects = ApplyMutations(tx, principal, request.Lane.Partition, request.Effects, now, position);
        var receipt = ack with { Mutations = effects.Concat(ack.Mutations).ToImmutableArray() };
        tx.PutRecord(inboxKey, new InboxRecord(fingerprint, receipt));
        return receipt;
    }

    /// <summary>Reads authorized queue metadata and projected message content.</summary>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="lane">Queue lane identity.</param>
    /// <param name="id">Message identity.</param>
    /// <returns>Visible message details, or null when unavailable.</returns>
    public MessageInspection? InspectMessage(string principalId, QueueLaneRef lane, string id) => Store.Read(view =>
    {
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        Authorization.Require(principal, lane.Partition, lane.Queue, Capability.QueueInspect);
        var resource = Resource(view, lane.Partition, lane.Queue, ResourceKind.WorkQueue);
        var metadata = view.GetRecord<MessageMetadata>(QueueKey(MessagingMessageMetadataKeySpace, lane, id));
        if (metadata is null)
        {
            return null;
        }

        if (metadata.State is MessageState.DeadLettered or MessageState.PendingDeadLetter)
        {
            Authorization.Require(principal, lane.Partition, lane.Queue, Capability.DeadLettersRead);
            _ = RequireQueueLifecycleCounters(view, lane);
            if (metadata.State == MessageState.DeadLettered)
            { RequireQueueParkedAuthority(view, lane, metadata); }
            else
            { RequireQueuePendingAuthority(view, lane, metadata); }
        }

        var body = view.GetRecord<MessageBody>(QueueKey(MessagingMessageBodyKeySpace, lane, id));
        return new MessageInspection(metadata, body is null ? null : Authorization.Project(principal, resource.FieldPolicies, body.PayloadJson, out _),
            body is null ? null : Authorization.Project(principal, resource.HeaderPolicies, body.HeadersJson, out _));
    });
}
