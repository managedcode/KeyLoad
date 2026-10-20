using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes EventStreamsCommandsTopicRetentionCommands operations against the caller's database context.</summary>
public static class EventStreamsCommandsTopicRetentionCommands
{
    internal const int RetainedPositionStep = 1;
    internal const long NoRetainedBytes = 0;
    internal const string PurgeTopicKind = "purgeTopic";
    internal const string PurgedIdentitySuffix = "retained-digest";
    internal const string InvalidTopicPurgeCut = "The topic purge cut is outside retained history.";
    internal const string PinnedTopicHistory = "The topic purge cut is pinned by a subscription checkpoint.";
    internal const string TopicPurgeScanExhausted = "The topic purge scan budget is exhausted.";
    internal static byte[] PurgedTopicIdentityKey(EventSourceRef source, string eventId) => KeySpace.Partition(MessagingCommandsTopicPublishReads.TopicEventIdKeySpace, source.Partition, source.Resource, source.Generation, eventId, PurgedIdentitySuffix);
    internal static MutationReceipt PurgeTopicHistory(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, PurgeTopic request, TopicPurgeReadBudget? budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var source = new EventSourceRef(partition, request.Topic, EventSourceKind.Topic, Generation: request.Generation);
        engine.SourceResource(tx, source);
        var head = MessagingCommandsTopicPublishReads.ReadTopicHead(tx, source);
        if (request.ThroughPosition < head.Head.FirstAvailablePosition || request.ThroughPosition > head.Head.TailPosition || request.ThroughPosition == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidTopicPurgeCut);
        }

        engine.RequireTopicPurgePins(tx, source, head.Head.TailPosition, request.ThroughPosition, budget);
        var bytes = head.StoredBytes;
        for (var position = head.Head.FirstAvailablePosition; position <= request.ThroughPosition; position++)
        {
            bytes = checked(bytes - PurgeTopicRecord(tx, source, position, budget));
        }

        if (bytes < NoRetainedBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventStreamsExecutionEventSources.EventSourceRetainedHistoryDetail);
        }

        tx.PutRecord(KeySpace.Partition(MessagingCommandsTopicPublishReads.TopicHeadKeySpace, partition, request.Topic), new TopicHead(head.Head.TailPosition, checked(request.ThroughPosition + RetainedPositionStep), source.Generation, bytes));
        return new(PurgeTopicKind, request.Topic, request.ThroughPosition.ToString(System.Globalization.CultureInfo.InvariantCulture), request.ThroughPosition);
    }

    internal static long PurgeTopicRecord(IAtomicTransaction tx, EventSourceRef source, long position, TopicPurgeReadBudget budget)
    {
        SourceEventRecord? record = null;
        var key = EventStreamsExecutionEventSources.SourceKey(source, position);
        var removedBytes = NoRetainedBytes;
        var found = tx.ReadValue(key, value =>
        {
            record = OrleansSerialization.Deserialize<SourceEventRecord>(value);
            removedBytes = value.Length;
        }, budget.ObserveBytes);
        if (!found || record is null || record.Source != source || record.Position != position)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventStreamsExecutionEventSources.EventSourceRetainedHistoryDetail);
        }

        var identityKey = KeySpace.Partition(MessagingCommandsTopicPublishReads.TopicEventIdKeySpace, source.Partition, source.Resource, source.Generation, record.Data.EventId);
        var indexedPosition = NoRetainedBytes;
        if (!tx.ReadValue(identityKey, value => indexedPosition = OrleansSerialization.Deserialize<long>(value), budget.ObserveBytes) || indexedPosition != position)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventStreamsExecutionEventSources.EventSourceRetainedHistoryDetail);
        }

        if (tx.ReadValue(PurgedTopicIdentityKey(source, record.Data.EventId), _ => throw Errors.Fail(ErrorCode.Corruption, EventStreamsExecutionEventSources.EventSourceRetainedHistoryDetail), budget.ObserveBytes))
        {
            throw Errors.Fail(ErrorCode.Corruption, EventStreamsExecutionEventSources.EventSourceRetainedHistoryDetail);
        }

        tx.PutRecord(PurgedTopicIdentityKey(source, record.Data.EventId), new RetainedTopicEventIdentity(JsonData.Fingerprint(record.Data), position, source.Generation));
        tx.Delete(identityKey);
        tx.Delete(key);
        return removedBytes;
    }

    internal static void RequireTopicPurgePins(this DatabaseEngine engine, IKeyValueView view, EventSourceRef source, long tail, long cut, TopicPurgeReadBudget budget)
    {
        var prefix = KeySpace.Partition(MessagingExecutionSubscriptionGroups.SubscriptionGroupsSubscriptionKeySpace, source.Partition, source.Resource, source.Kind.ToString(), source.StreamId, source.Generation);
        var scan = view.VisitRange(prefix, engine.Limits.MaxScanRecords, (key, value) =>
        {
            var group = EventStreamsValidation.ReadTopicPurgePin(key, value, tail);
            if (cut > group.Checkpoint)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, PinnedTopicHistory);
            }

            return true;
        }, observer: budget.ObserveBytes);
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, TopicPurgeScanExhausted);
        }
    }
}
