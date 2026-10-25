using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes TopicRetentionCommands operations against the caller's database context.</summary>
public static class TopicRetentionCommands
{
    internal const int RetainedPositionStep = 1;
    internal const long NoRetainedBytes = 0;
    internal const string PurgeTopicKind = "purgeTopic";
    internal const string PurgedIdentitySuffix = "retained-digest";
    internal const string InvalidTopicPurgeCut = "The topic purge cut is outside retained history.";
    internal const string PinnedTopicHistory = "The topic purge cut is pinned by a subscription checkpoint.";
    internal static byte[] PurgedTopicIdentityKey(EventSourceRef source, string eventId) => KeySpace.Partition(TopicPublishReads.TopicEventIdKeySpace, source.Partition, source.Resource, source.Generation, eventId, PurgedIdentitySuffix);
    internal static MutationReceipt PurgeTopicHistory(this DatabaseEngine engine, IAtomicTransaction tx, PartitionRef partition, PurgeTopic request)
    {
        var source = new EventSourceRef(partition, request.Topic, EventSourceKind.Topic, Generation: request.Generation);
        engine.SourceResource(tx, source);
        var head = TopicPublishReads.ReadTopicHead(tx, source);
        if (request.ThroughPosition < head.Head.FirstAvailablePosition || request.ThroughPosition > head.Head.TailPosition || request.ThroughPosition == long.MaxValue)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidTopicPurgeCut);
        }

        TopicRetentionCommands.RequireTopicPurgePins(tx, source, head.Head.TailPosition, request.ThroughPosition);
        var bytes = head.StoredBytes;
        for (var position = head.Head.FirstAvailablePosition; position <= request.ThroughPosition; position++)
        {
            bytes = checked(bytes - PurgeTopicRecord(tx, source, position));
        }

        if (bytes < NoRetainedBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
        }

        tx.PutRecord(KeySpace.Partition(TopicPublishReads.TopicHeadKeySpace, partition, request.Topic), new TopicHead(head.Head.TailPosition, checked(request.ThroughPosition + RetainedPositionStep), source.Generation, bytes));
        return new(PurgeTopicKind, request.Topic, request.ThroughPosition.ToString(System.Globalization.CultureInfo.InvariantCulture), request.ThroughPosition);
    }

    internal static long PurgeTopicRecord(IAtomicTransaction tx, EventSourceRef source, long position)
    {
        SourceEventRecord? record = null;
        var key = EventSources.SourceKey(source, position);
        var removedBytes = NoRetainedBytes;
        var found = tx.ReadValue(key, value =>
        {
            record = OrleansSerialization.Deserialize<SourceEventRecord>(value);
            removedBytes = value.Length;
        });
        if (!found || record is null || record.Source != source || record.Position != position)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
        }

        var identityKey = KeySpace.Partition(TopicPublishReads.TopicEventIdKeySpace, source.Partition, source.Resource, source.Generation, record.Data.EventId);
        var indexedPosition = NoRetainedBytes;
        if (!tx.ReadValue(identityKey, value => indexedPosition = OrleansSerialization.Deserialize<long>(value)) || indexedPosition != position)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
        }

        if (tx.ReadValue(PurgedTopicIdentityKey(source, record.Data.EventId), _ => throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail)))
        {
            throw Errors.Fail(ErrorCode.Corruption, EventSources.EventSourceRetainedHistoryDetail);
        }

        tx.PutRecord(PurgedTopicIdentityKey(source, record.Data.EventId), new RetainedTopicEventIdentity(JsonData.Fingerprint(record.Data), position, source.Generation));
        tx.Delete(identityKey);
        tx.Delete(key);
        return removedBytes;
    }

    internal static void RequireTopicPurgePins(IKeyValueView view, EventSourceRef source, long tail, long cut)
    {
        var prefix = KeySpace.Partition(SubscriptionGroups.SubscriptionGroupsSubscriptionKeySpace, source.Partition, source.Resource, source.Kind.ToString(), source.StreamId, source.Generation);
        _ = view.VisitAll(prefix, (key, value) =>
        {
            var group = EventStreamsValidation.ReadTopicPurgePin(key, value, tail);
            if (cut > group.Checkpoint)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, PinnedTopicHistory);
            }

            return true;
        });
    }
}
