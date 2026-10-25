using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes EventStreamsExecution operations against the caller's database context.</summary>
public static class EventStreamsExecution
{
    internal const string ExpectedStreamRevisionMismatchDetail = "The expected stream revision does not match.";
    internal const string EventFeedSpace = "event-feed";
    internal const long DefaultStreamAfterRevision = 0;
    internal const int DefaultStreamReadLimit = 100;
    internal const string DuplicateEventDetail = "The event ID is already retained in this source generation.";
    internal const string ConflictingEventContentDetail = "The event ID was reused with different content.";
    internal static void RejectDuplicateEvent(EventData existing, EventData incoming) => throw (JsonData.Fingerprint(existing) == JsonData.Fingerprint(incoming) ? Errors.Fail(ErrorCode.DuplicateEventId, DuplicateEventDetail) : Errors.Fail(ErrorCode.Conflict, ConflictingEventContentDetail));
    internal static MutationReceipt Append(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendEvents append, DateTimeOffset now)
    {
        const int EventsLengthValidationBoundary = 1;
        const int GenerationValidationBoundary = 1;
        const string InvalidEventBatchDetail = "The event batch or stream generation is invalid.";
        const string StreamHeadSpace = "stream-head";
        const string StaleStreamGenerationDetail = "The stream generation is stale.";
        const string InvalidStreamHeadDetail = "The persisted stream head is invalid.";
        const int HeadTailRevisionValidationBoundary = 0;
        const string EventSequenceSpace = "event-sequence";
        const string InvalidEventSequenceDetail = "The persisted event sequence is invalid.";
        const int AppendAbsentCount = 0;
        const int FirstAvailableRevisionSingleItemCount = 1;
        const string AppendKindText = "appendEvents";
        JsonData.Identifier(append.StreamId);
        var resource = engine.Resource(tx, partition, append.StreamSet, ResourceKind.StreamSet);
        if (append.Events.Length < EventsLengthValidationBoundary || append.Events.Length > engine.EventSourceExecution.MaximumAppendEvents || append.Generation < GenerationValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidEventBatchDetail);
        }

        var stream = new StreamRef(partition, append.StreamSet, append.StreamId, append.Generation);
        var headKey = KeySpace.Partition(StreamHeadSpace, partition, append.StreamSet, append.StreamId);
        var head = tx.GetRecord<StreamHead>(headKey);
        if (head is not null && head.Generation != append.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, StaleStreamGenerationDetail);
        }

        if (head is not null && (head.TailRevision < HeadTailRevisionValidationBoundary || head.FirstAvailableRevision < FirstAvailableRevisionSingleItemCount || head.TailRevision != long.MaxValue && head.FirstAvailableRevision > head.TailRevision + FirstAvailableRevisionSingleItemCount))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStreamHeadDetail);
        }

        if (append.ExpectedRevision.State == ExpectedStreamState.NoStream && head is not null || append.ExpectedRevision.State == ExpectedStreamState.Exact && append.ExpectedRevision.Revision != (head?.TailRevision ?? HeadTailRevisionValidationBoundary))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, ExpectedStreamRevisionMismatchDetail);
        }

        var tail = head?.TailRevision ?? HeadTailRevisionValidationBoundary;
        var sequenceKey = KeySpace.Partition(EventSequenceSpace, partition);
        var sequence = tx.ReadOwnedValue(sequenceKey) is { } bytes ? OrleansSerialization.Deserialize<long>(bytes) : AppendAbsentCount;
        if (sequence < tail)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidEventSequenceDetail);
        }

        foreach (var item in append.Events)
        {
            engine.AppendEvent(tx, principal, partition, append, resource, stream, item, now, ref tail, ref sequence);
        }

        tx.PutRecord(headKey, new StreamHead(tail, head?.FirstAvailableRevision ?? FirstAvailableRevisionSingleItemCount, append.Generation));
        tx.PutRecord(sequenceKey, sequence);
        return new(AppendKindText, append.StreamSet, append.StreamId, tail);
    }

    internal static void AppendEvent(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendEvents append, ResourceDefinition resource, StreamRef stream, EventData item, DateTimeOffset now, ref long tail, ref long sequence)
    {
        const int SchemaVersionValidationBoundary = 1;
        const string InvalidEventSchemaVersionDetail = "The event schema version is invalid.";
        const string EventIdentitySpace = "event-id";
        const string EventRecordSpace = "event";
        const string MissingRetainedEventDetail = "A retained event ID has no event.";
        const string InvalidRetainedEventDetail = "A retained event ID does not match its canonical event.";
        const int MinimumRetainedRevision = 1;
        JsonData.Identifier(item.EventId);
        JsonData.Identifier(item.EventType);
        if (item.SchemaVersion < SchemaVersionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidEventSchemaVersionDetail);
        }

        var idKey = KeySpace.Partition(EventIdentitySpace, partition, append.StreamSet, append.StreamId, append.Generation, item.EventId);
        var retained = tx.GetRecord<EventIdentity>(idKey);
        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        foreach (var policy in resource.HeaderPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }

        var data = item with
        {
            PayloadJson = JsonData.ValidateContent(item.PayloadJson),
            HeadersJson = JsonData.ValidateContent(item.HeadersJson)
        };
        if (retained is not null)
        {
            if (retained.StreamId != stream.StreamId || retained.Generation != stream.Generation || retained.Revision < MinimumRetainedRevision || retained.Revision > tail)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidRetainedEventDetail);
            }

            var existing = tx.GetRecord<EventRecord>(KeySpace.Partition(EventRecordSpace, partition, append.StreamSet, retained.StreamId, retained.Generation, retained.Revision)) ?? throw Errors.Fail(ErrorCode.Corruption, MissingRetainedEventDetail);
            if (existing.Stream != stream || existing.Revision != retained.Revision || existing.Data.EventId != data.EventId)
            {
                throw Errors.Fail(ErrorCode.Corruption, InvalidRetainedEventDetail);
            }

            RejectDuplicateEvent(existing.Data, data);
        }

        var record = new EventRecord(stream, checked(++tail), checked(++sequence), data, now);
        tx.PutRecord(KeySpace.Partition(EventRecordSpace, partition, append.StreamSet, append.StreamId, append.Generation, tail), record);
        tx.PutRecord(KeySpace.Partition(EventFeedSpace, partition, sequence), record);
        tx.PutRecord(idKey, new EventIdentity(append.StreamId, append.Generation, tail));
    }

    /// <summary>Reads an ordered, policy-projected page from one event stream.</summary>
    /// <param name = "principalId">Persisted principal requesting event-read access.</param>
    /// <param name = "stream">Stream identity and generation to read.</param>
    /// <param name = "afterRevision">Exclusive revision cursor.</param>
    /// <param name = "limit">Maximum number of events to return.</param>
    /// <param name = "cancellationToken">Caller cancellation for the complete bounded read.</param>
    /// <returns>A page with its committed cut and continuation indicator.</returns>
    /// <exception cref = "ArgumentNullException"><paramref name = "stream"/> is null.</exception>
    /// <exception cref = "KeyLoadException">Authorization, retention, generation, or read budgets reject the operation.</exception>

    /// <param name = "engine">The shared database execution context.</param>
    public static StreamPage ReadStream(this DatabaseEngine engine, string principalId, StreamRef stream, long afterRevision = DefaultStreamAfterRevision, int limit = DefaultStreamReadLimit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.ReadStream(principalId, new ReadStreamRequest(stream, afterRevision, limit), cancellationToken);
    }
}
