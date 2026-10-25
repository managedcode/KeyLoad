using System.Text.Json;
using KeyLoad.Query;
using KeyLoad.Storage;

namespace KeyLoad.Core.QueryExecution;

/// <summary>Reads and validates one retained event stream without replay or mutation.</summary>
internal static class ModelEventQueryRows
{
    private const string HeadSpace = "stream-head";
    private const string EventSpace = "event";
    private const string EventCorrupt = "The retained event source is inconsistent.";
    private const string StaleGeneration = "The event source generation is stale.";

    internal static void Visit(IKeyValueView view, PartitionRef partition,
        ResourceDefinition resource, ModelQuerySource source, ReadExecutionBudget budget, bool explain,
        Action<DocumentRecord> accept)
    {
        const int GenerationValidationBoundary = 1;
        const string VisitDetailText = "The event source generation must be positive.";
        const int TailRevisionEmptyCount = 0;
        const int FirstAvailableRevisionSingleItemCount = 1;
        const int GenerationSingleItemCount = 1;
        const int EmptyTailRevision = 0;

        JsonData.Identifier(source.Item);
        if (source.Generation < GenerationValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Validation, VisitDetailText);
        }
        var headKey = KeySpace.Partition(HeadSpace, partition, resource.Name, source.Item);
        var head = budget.ReadRecord<StreamHead>(view, headKey) ?? new StreamHead(TailRevisionEmptyCount, FirstAvailableRevisionSingleItemCount, GenerationSingleItemCount);
        ValidateHead(head);
        if (head.Generation != source.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, StaleGeneration);
        }
        if (explain || head.TailRevision == EmptyTailRevision)
        {
            return;
        }

        VisitRetained(view, partition, resource, source, budget, head, accept);
    }

    private static void ValidateHead(StreamHead head)
    {
        const int GenerationValidationBoundary = 1;
        const int TailRevisionValidationBoundary = 0;
        const int FirstAvailableRevisionValidationBoundary = 1;
        const int TailRevisionStep = 1;

        if (head.Generation < GenerationValidationBoundary || head.TailRevision < TailRevisionValidationBoundary
            || head.FirstAvailableRevision < FirstAvailableRevisionValidationBoundary
            || head.TailRevision < long.MaxValue && head.FirstAvailableRevision > head.TailRevision + TailRevisionStep)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventCorrupt);
        }
    }

    private static void VisitRetained(IKeyValueView view, PartitionRef partition,
        ResourceDefinition resource, ModelQuerySource source, ReadExecutionBudget budget, StreamHead head,
        Action<DocumentRecord> accept)
    {
        const ulong EmptyRetainedEventCount = 0UL;
        const int InclusiveRevisionRangeOffset = 1;

        var state = new EventScanState(head.FirstAvailableRevision);
        var prefix = KeySpace.Partition(EventSpace, partition, resource.Name, source.Item, source.Generation);
        var scan = budget.VisitAll(view, prefix, (key, bytes) => VisitOne(view, partition, resource, source, budget, state, key, bytes, accept));

        var expectedCount = head.TailRevision < head.FirstAvailableRevision
            ? EmptyRetainedEventCount : (ulong)head.TailRevision - (ulong)head.FirstAvailableRevision + InclusiveRevisionRangeOffset;
        if ((ulong)state.Count != expectedCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventCorrupt);
        }
    }

    private static bool VisitOne(IKeyValueView view, PartitionRef partition,
        ResourceDefinition resource, ModelQuerySource source, ReadExecutionBudget budget, EventScanState state,
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> bytes, Action<DocumentRecord> accept)
    {
        budget.Check();
        if (state.Exhausted)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventCorrupt);
        }
        var record = Deserialize(bytes);
        ValidateRecord(view, partition, resource, source, budget, state.Revision, key, record);
        Accept(partition, resource, record, accept);
        state.Count++;
        state.Advance();
        return true;
    }

    private static EventRecord Deserialize(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return OrleansSerialization.Deserialize<EventRecord>(bytes);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException
                                           or InvalidOperationException)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventCorrupt);
        }
    }

    private static void ValidateRecord(IKeyValueView view, PartitionRef partition, ResourceDefinition resource,
        ModelQuerySource source, ReadExecutionBudget budget, long revision, ReadOnlySpan<byte> key,
        EventRecord record)
    {
        const int EventSequenceValidationBoundary = 1;
        const int SchemaVersionValidationBoundary = 1;

        var stream = new StreamRef(partition, resource.Name, source.Item, source.Generation);
        var expectedKey = KeySpace.Partition(EventSpace, partition, resource.Name, source.Item,
            source.Generation, revision);
        if (record is null || record.Data is null || !ModelQueryReadRows.IsIdentifier(record.Data.EventId)
            || !ModelQueryReadRows.IsIdentifier(record.Data.EventType)
            || record.Data.SchemaVersion < SchemaVersionValidationBoundary || record.EventSequence < EventSequenceValidationBoundary
            || record.Stream != stream || record.Revision != revision || !key.SequenceEqual(expectedKey))
        {
            throw Errors.Fail(ErrorCode.Corruption, EventCorrupt);
        }
        ValidateIdentity(view, partition, resource, source, budget, revision, record.Data.EventId);
    }

    private static void ValidateIdentity(IKeyValueView view, PartitionRef partition, ResourceDefinition resource,
        ModelQuerySource source, ReadExecutionBudget budget, long revision, string eventId)
    {
        const string EventIdentityKeySpace = "event-id";

        var identityKey = KeySpace.Partition(EventIdentityKeySpace, partition, resource.Name, source.Item,
            source.Generation, eventId);
        var identity = budget.ReadRecord<EventIdentity>(view, identityKey);
        if (identity is null || identity.StreamId != source.Item || identity.Generation != source.Generation
            || identity.Revision != revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, EventCorrupt);
        }
    }

    private static void Accept(PartitionRef partition, ResourceDefinition resource,
        EventRecord record, Action<DocumentRecord> accept)
    {
        using var payload = ModelQueryReadRows.ParseObject(record.Data.PayloadJson, EventCorrupt);
        using var headers = ModelQueryReadRows.ParseObject(record.Data.HeadersJson, EventCorrupt);
        var json = JsonSerializer.Serialize(new
        {
            eventId = record.Data.EventId,
            eventType = record.Data.EventType,
            schemaVersion = record.Data.SchemaVersion,
            revision = record.Revision,
            eventSequence = record.EventSequence,
            recordedAt = record.RecordedAt,
            payload = payload.RootElement,
            headers = headers.RootElement
        }, JsonDefaults.Options);
        accept(new(new(partition, resource.Name, record.Data.EventId), record.Revision, json,
            new RowAccess(), record.RecordedAt));
    }

    private sealed class EventScanState(long revision)
    {
        internal long Revision { get; private set; } = revision;
        internal long Count
        {
            get; set;
        }
        internal bool Exhausted
        {
            get; private set;
        }

        internal void Advance()
        {
            if (Revision == long.MaxValue)
            {
                Exhausted = true;
            }
            else
            {
                Revision++;
            }
        }
    }
}
