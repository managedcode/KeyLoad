using System.Collections.Immutable;
using KeyLoad.Core.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes EventSources operations against the caller's database context.</summary>
public static class EventSources
{
    internal const int MinimumEventSourcePageRecords = 1;
    internal const int MinimumEventSourceAfterPosition = 0;
    internal const int UnpositionedEventSourceCursor = 0;
    internal const int RetainedSourcePositionOffset = 1;
    internal const string EventSourceRetainedHistoryDetail = "The requested event history was retained away.";
    internal const string EventSourceCursorBeyondTailDetail = "The requested position is beyond the source tail.";
    internal const int NextEventSourcePositionOffset = 1;
    internal const int EmptyEventSourceResultCount = 0;
    internal const int LastPublishedEventOffset = 1;
    internal const int LastReadPositionOffset = 1;
    internal const string SourceCursorPurpose = "event-source-page";
    internal const string SourceReadStartInvalidMessage = "The event source read budget or start position is invalid.";
    internal static Capability SourceReadCapability(EventSourceRef source) => source.Kind == EventSourceKind.Topic ? Capability.TopicsRead : Capability.EventsRead;
    internal static ResourceDefinition SourceResource(this DatabaseEngine engine, IKeyValueView view, EventSourceRef source, bool authorizedRead = false)
    {
        const int GenerationValidationBoundary = 1;
        const string SourceResourceDetailText = "The event source identity is invalid.";
        EngineResourceAccess.ValidatePartition(source.Partition);
        JsonData.Identifier(source.Resource);
        if (source.Generation < GenerationValidationBoundary || !Enum.IsDefined(source.Kind) || source.Kind == EventSourceKind.Topic && source.StreamId is not null || source.Kind == EventSourceKind.Stream && source.StreamId is null)
        {
            throw Errors.Fail(ErrorCode.Validation, SourceResourceDetailText);
        }

        if (source.StreamId is not null)
        {
            JsonData.Identifier(source.StreamId);
        }

        var kind = source.Kind == EventSourceKind.Topic ? ResourceKind.Topic : ResourceKind.StreamSet;
        return authorizedRead ? ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, source.Partition, source.Resource, kind) : engine.Resource(view, source.Partition, source.Resource, kind);
    }

    internal static EventSourceHead SourceHead(this DatabaseEngine engine, IKeyValueView view, EventSourceRef source)
    {
        var resource = engine.SourceResource(view, source);
        return SourceHead(view, source, resource);
    }

    internal static EventSourceHead SourceHead(IKeyValueView view, EventSourceRef source, ResourceDefinition resource)
    {
        const string SourceHeadSpaceText = "stream-head";
        const int TailPositionEmptyCount = 0;
        const int FirstAvailablePositionSingleItemCount = 1;
        ArgumentNullException.ThrowIfNull(resource);
        if (source.Kind == EventSourceKind.Topic)
        {
            return TopicPublishReads.ReadTopicHead(view, source).Head;
        }

        var stream = view.GetRecord<StreamHead>(KeySpace.Partition(SourceHeadSpaceText, source.Partition, source.Resource, source.StreamId));
        var head = stream is null ? new EventSourceHead(TailPositionEmptyCount, FirstAvailablePositionSingleItemCount, source.Generation) : new EventSourceHead(stream.TailRevision, stream.FirstAvailableRevision, stream.Generation);
        if (head.Generation != source.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, TopicPublishReads.StaleSourceGenerationMessage);
        }

        return head;
    }

    internal static byte[] SourceKey(EventSourceRef source, long? position = null)
    {
        const string TopicEventSpace = "topic-event";
        const string EventRecordSpace = "event";
        object?[] suffix = source.Kind == EventSourceKind.Topic ? [source.Resource, source.Generation] : [source.Resource, source.StreamId, source.Generation];
        if (position is { } value)
        {
            suffix = [.. suffix, value];
        }

        return KeySpace.Partition(source.Kind == EventSourceKind.Topic ? TopicEventSpace : EventRecordSpace, source.Partition, suffix);
    }

    internal static SourceEventRecord SourceRecord(IKeyValueView view, EventSourceRef source, long position) => SourceEventReader.Read(view, source, position);
    internal static SourceEventRecord ProjectEvent(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, SourceEventRecord record) => record with
    {
        Data = record.Data with
        {
            PayloadJson = engine.Authorization.Project(principal, resource.FieldPolicies, record.Data.PayloadJson, out _),
            HeadersJson = engine.Authorization.Project(principal, resource.HeaderPolicies, record.Data.HeadersJson, out _)
        }
    };
    internal static MutationReceipt Publish(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PublishTopic publish, DateTimeOffset now)
    {
        const int PublishAbsentCount = 0;
        var source = new EventSourceRef(partition, publish.Topic, EventSourceKind.Topic, Generation: publish.Generation);
        var resource = engine.SourceResource(tx, source);
        var topicHead = TopicPublishReads.ReadTopicHead(tx, source);
        var head = topicHead.Head;
        engine.ValidateTopicPublication(principal, resource, publish.Events);
        var headKey = KeySpace.Partition(TopicPublishReads.TopicHeadKeySpace, partition, publish.Topic);
        var sequenceKey = KeySpace.Partition(TopicPublishReads.EventSequenceKeySpace, partition);
        var sequence = tx.ReadOwnedValue(sequenceKey) is { } prior ? OrleansSerialization.Deserialize<long>(prior) : PublishAbsentCount;
        var progress = TopicPublishReads.AppendTopicEvents(tx, source, resource, publish.Events, now, head.TailPosition, sequence, head.FirstAvailablePosition, topicHead.StoredBytes);
        tx.PutRecord(headKey, new TopicHead(progress.Tail, head.FirstAvailablePosition, publish.Generation, progress.StoredBytes));
        tx.PutRecord(sequenceKey, progress.Sequence);
        return new(TopicPublishReads.PublishTopicKind, publish.Topic, publish.Events[^LastPublishedEventOffset].EventId, progress.Tail);
    }

    internal static long SourceCursorPosition(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, EventSourceRef source, string token, DateTimeOffset now)
    {
        var cursor = engine.Verify<SourceCursor>(token);
        var resource = engine.SourceResource(view, source);
        return engine.SourceCursorPosition(cursor, principal, source, now, resource);
    }

    internal static long SourceCursorPosition(this DatabaseEngine engine, PrincipalRecord principal, EventSourceRef source, string token, DateTimeOffset now, ResourceDefinition resource)
    {
        var cursor = engine.Verify<SourceCursor>(token);
        return engine.SourceCursorPosition(cursor, principal, source, now, resource);
    }

    internal static long SourceCursorPosition(this DatabaseEngine engine, SourceCursor cursor, PrincipalRecord principal, EventSourceRef source, DateTimeOffset now, ResourceDefinition resource)
    {
        const int PositionValidationBoundary = 0;
        const string SourceCursorPositionDetailText = "The event source cursor expired or changed scope.";
        if (cursor.Purpose != SourceCursorPurpose || cursor.Incarnation != engine.Store.Identity.Incarnation || cursor.Source != source || cursor.PrincipalId != principal.Id || cursor.PolicyEpoch != principal.PolicyEpoch || cursor.SchemaVersion != resource.SchemaVersion || cursor.ExpiresAt <= now || cursor.Position < PositionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.CursorExpired, SourceCursorPositionDetailText);
        }

        return cursor.Position;
    }

    /// <summary>Reads one bounded, policy-projected page from a topic or stream source.</summary>
    /// <param name = "principalId">Persisted principal requesting source-read access.</param>
    /// <param name = "request">Source, position or signed cursor, and result limit.</param>
    /// <param name = "cancellationToken">Caller cancellation for the complete bounded read.</param>
    /// <returns>The source page and its signed continuation cursor.</returns>
    /// <exception cref = "ArgumentNullException"><paramref name = "request"/> is null.</exception>
    /// <exception cref = "KeyLoadException">Authorization, retention, generation, or read budgets reject the operation.</exception>

    /// <param name = "engine">The shared database execution context.</param>
    public static EventSourcePage ReadEventSource(this DatabaseEngine engine, string principalId, ReadEventSourceRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(engine.Clock, cancellationToken);
        budget.Check();
        return engine.Store.Read(view => engine.ReadEventSource(view, principalId, request, budget));
    }

    internal static EventSourcePage ReadEventSource(this DatabaseEngine engine, IKeyValueView gatedView, string principalId, ReadEventSourceRequest request, ReadExecutionBudget budget)
    {
        if (request.Limit is < MinimumEventSourcePageRecords || request.AfterPosition < MinimumEventSourceAfterPosition || request.Cursor is not null && request.AfterPosition != UnpositionedEventSourceCursor)
        {
            throw Errors.Fail(ErrorCode.Validation, SourceReadStartInvalidMessage);
        }

        var view = budget.CreateView(gatedView);
        var now = engine.Clock.GetUtcNow();
        var principal = engine.Principal(view, principalId, now);
        engine.Authorization.Require(principal, request.Source.Partition, request.Source.Resource, SourceReadCapability(request.Source));
        var resource = engine.SourceResource(view, request.Source, authorizedRead: true);
        var head = SourceHead(view, request.Source, resource);
        var after = request.Cursor is null ? request.AfterPosition : engine.SourceCursorPosition(principal, request.Source, request.Cursor, now, resource);
        if (after < head.FirstAvailablePosition - RetainedSourcePositionOffset)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, EventSourceRetainedHistoryDetail);
        }

        if (after > head.TailPosition)
        {
            throw Errors.Fail(ErrorCode.Validation, EventSourceCursorBeyondTailDetail);
        }

        var records = new List<SourceEventRecord>();
        var position = after;
        while (position < head.TailPosition && records.Count < request.Limit)
        {
            position = checked(position + NextEventSourcePositionOffset);
            budget.Check();
            var record = engine.ProjectEvent(principal, resource, SourceRecord(view, request.Source, position));
            records.Add(record);
        }

        var last = records.Count == EmptyEventSourceResultCount ? after : records[^LastReadPositionOffset].Position;
        var token = engine.Sign(new SourceCursor(SourceCursorPurpose, engine.Store.Identity.Incarnation, request.Source, principal.Id, principal.PolicyEpoch, resource.SchemaVersion, last, now.Add(engine.eventSourceCursorLifetime)));
        var page = new EventSourcePage(request.Source, head, records.ToImmutableArray(), token, engine.Store.Position, last < head.TailPosition);
        return page;
    }
}
