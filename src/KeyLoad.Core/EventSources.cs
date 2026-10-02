using System.Collections.Immutable;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private sealed record TopicHead(long TailPosition, long FirstAvailablePosition, long Generation, long StoredBytes);
    private sealed record SourceCursor(string Purpose, Guid Incarnation, EventSourceRef Source, string PrincipalId,
        long PolicyEpoch, long SchemaVersion, long Position, DateTimeOffset ExpiresAt);
    private const string SourceCursorPurpose = "event-source-page";
    private const string SourceReadStartInvalidMessage = "The event source read budget or start position is invalid.";
    private const string SourceReadResultExceededMessage = "The event source result exceeds its byte budget.";
    private static readonly TimeSpan SourceCursorLifetime = TimeSpan.FromHours(24);
    private static Capability SourceReadCapability(EventSourceRef source) => source.Kind == EventSourceKind.Topic ? Capability.TopicsRead : Capability.EventsRead;
    private ResourceDefinition SourceResource(IKeyValueView view, EventSourceRef source)
    {
        ValidatePartition(source.Partition);
        JsonData.Identifier(source.Resource);
        if (source.Generation < 1 || !Enum.IsDefined(source.Kind)
            || source.Kind == EventSourceKind.Topic && source.StreamId is not null
            || source.Kind == EventSourceKind.Stream && source.StreamId is null)
        {
            throw Errors.Fail(ErrorCode.Validation, "The event source identity is invalid.");
        }

        if (source.StreamId is not null)
        {
            JsonData.Identifier(source.StreamId);
        }

        return Resource(view, source.Partition, source.Resource, source.Kind == EventSourceKind.Topic ? ResourceKind.Topic : ResourceKind.StreamSet);
    }
    private EventSourceHead SourceHead(IKeyValueView view, EventSourceRef source)
    {
        var resource = SourceResource(view, source);
        return SourceHead(view, source, resource);
    }
    private static EventSourceHead SourceHead(IKeyValueView view, EventSourceRef source, ResourceDefinition resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        if (source.Kind == EventSourceKind.Topic)
        {
            return ReadTopicHead(view, source).Head;
        }
        var stream = view.GetRecord<StreamHead>(KeySpace.Partition("stream-head", source.Partition, source.Resource, source.StreamId));
        var head = stream is null ? new EventSourceHead(0, 1, source.Generation)
            : new EventSourceHead(stream.TailRevision, stream.FirstAvailableRevision, stream.Generation);
        if (head.Generation != source.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, StaleSourceGenerationMessage);
        }
        return head;
    }
    private static byte[] SourceKey(EventSourceRef source, long? position = null)
    {
        object?[] suffix = source.Kind == EventSourceKind.Topic ? [source.Resource, source.Generation] : [source.Resource, source.StreamId, source.Generation];
        if (position is { } value)
        {
            suffix = [.. suffix, value];
        }

        return KeySpace.Partition(source.Kind == EventSourceKind.Topic ? "topic-event" : "event", source.Partition, suffix);
    }
    private static SourceEventRecord SourceRecord(IKeyValueView view, EventSourceRef source, long position)
        => SourceEventReader.Read(view, source, position);
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
        var resource = SourceResource(tx, source);
        var topicHead = ReadTopicHead(tx, source);
        var head = topicHead.Head;
        ValidateTopicPublication(principal, resource, publish.Events);
        var headKey = KeySpace.Partition(TopicHeadKeySpace, partition, publish.Topic);
        var sequenceKey = KeySpace.Partition(EventSequenceKeySpace, partition);
        var sequence = tx.ReadOwnedValue(sequenceKey) is { } prior ? JsonDefaults.Deserialize<long>(prior) : 0;
        var progress = AppendTopicEvents(tx, source, resource, publish.Events, now,
            head.TailPosition, sequence, head.FirstAvailablePosition, topicHead.StoredBytes);
        tx.PutRecord(headKey, new TopicHead(progress.Tail, head.FirstAvailablePosition, publish.Generation, progress.StoredBytes));
        tx.PutRecord(sequenceKey, progress.Sequence);
        return new(PublishTopicKind, publish.Topic, publish.Events[^1].EventId, progress.Tail);
    }
    private long SourceCursorPosition(IKeyValueView view, PrincipalRecord principal, EventSourceRef source, string token, DateTimeOffset now)
    {
        var cursor = Verify<SourceCursor>(token);
        var resource = SourceResource(view, source);
        return SourceCursorPosition(cursor, principal, source, now, resource);
    }
    private long SourceCursorPosition(PrincipalRecord principal, EventSourceRef source,
        string token, DateTimeOffset now, ResourceDefinition resource)
    {
        var cursor = Verify<SourceCursor>(token);
        return SourceCursorPosition(cursor, principal, source, now, resource);
    }
    private long SourceCursorPosition(SourceCursor cursor, PrincipalRecord principal, EventSourceRef source,
        DateTimeOffset now, ResourceDefinition resource)
    {
        if (cursor.Purpose != SourceCursorPurpose || cursor.Incarnation != Store.Identity.Incarnation || cursor.Source != source
            || cursor.PrincipalId != principal.Id || cursor.PolicyEpoch != principal.PolicyEpoch || cursor.SchemaVersion != resource.SchemaVersion
            || cursor.ExpiresAt <= now || cursor.Position < 0)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, "The event source cursor expired or changed scope.");
        }
        return cursor.Position;
    }
    /// <summary>Reads one bounded, policy-projected page from a topic or stream source.</summary>
    /// <param name="principalId">Persisted principal requesting source-read access.</param>
    /// <param name="request">Source, position or signed cursor, and result limit.</param>
    /// <param name="cancellationToken">Caller cancellation for the complete bounded read.</param>
    /// <returns>The source page and its signed continuation cursor.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    /// <exception cref="KeyLoadException">Authorization, retention, generation, or read budgets reject the operation.</exception>
    public EventSourcePage ReadEventSource(string principalId, ReadEventSourceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(Limits, Clock, cancellationToken);
        budget.Check();
        return Store.Read(view => ReadEventSource(view, principalId, request, budget));
    }
    private EventSourcePage ReadEventSource(IKeyValueView gatedView, string principalId, ReadEventSourceRequest request,
        ReadExecutionBudget budget)
    {
        if (request.Limit is < 1 || request.Limit > Limits.MaxResults || request.AfterPosition < 0
            || request.Cursor is not null && request.AfterPosition != 0)
        {
            throw Errors.Fail(ErrorCode.Validation, SourceReadStartInvalidMessage);
        }
        var view = budget.CreateView(gatedView);
        var now = Clock.GetUtcNow();
        var principal = Principal(view, principalId, now);
        Authorization.Require(principal, request.Source.Partition, request.Source.Resource, SourceReadCapability(request.Source));
        var resource = SourceResource(view, request.Source);
        var head = SourceHead(view, request.Source, resource);
        var after = request.Cursor is null ? request.AfterPosition
            : SourceCursorPosition(principal, request.Source, request.Cursor, now, resource);
        if (after < head.FirstAvailablePosition - 1)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, "The requested event history was retained away.");
        }
        if (after > head.TailPosition)
        {
            throw Errors.Fail(ErrorCode.Validation, "The requested position is beyond the source tail.");
        }
        var records = new List<SourceEventRecord>();
        var projectedBytes = 0L;
        var position = after;
        while (position < head.TailPosition && records.Count < request.Limit)
        {
            position = checked(position + 1);
            budget.Check();
            var record = ProjectEvent(principal, resource, SourceRecord(view, request.Source, position));
            var eventBytes = budget.MeasureResult(record);
            if (eventBytes > Limits.MaxBatchBytes - projectedBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, SourceReadResultExceededMessage);
            }
            records.Add(record);
            projectedBytes += eventBytes;
        }
        var last = records.Count == 0 ? after : records[^1].Position;
        var token = Sign(new SourceCursor(SourceCursorPurpose, Store.Identity.Incarnation, request.Source, principal.Id, principal.PolicyEpoch,
            resource.SchemaVersion, last, now.Add(SourceCursorLifetime)));
        var page = new EventSourcePage(request.Source, head, records.ToImmutableArray(), token, Store.Position, last < head.TailPosition);
        budget.CheckResult(page);
        return page;
    }
}
