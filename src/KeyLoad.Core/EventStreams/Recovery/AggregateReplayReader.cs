using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class AggregateReplayReader
{
    internal const string StreamHeadKeySpace = "stream-head";
    private const string EventSpace = "event";
    private const string EventIdentitySpace = "event-id";
    private const string InvalidReplayLimitMessage = "The aggregate replay limit is invalid.";
    private const string InvalidReplayIdentityMessage = "The aggregate replay identity or state schema is invalid.";
    private const string ReplayGenerationStaleMessage = "The aggregate replay stream generation is stale.";
    private const string SnapshotStreamMismatchMessage = "The aggregate snapshot stream identity is corrupt.";
    private const string SnapshotAheadMessage = "The aggregate snapshot source is ahead of the stream tail.";
    private const string SnapshotVersionsMismatchMessage = "The aggregate snapshot reducer or schema is incompatible.";
    private const string InvalidStreamHeadMessage = "The aggregate replay stream head is corrupt.";
    private const string InvalidEventRecordMessage = "The aggregate replay event sequence is corrupt.";
    private const string ReplayHistoryUnavailableMessage = "The aggregate replay requires unavailable history.";
    private const string ReplayEventsExceededMessage = "The complete aggregate replay tail exceeds its event budget.";
    private const string ReplayTailCorruptMessage = "The aggregate replay tail is incomplete or corrupt.";

    internal static AggregateReplayPage Read(DatabaseEngine database, IKeyValueView view, string principalId,
        ReadAggregateReplayRequest request, ReadExecutionBudget budget)
    {
        const int ReadAbsentCount = 0;

        ValidateRequest(request);
        var metadataView = budget.CreateView(view);
        var principal = database.Principal(metadataView, principalId, database.EvaluationClock.GetUtcNow());
        database.Authorization.Require(principal, request.Stream.Partition, request.Stream.StreamSet,
            Capability.EventsReplay | Capability.EventsRead);
        var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(metadataView, request.Stream.Partition, request.Stream.StreamSet, ResourceKind.StreamSet);
        database.Authorization.RequireReplayInput(principal, resource);
        var head = ReadHead(view, request.Stream, budget);
        ValidateHead(head);
        if (head.Generation != request.Stream.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ReplayGenerationStaleMessage);
        }

        var snapshot = request.FromBeginning ? null : (ReadSnapshot(view, request.Stream, budget)).Value;
        var startRevision = snapshot is null ? ReadAbsentCount : ValidateSnapshot(snapshot, request, head);
        ValidateRetainedStart(startRevision, head);
        var events = ReadTail(view, request.Stream, head, startRevision, request.MaximumEvents, budget);
        var page = new AggregateReplayPage(request.Stream, head, snapshot, events, database.Store.Position);
        budget.CheckResult(page);
        return page;
    }

    private static void ValidateRequest(ReadAggregateReplayRequest request)
    {
        const int GenerationValidationBoundary = 1;
        const int StateSchemaVersionValidationBoundary = 1;
        const int MaximumEventsValidationBoundary = 1;

        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Stream);
        EngineResourceAccess.ValidatePartition(request.Stream.Partition);
        JsonData.Identifier(request.Stream.StreamSet);
        JsonData.Identifier(request.Stream.StreamId);
        JsonData.Identifier(request.ReducerVersion);
        if (request.Stream.Generation < GenerationValidationBoundary || request.StateSchemaVersion < StateSchemaVersionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidReplayIdentityMessage);
        }
        if (request.MaximumEvents < MaximumEventsValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidReplayLimitMessage);
        }
    }

    private static StreamHead ReadHead(IKeyValueView view, StreamRef stream, ReadExecutionBudget budget)
        => budget.ReadRecord<StreamHead>(view,
            KeySpace.Partition(StreamHeadKeySpace, stream.Partition, stream.StreamSet, stream.StreamId))
            ?? new StreamHead(0, 1, 1);

    private static (bool HasValue, AggregateSnapshotState? Value) ReadSnapshot(IKeyValueView view, StreamRef stream,
        ReadExecutionBudget budget)
        => (budget.Read(view, AggregateSnapshotPersistence.Key(stream)) is { } bytes
            ? AggregateSnapshotPersistence.Deserialize(bytes)
            : null) is { } outcomeValue1 ? (true, outcomeValue1) : (false, default(AggregateSnapshotState?));

    private static long ValidateSnapshot(AggregateSnapshotState snapshot, ReadAggregateReplayRequest request,
        StreamHead head)
    {
        if (snapshot.Stream != request.Stream)
        {
            throw Errors.Fail(ErrorCode.Corruption, SnapshotStreamMismatchMessage);
        }
        if (snapshot.SourceRevision > head.TailRevision)
        {
            throw Errors.Fail(ErrorCode.Corruption, SnapshotAheadMessage);
        }
        if (snapshot.ReducerVersion != request.ReducerVersion
            || snapshot.StateSchemaVersion != request.StateSchemaVersion)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, SnapshotVersionsMismatchMessage);
        }
        return snapshot.SourceRevision;
    }

    private static void ValidateRetainedStart(long revision, StreamHead head)
    {
        const int FirstAvailableRevisionStep = 1;

        if (revision < head.FirstAvailableRevision - FirstAvailableRevisionStep)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, ReplayHistoryUnavailableMessage);
        }
    }

    private static ImmutableArray<EventRecord> ReadTail(IKeyValueView view, StreamRef stream, StreamHead head,
        long afterRevision, int maximumEvents, ReadExecutionBudget budget)
    {
        const long VisitorSingleItemCount = 1L;
        const int VisitorEmptyCount = 0;

        var requiredCount = head.TailRevision - afterRevision;
        if (requiredCount > maximumEvents)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ReplayEventsExceededMessage);
        }
        var records = new List<EventRecord>((int)requiredCount);
        var prefix = KeySpace.Partition(EventSpace, stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation);
        var afterKey = KeySpace.Partition(EventSpace, stream.Partition, stream.StreamSet, stream.StreamId,
            stream.Generation, afterRevision);
        _ = budget.VisitAll(view, prefix, (key, value) =>
        {
            if (records.Count == requiredCount)
            {
                throw Errors.Fail(ErrorCode.Corruption, ReplayTailCorruptMessage);
            }
            var expectedRevision = checked(afterRevision + records.Count + VisitorSingleItemCount);
            var record = OrleansSerialization.Deserialize<EventRecord>(value);
            ValidateEvent(record, key, stream, expectedRevision, head);
            if (records.Count > VisitorEmptyCount && record.EventSequence <= records[^1].EventSequence)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidEventRecordMessage);
            }
            records.Add(record);
            return true;
        }, afterKey);
        if (records.Count != requiredCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplayTailCorruptMessage);
        }
        foreach (var record in records)
        {
            ValidateEventIdentity(view, record, budget);
        }
        return records.ToImmutableArray();
    }

    private static void ValidateEvent(EventRecord record, ReadOnlySpan<byte> key, StreamRef stream,
        long expectedRevision, StreamHead head)
    {
        const int EventSequenceValidationBoundary = 1;
        const int SchemaVersionValidationBoundary = 1;

        if (!record.Stream.Equals(stream) || record.Revision != expectedRevision || record.Revision > head.TailRevision
            || record.EventSequence < EventSequenceValidationBoundary || record.Data.SchemaVersion < SchemaVersionValidationBoundary
            || !key.SequenceEqual(KeySpace.Partition(EventSpace, stream.Partition, stream.StreamSet,
                stream.StreamId, stream.Generation, record.Revision)))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidEventRecordMessage);
        }
        try
        {
            JsonData.Identifier(record.Data.EventId);
            JsonData.Identifier(record.Data.EventType);
            _ = JsonData.Validate(record.Data.PayloadJson, requireObject: false);
            _ = JsonData.Validate(record.Data.HeadersJson, requireObject: false);
        }
        catch (KeyLoadException)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidEventRecordMessage);
        }
    }

    private static void ValidateEventIdentity(IKeyValueView view, EventRecord record, ReadExecutionBudget budget)
    {
        var identity = budget.ReadRecord<EventIdentity>(view, KeySpace.Partition(EventIdentitySpace,
            record.Stream.Partition, record.Stream.StreamSet, record.Stream.StreamId,
            record.Stream.Generation, record.Data.EventId));
        if (identity is null || identity.StreamId != record.Stream.StreamId
            || identity.Generation != record.Stream.Generation || identity.Revision != record.Revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidEventRecordMessage);
        }
    }

    internal static void ValidateHead(StreamHead head)
    {
        const int GenerationValidationBoundary = 1;
        const int TailRevisionValidationBoundary = 0;
        const int FirstAvailableRevisionValidationBoundary = 1;
        const int FirstAvailableRevisionStep = 1;

        if (head.Generation < GenerationValidationBoundary || head.TailRevision < TailRevisionValidationBoundary || head.FirstAvailableRevision < FirstAvailableRevisionValidationBoundary
            || head.FirstAvailableRevision - FirstAvailableRevisionStep > head.TailRevision)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStreamHeadMessage);
        }
    }
}
