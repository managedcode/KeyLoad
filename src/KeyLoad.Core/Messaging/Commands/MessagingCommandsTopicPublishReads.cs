using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes MessagingCommandsTopicPublishReads operations against the caller's database context.</summary>
public static class MessagingCommandsTopicPublishReads
{
    internal const int TopicPublishReadsInitialSequence = 0;
    internal const int TopicPublishReadsSingleElementCount = 1;
    internal const int TopicPublishReadsNoRetainedBytes = 0;
    internal const int TopicPublishReadsMinimumPositiveCount = 1;
    internal const int TopicPublishReadsVersionOne = 1;
    internal const int TopicPublishReadsAdjacentElementOffset = 1;
    internal const string TopicHeadKeySpace = "topic-head";
    internal const string EventSequenceKeySpace = "event-sequence";
    internal const string TopicEventIdKeySpace = "topic-event-id";
    internal const int RetainedTopicDigestCharacters = 64;
    internal const string PublishTopicKind = "publishTopic";
    internal const string PausedTopicMessage = "This topic is paused.";
    internal const string InvalidTopicEventCountMessage = "The topic event count is invalid.";
    internal const string InvalidEventSchemaVersionMessage = "The event schema version is invalid.";
    internal const string TopicQuotaExhaustedMessage = "The retained topic quota is exhausted.";
    internal const string StaleSourceGenerationMessage = "The event source generation is stale.";
    internal static TopicHeadSnapshot ReadTopicHead(IKeyValueView view, EventSourceRef source)
    {
        var topic = view.GetRecord<TopicHead>(KeySpace.Partition(TopicHeadKeySpace, source.Partition, source.Resource));
        var head = topic is null ? new EventSourceHead(TopicPublishReadsInitialSequence, TopicPublishReadsSingleElementCount, source.Generation) : new EventSourceHead(topic.TailPosition, topic.FirstAvailablePosition, topic.Generation);
        if (head.Generation != source.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, StaleSourceGenerationMessage);
        }

        return new(head, topic?.StoredBytes ?? TopicPublishReadsNoRetainedBytes);
    }

    internal static void ValidateTopicPublication(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, ImmutableArray<EventData> events)
    {
        if (resource.Paused)
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, PausedTopicMessage);
        }

        if (events.Length < TopicPublishReadsMinimumPositiveCount || events.Length > engine.MessagingExecution.MaximumTopicEvents)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidTopicEventCountMessage);
        }

        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        foreach (var policy in resource.HeaderPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
    }

    internal static TopicPublicationProgress AppendTopicEvents(this DatabaseEngine engine, IAtomicTransaction tx, EventSourceRef source, ResourceDefinition resource, ImmutableArray<EventData> events, DateTimeOffset now, long tail, long sequence, long firstAvailablePosition, long storedBytes)
    {
        foreach (var item in events)
        {
            JsonData.Identifier(item.EventId);
            JsonData.Identifier(item.EventType);
            if (item.SchemaVersion < TopicPublishReadsVersionOne)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidEventSchemaVersionMessage);
            }

            var idKey = KeySpace.Partition(TopicEventIdKeySpace, source.Partition, source.Resource, source.Generation, item.EventId);
            var data = item with
            {
                PayloadJson = JsonData.Validate(item.PayloadJson, engine.Limits),
                HeadersJson = JsonData.Validate(item.HeadersJson, engine.Limits)
            };
            if (tx.ReadOwnedValue(EventStreamsCommandsTopicRetentionCommands.PurgedTopicIdentityKey(source, item.EventId)) is { } purgedBytes)
            {
                var purged = EventStreamsValidation.ReadPurgedTopicIdentity(purgedBytes, source, firstAvailablePosition);
                throw JsonData.Fingerprint(data) == purged.ContentDigest ? Errors.Fail(ErrorCode.DuplicateEventId, EventStreamsExecution.DuplicateEventDetail) : Errors.Fail(ErrorCode.Conflict, EventStreamsExecution.ConflictingEventContentDetail);
            }

            if (tx.ReadOwnedValue(idKey) is { } retained)
            {
                EventStreamsExecution.RejectDuplicateEvent(EventStreamsExecutionEventSources.SourceRecord(tx, source, OrleansSerialization.Deserialize<long>(retained)).Data, data);
            }

            var record = new SourceEventRecord(source, checked(++tail), checked(++sequence), data, now);
            var payload = OrleansSerialization.Serialize(record);
            storedBytes = checked(storedBytes + payload.LongLength);
            if (tail - firstAvailablePosition + TopicPublishReadsAdjacentElementOffset > resource.EventRetention.MaxEvents || storedBytes > resource.EventRetention.MaxBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, TopicQuotaExhaustedMessage);
            }

            tx.Put(EventStreamsExecutionEventSources.SourceKey(source, tail), payload);
            tx.PutRecord(idKey, tail);
        }

        return new(tail, sequence, storedBytes);
    }
}
