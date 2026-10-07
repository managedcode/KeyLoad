using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int RetainedPositionStep = 1;
    private const long NoRetainedBytes = 0;
    private const string PurgeTopicKind = "purgeTopic";
    private const string PurgedIdentitySuffix = "retained-digest";
    private const string InvalidTopicPurgeCut = "The topic purge cut is outside retained history.";
    private const string PinnedTopicHistory = "The topic purge cut is pinned by a subscription checkpoint.";
    private const string TopicPurgeScanExhausted = "The topic purge scan budget is exhausted.";

    private static byte[] PurgedTopicIdentityKey(EventSourceRef source, string eventId)
        => KeySpace.Partition(TopicEventIdKeySpace, source.Partition, source.Resource, source.Generation,
            eventId, PurgedIdentitySuffix);

    private MutationReceipt PurgeTopicHistory(IAtomicTransaction tx, PartitionRef partition, PurgeTopic request, TopicPurgeReadBudget? budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var source = new EventSourceRef(partition, request.Topic, EventSourceKind.Topic, Generation: request.Generation);
        SourceResource(tx, source);
        var head = ReadTopicHead(tx, source);
        if (request.ThroughPosition < head.Head.FirstAvailablePosition || request.ThroughPosition > head.Head.TailPosition
            || request.ThroughPosition == long.MaxValue)
        { throw Errors.Fail(ErrorCode.Validation, InvalidTopicPurgeCut); }
        RequireTopicPurgePins(tx, source, head.Head.TailPosition, request.ThroughPosition, budget);
        var bytes = head.StoredBytes;
        for (var position = head.Head.FirstAvailablePosition; position <= request.ThroughPosition; position++)
        {
            bytes = checked(bytes - PurgeTopicRecord(tx, source, position, budget));
        }
        if (bytes < NoRetainedBytes)
        { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
        tx.PutRecord(KeySpace.Partition(TopicHeadKeySpace, partition, request.Topic),
            new TopicHead(head.Head.TailPosition, checked(request.ThroughPosition + RetainedPositionStep), source.Generation, bytes));
        return new(PurgeTopicKind, request.Topic, request.ThroughPosition.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.ThroughPosition);
    }

    private static long PurgeTopicRecord(IAtomicTransaction tx, EventSourceRef source, long position,
        TopicPurgeReadBudget budget)
    {
        SourceEventRecord? record = null;
        var key = SourceKey(source, position);
        var removedBytes = NoRetainedBytes;
        var found = tx.ReadValue(key, value =>
        {
            record = NativeSerialization.Deserialize<SourceEventRecord>(value);
            removedBytes = value.Length;
        }, budget.ObserveBytes);
        if (!found || record is null || record.Source != source || record.Position != position)
        { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
        var identityKey = KeySpace.Partition(TopicEventIdKeySpace, source.Partition, source.Resource, source.Generation, record.Data.EventId);
        var indexedPosition = NoRetainedBytes;
        if (!tx.ReadValue(identityKey, value => indexedPosition = NativeSerialization.Deserialize<long>(value), budget.ObserveBytes)
            || indexedPosition != position)
        { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
        if (tx.ReadValue(PurgedTopicIdentityKey(source, record.Data.EventId), _ =>
            throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail), budget.ObserveBytes))
        { throw Errors.Fail(ErrorCode.Corruption, EventSourceRetainedHistoryDetail); }
        tx.PutRecord(PurgedTopicIdentityKey(source, record.Data.EventId),
            new RetainedTopicEventIdentity(JsonData.Fingerprint(record.Data), position, source.Generation));
        tx.Delete(identityKey);
        tx.Delete(key);
        return removedBytes;
    }

    private void RequireTopicPurgePins(IKeyValueView view, EventSourceRef source, long tail, long cut, TopicPurgeReadBudget budget)
    {
        var prefix = KeySpace.Partition(SubscriptionGroupsSubscriptionKeySpace, source.Partition,
            source.Resource, source.Kind.ToString(), source.StreamId, source.Generation);
        var scan = view.VisitRange(prefix, Limits.MaxScanRecords, (key, value) =>
        {
            var group = ReadTopicPurgePin(key, value, tail);
            if (cut > group.Checkpoint)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, PinnedTopicHistory); }
            return true;
        }, observer: budget.ObserveBytes);
        if (scan.HasMore)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, TopicPurgeScanExhausted); }
    }
}
