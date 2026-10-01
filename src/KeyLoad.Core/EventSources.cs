using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private sealed record TopicHead(long TailPosition, long FirstAvailablePosition, long Generation, long StoredBytes);
    private sealed record SourceCursor(string Purpose, Guid Incarnation, EventSourceRef Source, string PrincipalId,
        long PolicyEpoch, long SchemaVersion, long Position, DateTimeOffset ExpiresAt);
    private static Capability SourceReadCapability(EventSourceRef source) => source.Kind == EventSourceKind.Topic ? Capability.TopicsRead : Capability.EventsRead;
    private ResourceDefinition SourceResource(IKeyValueView view, EventSourceRef source)
    {
        ValidatePartition(source.Partition); JsonData.Identifier(source.Resource);
        if (source.Generation < 1 || !Enum.IsDefined(source.Kind)
            || source.Kind == EventSourceKind.Topic && source.StreamId is not null
            || source.Kind == EventSourceKind.Stream && source.StreamId is null)
            throw Errors.Fail(ErrorCode.Validation, "The event source identity is invalid.");
        if (source.StreamId is not null) JsonData.Identifier(source.StreamId);
        return Resource(view, source.Partition, source.Resource, source.Kind == EventSourceKind.Topic ? ResourceKind.Topic : ResourceKind.StreamSet);
    }
    private EventSourceHead SourceHead(IKeyValueView view, EventSourceRef source)
    {
        SourceResource(view, source);
        EventSourceHead head;
        if (source.Kind == EventSourceKind.Topic)
        {
            var topic = view.GetRecord<TopicHead>(KeySpace.Partition("topic-head", source.Partition, source.Resource));
            head = topic is null ? new(0, 1, source.Generation) : new(topic.TailPosition, topic.FirstAvailablePosition, topic.Generation);
        }
        else
        {
            var stream = view.GetRecord<StreamHead>(KeySpace.Partition("stream-head", source.Partition, source.Resource, source.StreamId));
            head = stream is null ? new(0, 1, source.Generation) : new(stream.TailRevision, stream.FirstAvailableRevision, stream.Generation);
        }
        if (head.Generation != source.Generation) throw Errors.Fail(ErrorCode.TokenInvalidated, "The event source generation is stale.");
        return head;
    }
    private static byte[] SourceKey(EventSourceRef source, long? position = null)
    {
        object?[] suffix = source.Kind == EventSourceKind.Topic ? [source.Resource, source.Generation] : [source.Resource, source.StreamId, source.Generation];
        if (position is { } value) suffix = [.. suffix, value];
        return KeySpace.Partition(source.Kind == EventSourceKind.Topic ? "topic-event" : "event", source.Partition, suffix);
    }
    private SourceEventRecord SourceRecord(IKeyValueView view, EventSourceRef source, long position)
    {
        var bytes = view.Get(SourceKey(source, position)) ?? throw Errors.Fail(ErrorCode.HistoryUnavailable, "The requested event is unavailable.");
        if (source.Kind == EventSourceKind.Topic) return JsonDefaults.Deserialize<SourceEventRecord>(bytes);
        var record = JsonDefaults.Deserialize<EventRecord>(bytes);
        return new(source, record.Revision, record.EventSequence, record.Data, record.RecordedAt);
    }
    private SourceEventRecord ProjectEvent(PrincipalRecord principal, ResourceDefinition resource, SourceEventRecord record) => record with
    {
        Data = record.Data with
        {
            PayloadJson = Authorization.Project(principal, resource.FieldPolicies, record.Data.PayloadJson, out _),
            HeadersJson = Authorization.Project(principal, resource.HeaderPolicies, record.Data.HeadersJson, out _)
        }
    };
    private MutationReceipt Publish(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PublishTopic publish, DateTimeOffset now)
    {
        var source = new EventSourceRef(partition, publish.Topic, EventSourceKind.Topic, Generation: publish.Generation);
        var resource = SourceResource(tx, source); var head = SourceHead(tx, source);
        if (resource.Paused) throw Errors.Fail(ErrorCode.DispatchPaused, "This topic is paused.");
        if (publish.Events.Length is < 1 or > 256) throw Errors.Fail(ErrorCode.Validation, "The topic event count is invalid.");
        foreach (var policy in resource.FieldPolicies) Authorization.RequireFieldWrite(principal, resource, policy.Path);
        foreach (var policy in resource.HeaderPolicies) Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        var headKey = KeySpace.Partition("topic-head", partition, publish.Topic);
        var bytes = tx.GetRecord<TopicHead>(headKey)?.StoredBytes ?? 0;
        var tail = head.TailPosition;
        var sequenceKey = KeySpace.Partition("event-sequence", partition);
        var sequence = tx.Get(sequenceKey) is { } prior ? JsonDefaults.Deserialize<long>(prior) : 0;
        foreach (var item in publish.Events)
        {
            JsonData.Identifier(item.EventId); JsonData.Identifier(item.EventType);
            if (item.SchemaVersion < 1) throw Errors.Fail(ErrorCode.Validation, "The event schema version is invalid.");
            var idKey = KeySpace.Partition("topic-event-id", partition, publish.Topic, publish.Generation, item.EventId);
            var data = item with { PayloadJson = JsonData.Validate(item.PayloadJson, Limits), HeadersJson = JsonData.Validate(item.HeadersJson, Limits) };
            if (tx.Get(idKey) is { } retained)
                RejectDuplicateEvent(SourceRecord(tx, source, JsonDefaults.Deserialize<long>(retained)).Data, data);
            var record = new SourceEventRecord(source, checked(++tail), checked(++sequence), data, now);
            var payload = JsonDefaults.Serialize(record); bytes = checked(bytes + payload.LongLength);
            if (tail - head.FirstAvailablePosition + 1 > resource.EventRetention.MaxEvents || bytes > resource.EventRetention.MaxBytes)
                throw Errors.Fail(ErrorCode.ResourceExhausted, "The retained topic quota is exhausted.");
            tx.Put(SourceKey(source, tail), payload); tx.PutRecord(idKey, tail);
        }
        tx.PutRecord(headKey, new TopicHead(tail, head.FirstAvailablePosition, publish.Generation, bytes));
        tx.PutRecord(sequenceKey, sequence);
        return new("publishTopic", publish.Topic, publish.Events[^1].EventId, tail);
    }
    private long SourceCursorPosition(IKeyValueView view, PrincipalRecord principal, EventSourceRef source, string token, DateTimeOffset now)
    {
        var cursor = Verify<SourceCursor>(token); var resource = SourceResource(view, source);
        if (cursor.Purpose != "event-source-page" || cursor.Incarnation != Store.Identity.Incarnation || cursor.Source != source
            || cursor.PrincipalId != principal.Id || cursor.PolicyEpoch != principal.PolicyEpoch || cursor.SchemaVersion != resource.SchemaVersion
            || cursor.ExpiresAt <= now || cursor.Position < 0)
            throw Errors.Fail(ErrorCode.CursorExpired, "The event source cursor expired or changed scope.");
        return cursor.Position;
    }
    public EventSourcePage ReadEventSource(string principalId, ReadEventSourceRequest request) => Store.Read(view =>
    {
        if (request.Limit is < 1 || request.Limit > Limits.MaxResults || request.AfterPosition < 0
            || request.Cursor is not null && request.AfterPosition != 0)
            throw Errors.Fail(ErrorCode.Validation, "The event source read budget or start position is invalid.");
        var now = DateTimeOffset.UtcNow; var principal = Principal(view, principalId, now);
        Authorization.Require(principal, request.Source.Partition, request.Source.Resource, SourceReadCapability(request.Source));
        var resource = SourceResource(view, request.Source);
        var head = SourceHead(view, request.Source);
        var after = request.Cursor is null ? request.AfterPosition : SourceCursorPosition(view, principal, request.Source, request.Cursor, now);
        if (after < head.FirstAvailablePosition - 1) throw Errors.Fail(ErrorCode.HistoryUnavailable, "The requested event history was retained away.");
        if (after > head.TailPosition) throw Errors.Fail(ErrorCode.Validation, "The requested position is beyond the source tail.");
        var records = new List<SourceEventRecord>(); long bytes = 0;
        for (var position = after + 1; position <= head.TailPosition && records.Count < request.Limit; position++)
        {
            var record = ProjectEvent(principal, resource, SourceRecord(view, request.Source, position));
            bytes += JsonDefaults.Serialize(record).LongLength;
            if (bytes > Limits.MaxBatchBytes) throw Errors.Fail(ErrorCode.BudgetExceeded, "The event source result exceeds its byte budget.");
            records.Add(record);
        }
        var last = records.Count == 0 ? after : records[^1].Position;
        var token = Sign(new SourceCursor("event-source-page", Store.Identity.Incarnation, request.Source, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion, last, now.AddHours(24)));
        return new EventSourcePage(request.Source, head, records.ToArray(), token, Store.Position, last < head.TailPosition);
    });
}
