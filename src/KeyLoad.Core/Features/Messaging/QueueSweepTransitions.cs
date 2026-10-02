using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string ScheduledQueueSpace = "scheduled";
    private const string LeasedQueueSpace = "lease";
    private const string ReadyQueueSpace = "ready";
    private const string DeadLetterQueueSpace = "dead-letter";
    private const string MessageBodySpace = "message-body";
    private const string MessageMetadataSpace = "message-meta";
    private const string QueueCountersSpace = "queue-counters";
    private const string InboxQueueSpace = "inbox";
    private const string AttemptsExhausted = "AttemptsExhausted";
    private const string RetryRequested = "RetryRequested";
    private const string EnqueueReceiptKind = "enqueue";
    private const string QueuePaused = "This queue is paused.";
    private const string DuplicateMessageId = "A message ID is already present in this lane.";
    private const string InvalidMessageExpiry = "The message expiry precedes its availability.";
    private const string StoredQuotaExhausted = "The queue stored-message quota is exhausted.";
    private const string DispatchPausedMessage = "Queue dispatch is paused.";
    private const string InvalidReceiveBudget = "The receive budget or lease duration is invalid.";
    private const string InvalidDeliveryTokenScope = "The delivery token scope is invalid.";
    private const string MissingLeasedMessage = "The message is unavailable.";
    private const string StaleDeliveryLease = "The delivery lease is stale.";
    private const string ExpiredDeliveryLease = "The delivery lease has expired.";
    private const string MissingLeasedBody = "A leased message body is absent.";
    private const string InvalidRenewal = "The renewal duration is invalid.";
    private const string InvalidHandlerGeneration = "The handler generation is invalid.";
    private const string InboxEffectConflict = "The inbox input was already completed with different effects.";
    private const string MissingIndexedMetadata = "A queue index points to absent metadata.";
    private const string MissingIndexedBody = "A queue message body is absent.";
    private const int QueueScanPageSize = 256;

    private static void SweepDueEntries(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy, DateTimeOffset now)
    {
        // Sweep transitions use the replicated command time and one in-gate counter value.
        QueueCounters? counters = null;
        counters = SweepDueSpace(tx, lane, policy, now, ScheduledQueueSpace, false, counters);
        counters = SweepDueSpace(tx, lane, policy, now, LeasedQueueSpace, true, counters);
        if (counters is not null)
        {
            tx.PutRecord(QueueKey(QueueCountersSpace, lane), counters);
        }
    }

    private static QueueCounters? SweepDueSpace(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy,
        DateTimeOffset now, string space, bool leased, QueueCounters? counters)
    {
        var page = tx.Scan(QueueKey(space, lane), QueueScanPageSize);
        foreach (var item in page.Records)
        {
            var components = KeyCodec.Decode(item.Key.Span);
            if ((DateTimeOffset)components[^2]! > now)
            {
                break;
            }
            counters = ApplyDueEntry(tx, lane, policy, now, leased, item, counters);
        }
        return counters;
    }

    private static QueueCounters ApplyDueEntry(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy,
        DateTimeOffset now, bool leased, KeyValueRecord item, QueueCounters? counters)
    {
        var id = JsonDefaults.Deserialize<string>(item.Value.Span);
        var metadataKey = QueueKey(MessageMetadataSpace, lane, id);
        var metadata = tx.GetRecord<MessageMetadata>(metadataKey)
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingIndexedMetadata);
        var body = StoredMessageBody.Read(tx, QueueKey(MessageBodySpace, lane, id))
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingIndexedBody);
        var current = counters ?? Counters(tx, lane);
        tx.Delete(item.Key.ToArray());
        if (leased)
        {
            current = current with
            {
                InFlightMessages = current.InFlightMessages - 1,
                InFlightBytes = current.InFlightBytes - body.Bytes
            };
        }
        var updated = DueMetadata(tx, lane, policy, now, metadata, body.Bytes, ref current);
        tx.PutRecord(metadataKey, updated);
        return current;
    }

    private static MessageMetadata DueMetadata(IAtomicTransaction tx, QueueLaneRef lane, QueuePolicy policy,
        DateTimeOffset now, MessageMetadata metadata, long bodyBytes, ref QueueCounters counters)
    {
        if (metadata.ExpiresAt <= now)
        {
            counters = counters with
            {
                StoredMessages = counters.StoredMessages - 1,
                StoredBytes = counters.StoredBytes - bodyBytes
            };
            tx.Delete(QueueKey(MessageBodySpace, lane, metadata.Id));
            return metadata with
            {
                State = MessageState.Expired,
                StateVersion = metadata.StateVersion + 1,
                LeaseOwner = null,
                LeaseUntil = null
            };
        }
        if (metadata.Attempts >= policy.MaxAttempts)
        {
            tx.PutRecord(QueueKey(DeadLetterQueueSpace, lane, metadata.Id), metadata.Id);
            return metadata with
            {
                State = MessageState.DeadLettered,
                StateVersion = metadata.StateVersion + 1,
                LeaseOwner = null,
                LeaseUntil = null,
                SafeFailureCode = AttemptsExhausted
            };
        }
        var sequence = checked(counters.NextReadySequence + 1);
        counters = counters with { NextReadySequence = sequence };
        tx.PutRecord(QueueKey(ReadyQueueSpace, lane, sequence, metadata.Id), metadata.Id);
        return metadata with
        {
            State = MessageState.Ready,
            ReadySequence = sequence,
            StateVersion = metadata.StateVersion + 1,
            LeaseOwner = null,
            LeaseUntil = null,
            NotBefore = null
        };
    }
}
