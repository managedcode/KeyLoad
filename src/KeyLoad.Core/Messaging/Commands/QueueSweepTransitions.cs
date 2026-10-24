using KeyLoad.Core.Messaging;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes QueueSweepTransitions operations against the caller's database context.</summary>
public static class QueueSweepTransitions
{
    internal const int QueueSweepTransitionsNamespaceComponentFromEnd = 2;
    internal const int QueueSweepTransitionsAdjacentElementOffset = 1;
    internal const int QueueSweepTransitionsVersionOne = 1;
    internal const string ScheduledQueueSpace = "scheduled";
    internal const string LeasedQueueSpace = "lease";
    internal const string ReadyQueueSpace = "ready";
    internal const string DeadLetterQueueSpace = "dead-letter";
    internal const string MessageBodySpace = "message-body";
    internal const string MessageMetadataSpace = "message-meta";
    internal const string QueueCountersSpace = "queue-counters";
    internal const string InboxQueueSpace = "inbox";
    internal const string AttemptsExhausted = "AttemptsExhausted";
    internal const string RetryRequested = "RetryRequested";
    internal const string EnqueueReceiptKind = "enqueue";
    internal const string QueuePaused = "This queue is paused.";
    internal const string DuplicateMessageId = "A message ID is already present in this lane.";
    internal const string InvalidMessageExpiry = "The message expiry precedes its availability.";
    internal const string StoredQuotaExhausted = "The queue stored-message quota is exhausted.";
    internal const string DispatchPausedMessage = "Queue dispatch is paused.";
    internal const string InvalidReceiveBudget = "The receive budget or lease duration is invalid.";
    internal const string InvalidDeliveryTokenScope = "The delivery token scope is invalid.";
    internal const string MissingLeasedMessage = "The message is unavailable.";
    internal const string StaleDeliveryLease = "The delivery lease is stale.";
    internal const string ExpiredDeliveryLease = "The delivery lease has expired.";
    internal const string MissingLeasedBody = "A leased message body is absent.";
    internal const string InvalidRenewal = "The renewal duration is invalid.";
    internal const string InvalidHandlerGeneration = "The handler generation is invalid.";
    internal const string InboxEffectConflict = "The inbox input was already completed with different effects.";
    internal const string MissingIndexedMetadata = "A queue index points to absent metadata.";
    internal const string MissingIndexedBody = "A queue message body is absent.";
    internal static void SweepDueEntries(this DatabaseEngine engine, IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now, Command<DatabaseCommand> operation)
    {
        // Sweep transitions use the replicated command time and one in-gate counter value.
        QueueCounters? counters = null;
        counters = engine.SweepDueSpace(tx, lane, policy, now, ScheduledQueueSpace, false, counters, operation);
        counters = engine.SweepDueSpace(tx, lane, policy, now, LeasedQueueSpace, true, counters, operation);
        if (counters is not null)
        {
            tx.PutRecord(MessagingExecutionMessaging.QueueKey(QueueCountersSpace, lane), counters);
        }
    }

    internal static QueueCounters? SweepDueSpace(this DatabaseEngine engine, IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now, string space, bool leased, QueueCounters? counters, Command<DatabaseCommand> operation)
    {
        var page = tx.Scan(MessagingExecutionMessaging.QueueKey(space, lane), engine.MessagingExecution.QueueScanPageSize);
        foreach (var item in page.Records)
        {
            var components = KeyCodec.Decode(item.Key.Span);
            if ((DateTimeOffset)components[^QueueSweepTransitionsNamespaceComponentFromEnd]! > now)
            {
                break;
            }

            counters = engine.ApplyDueEntry(tx, lane, policy, now, leased, item, counters, operation);
        }

        return counters;
    }

    internal static QueueCounters ApplyDueEntry(this DatabaseEngine engine, IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now, bool leased, KeyValueRecord item, QueueCounters? counters, Command<DatabaseCommand> operation)
    {
        var id = OrleansSerialization.Deserialize<string>(item.Value.Span);
        var metadataKey = MessagingExecutionMessaging.QueueKey(MessageMetadataSpace, lane, id);
        var metadata = tx.GetRecord<MessageMetadata>(metadataKey) ?? throw Errors.Fail(ErrorCode.Corruption, MissingIndexedMetadata);
        var body = StoredMessageBody.Read(tx, MessagingExecutionMessaging.QueueKey(MessageBodySpace, lane, id)) ?? throw Errors.Fail(ErrorCode.Corruption, MissingIndexedBody);
        if (body.Body.Id != id)
        {
            throw Errors.Fail(ErrorCode.Corruption, MissingIndexedBody);
        }

        QueueDueAuthority.RequireCurrentQueueDueEntry(lane, metadata, now, leased, item.Key.Span);
        var current = counters ?? MessagingExecutionMessaging.Counters(tx, lane);
        if (leased)
        {
            current = QueueLifecycleAccounting.ReleaseQueueInFlight(current, body.Bytes);
        }

        var plan = engine.PrepareQueueDueTransition(tx, lane, policy, now, metadata, body.Body, body.Bytes, current, operation, item.Key.Span);
        tx.Delete(item.Key.ToArray());
        QueueDueTransitionApply.ApplyQueueDueTransition(tx, lane, plan);
        tx.PutRecord(metadataKey, plan.Metadata);
        return plan.Counters;
    }
}
