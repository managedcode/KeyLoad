using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int TopicPublishReadsInitialSequence = 0;
    private const int TopicPublishReadsSingleElementCount = 1;
    private const int TopicPublishReadsNoRetainedBytes = 0;
    private const int TopicPublishReadsMinimumPositiveCount = 1;
    private const int TopicPublishReadsVersionOne = 1;
    private const int TopicPublishReadsAdjacentElementOffset = 1;

    private const string TopicHeadKeySpace = "topic-head";
    private const string EventSequenceKeySpace = "event-sequence";
    private const string TopicEventIdKeySpace = "topic-event-id";
    private const string PublishTopicKind = "publishTopic";
    private const string PausedTopicMessage = "This topic is paused.";
    private const string InvalidTopicEventCountMessage = "The topic event count is invalid.";
    private const string InvalidEventSchemaVersionMessage = "The event schema version is invalid.";
    private const string TopicQuotaExhaustedMessage = "The retained topic quota is exhausted.";
    private const string StaleSourceGenerationMessage = "The event source generation is stale.";

    private static TopicHeadSnapshot ReadTopicHead(IKeyValueView view, EventSourceRef source)
    {
        var topic = view.GetRecord<TopicHead>(KeySpace.Partition(TopicHeadKeySpace, source.Partition, source.Resource));
        var head = topic is null
            ? new EventSourceHead(TopicPublishReadsInitialSequence, TopicPublishReadsSingleElementCount, source.Generation)
            : new EventSourceHead(topic.TailPosition, topic.FirstAvailablePosition, topic.Generation);
        if (head.Generation != source.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, StaleSourceGenerationMessage);
        }

        return new(head, topic?.StoredBytes ?? TopicPublishReadsNoRetainedBytes);
    }

    private void ValidateTopicPublication(PrincipalRecord principal, ResourceDefinition resource, ImmutableArray<EventData> events)
    {
        if (resource.Paused)
        {
            throw Errors.Fail(ErrorCode.DispatchPaused, PausedTopicMessage);
        }
        if (events.Length < TopicPublishReadsMinimumPositiveCount || events.Length > messagingExecution.MaximumTopicEvents)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidTopicEventCountMessage);
        }
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }
        foreach (var policy in resource.HeaderPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }
    }

    private TopicPublicationProgress AppendTopicEvents(IAtomicTransaction tx, EventSourceRef source,
        ResourceDefinition resource, ImmutableArray<EventData> events, DateTimeOffset now, long tail, long sequence,
        long firstAvailablePosition, long storedBytes)
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
                PayloadJson = JsonData.Validate(item.PayloadJson, Limits),
                HeadersJson = JsonData.Validate(item.HeadersJson, Limits)
            };
            if (tx.ReadOwnedValue(idKey) is { } retained)
            {
                RejectDuplicateEvent(SourceRecord(tx, source, NativeSerialization.Deserialize<long>(retained)).Data, data);
            }

            var record = new SourceEventRecord(source, checked(++tail), checked(++sequence), data, now);
            var payload = NativeSerialization.Serialize(record);
            storedBytes = checked(storedBytes + payload.LongLength);
            if (tail - firstAvailablePosition + TopicPublishReadsAdjacentElementOffset > resource.EventRetention.MaxEvents
                || storedBytes > resource.EventRetention.MaxBytes)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, TopicQuotaExhaustedMessage);
            }

            tx.Put(SourceKey(source, tail), payload);
            tx.PutRecord(idKey, tail);
        }

        return new(tail, sequence, storedBytes);
    }
}
