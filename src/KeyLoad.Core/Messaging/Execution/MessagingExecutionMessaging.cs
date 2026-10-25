using KeyLoad.Core.InternalSerialization;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes MessagingExecutionMessaging operations against the caller's database context.</summary>
public static class MessagingExecutionMessaging
{
    internal const string MessagingQueueCountersKeySpace = "queue-counters";
    internal const int MessagingInitialSequence = 0;
    internal const string MessagingSystemKeySpace = "system";
    internal const string MessagingDispatchPausedKey = "dispatch-paused";
    internal const int MessagingSingleElementCount = 1;
    internal const int MessagingAdjacentElementOffset = 1;
    internal const int MessagingEmptyElementCount = 0;
    internal const int MessagingMinimumPositiveCount = 1;
    internal const string MessagingMessageMetadataKeySpace = "message-meta";
    internal const string MessagingMessageBodyKeySpace = "message-body";
    internal static byte[] QueueKey(string space, QueueLaneRef lane, params object?[] tail) => KeySpace.Partition(space, lane.Partition, new object?[] { lane.Queue }.Concat(tail).ToArray());
    internal static QueueCounters Counters(IKeyValueView view, QueueLaneRef lane)
    {
        var counters = view.GetRecord<QueueCounters>(QueueKey(MessagingQueueCountersKeySpace, lane));
        if (counters is null)
        {
            if (!view.Scan(QueueKey(MessagingMessageMetadataKeySpace, lane), MessagingSingleElementCount).Records.IsEmpty || !view.Scan(QueueKey(MessagingMessageBodyKeySpace, lane), MessagingSingleElementCount).Records.IsEmpty)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, QueueLifecycleProtocol.InvalidCounter);
            }

            counters = new(MessagingInitialSequence, MessagingInitialSequence, MessagingInitialSequence, MessagingInitialSequence, MessagingInitialSequence);
        }

        QueueDeadLetterAuthority.RequireQueueDeadLetterAuthority(view, lane, counters);
        return counters;
    }

    internal static bool DispatchPaused(this DatabaseEngine engine, IKeyValueView view) => view.ReadOwnedValue(KeyCodec.Encode(MessagingSystemKeySpace, MessagingDispatchPausedKey)) is { } value ? OrleansSerialization.Deserialize<bool>(value) : engine.Store.Identity.DispatchPaused;
    internal static MutationReceipt Enqueue(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, EnqueueMessage message, DateTimeOffset now)
    {
        JsonData.Identifier(message.MessageId);
        var resource = engine.Resource(tx, partition, message.Queue, ResourceKind.WorkQueue);
        if (resource.Paused)
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, QueueSweepTransitions.QueuePaused);
        }

        var lane = new QueueLaneRef(partition, message.Queue);
        if (tx.ReadOwnedValue(QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, message.MessageId)) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, QueueSweepTransitions.DuplicateMessageId);
        }

        if (message.ExpiresAt <= now || message.ExpiresAt <= message.NotBefore)
        {
            throw Errors.Fail(ErrorCode.Validation, QueueSweepTransitions.InvalidMessageExpiry);
        }

        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        foreach (var policy in resource.HeaderPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }

        var body = new MessageBody(message.MessageId, JsonData.ValidateContent(message.PayloadJson), JsonData.ValidateContent(message.HeadersJson), message.OrderingKey, JsonData.Fingerprint(message));
        PersistEnqueuedMessage(tx, lane, message, resource, body, now);
        return new(QueueSweepTransitions.EnqueueReceiptKind, message.Queue, message.MessageId, MessagingSingleElementCount);
    }

    internal static void PersistEnqueuedMessage(IAtomicTransaction tx, QueueLaneRef lane, EnqueueMessage message, ResourceDefinition resource, MessageBody body, DateTimeOffset now)
    {
        var counters = Counters(tx, lane);
        var payload = OrleansSerialization.Serialize(body);
        var size = payload.LongLength;
        if (counters.StoredMessages >= resource.QueuePolicy.MaxStoredMessages || size > resource.QueuePolicy.MaxStoredBytes - counters.StoredBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueSweepTransitions.StoredQuotaExhausted);
        }

        var scheduled = message.NotBefore > now;
        if ((!scheduled && counters.NextReadySequence == long.MaxValue) || (resource.QueuePolicy.OrderingProfile == QueueOrderingProfile.StrictPerKey && counters.NextOrderSequence == long.MaxValue))
        {
            if (resource.QueuePolicy.OrderingProfile == QueueOrderingProfile.StrictPerKey)
            {
                MessagingCommandsQueueOrdering.RequireQueueOrderingKey(body);
            }

            throw Errors.Fail(ErrorCode.ResourceExhausted, QueueEnqueueProtocol.SequenceExhausted);
        }

        var readySequence = scheduled ? MessagingInitialSequence : checked(counters.NextReadySequence + MessagingAdjacentElementOffset);
        var metadata = new MessageMetadata(message.MessageId, scheduled ? MessageState.Scheduled : MessageState.Ready, MessagingEmptyElementCount, MessagingSingleElementCount, readySequence, message.NotBefore, message.ExpiresAt);
        (metadata, counters) = MessagingCommandsQueueOrdering.AdmitQueueOrder(tx, lane, resource.QueuePolicy, body, metadata, counters);
        tx.Put(QueueKey(QueueSweepTransitions.MessageBodySpace, lane, message.MessageId), payload);
        tx.PutRecord(QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, message.MessageId), metadata);
        tx.PutRecord(scheduled ? QueueKey(QueueSweepTransitions.ScheduledQueueSpace, lane, message.NotBefore!.Value, message.MessageId) : QueueKey(QueueSweepTransitions.ReadyQueueSpace, lane, readySequence, message.MessageId), message.MessageId);
        tx.PutRecord(QueueKey(QueueSweepTransitions.QueueCountersSpace, lane), counters with { StoredMessages = counters.StoredMessages + MessagingAdjacentElementOffset, StoredBytes = counters.StoredBytes + size, NextReadySequence = scheduled ? counters.NextReadySequence : readySequence });
    }

    internal static void Sweep(this DatabaseEngine engine, IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now, Command<DatabaseCommand> operation) => engine.SweepDueEntries(tx, lane, policy, now, operation);

    /// <summary>Signs versioned execution claim bytes with the persisted store signing key.</summary>
    /// <typeparam name = "T">Claim type.</typeparam>
    /// <param name = "claims">Claim value to sign.</param>
    /// <returns>Signed URL-safe token.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static string Sign<T>(this DatabaseEngine engine, T claims)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return CoreClaims.Sign(engine.Store.Identity.SigningKey.Span, claims);
    }

    /// <summary>Verifies a signed token and restores its execution claims.</summary>
    /// <typeparam name = "T">Claim type.</typeparam>
    /// <param name = "token">Signed token.</param>
    /// <returns>Verified claims admitted by the captured execution execution policy.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static T Verify<T>(this DatabaseEngine engine, string token)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.Verify<T>(token, engine.ClaimsExecution.MaximumTokenCharacters);
    }

    /// <summary>Verifies a signed token using an explicitly bounded operand limit.</summary>
    /// <typeparam name = "T">Claim type.</typeparam>
    /// <param name = "token">Signed token.</param>
    /// <param name = "maximumCharacters">Maximum accepted token length.</param>
    /// <returns>Verified claims.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static T Verify<T>(this DatabaseEngine engine, string token, int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return CoreClaims.Verify<T>(engine.Store.Identity.SigningKey.Span, token, maximumCharacters);
    }

    /// <summary>Verifies a server-issued token whose size only its carrying record bounds.</summary>
    /// <typeparam name = "T">Claim type.</typeparam>
    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "token">Signed token.</param>
    /// <returns>Verified claims.</returns>
    internal static T VerifyRetained<T>(this DatabaseEngine engine, string token)
        => engine.Verify<T>(token, CoreClaims.UnboundedCharacters);
}
