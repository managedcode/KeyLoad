using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string ExpectedStreamRevisionMismatchDetail = "The expected stream revision does not match.";
    private const string EventFeedSpace = "event-feed";
    private const long DefaultStreamAfterRevision = 0;
    private const int DefaultStreamReadLimit = 100;

    private const string DuplicateEventDetail = "The event ID is already retained in this source generation.";
    private const string ConflictingEventContentDetail = "The event ID was reused with different content.";

    private static void RejectDuplicateEvent(EventData existing, EventData incoming)
        => throw (JsonData.Fingerprint(existing) == JsonData.Fingerprint(incoming)
            ? Errors.Fail(ErrorCode.DuplicateEventId, DuplicateEventDetail)
            : Errors.Fail(ErrorCode.Conflict, ConflictingEventContentDetail));
    private MutationReceipt Append(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendEvents append, DateTimeOffset now)
    {
        const int EventsLengthValidationBoundary = 1;
        const int GenerationValidationBoundary = 1;
        const string InvalidEventBatchDetail = "The event batch or stream generation is invalid.";
        const string StreamHeadSpace = "stream-head";
        const string StaleStreamGenerationDetail = "The stream generation is stale.";
        const int HeadTailRevisionValidationBoundary = 0;
        const string EventSequenceSpace = "event-sequence";
        const int AppendAbsentCount = 0;
        const int FirstAvailableRevisionSingleItemCount = 1;
        const string AppendKindText = "appendEvents";

        JsonData.Identifier(append.StreamId);
        var resource = Resource(tx, partition, append.StreamSet, ResourceKind.StreamSet);
        if (append.Events.Length < EventsLengthValidationBoundary || append.Events.Length > eventSourceExecution.MaximumAppendEvents || append.Generation < GenerationValidationBoundary)
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

        if (append.ExpectedRevision.State == ExpectedStreamState.NoStream && head is not null
            || append.ExpectedRevision.State == ExpectedStreamState.Exact && append.ExpectedRevision.Revision != (head?.TailRevision ?? HeadTailRevisionValidationBoundary))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, ExpectedStreamRevisionMismatchDetail);
        }

        var tail = head?.TailRevision ?? HeadTailRevisionValidationBoundary;
        var sequenceKey = KeySpace.Partition(EventSequenceSpace, partition);
        var sequence = tx.ReadOwnedValue(sequenceKey) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : AppendAbsentCount;
        foreach (var item in append.Events)
        {
            AppendEvent(tx, principal, partition, append, resource, stream, item, now, ref tail, ref sequence);
        }
        tx.PutRecord(headKey, new StreamHead(tail, head?.FirstAvailableRevision ?? FirstAvailableRevisionSingleItemCount, append.Generation));
        tx.PutRecord(sequenceKey, sequence);
        return new(AppendKindText, append.StreamSet, append.StreamId, tail);
    }
    private void AppendEvent(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendEvents append,
        ResourceDefinition resource, StreamRef stream, EventData item, DateTimeOffset now, ref long tail, ref long sequence)
    {
        const int SchemaVersionValidationBoundary = 1;
        const string InvalidEventSchemaVersionDetail = "The event schema version is invalid.";
        const string EventIdentitySpace = "event-id";
        const string EventRecordSpace = "event";
        const string MissingRetainedEventDetail = "A retained event ID has no event.";

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
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        foreach (var policy in resource.HeaderPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
        }

        var data = item with { PayloadJson = JsonData.Validate(item.PayloadJson, Limits), HeadersJson = JsonData.Validate(item.HeadersJson, Limits) };
        if (retained is not null)
        {
            var existing = tx.GetRecord<EventRecord>(KeySpace.Partition(EventRecordSpace, partition, append.StreamSet, retained.StreamId, retained.Generation, retained.Revision))
                ?? throw Errors.Fail(ErrorCode.Corruption, MissingRetainedEventDetail);
            RejectDuplicateEvent(existing.Data, data);
        }
        var record = new EventRecord(stream, checked(++tail), checked(++sequence), data, now);
        tx.PutRecord(KeySpace.Partition(EventRecordSpace, partition, append.StreamSet, append.StreamId, append.Generation, tail), record);
        tx.PutRecord(KeySpace.Partition(EventFeedSpace, partition, sequence), record);
        tx.PutRecord(idKey, new EventIdentity(append.StreamId, append.Generation, tail));
    }
    /// <summary>Reads an ordered, policy-projected page from one event stream.</summary>
    /// <param name="principalId">Persisted principal requesting event-read access.</param>
    /// <param name="stream">Stream identity and generation to read.</param>
    /// <param name="afterRevision">Exclusive revision cursor.</param>
    /// <param name="limit">Maximum number of events to return.</param>
    /// <param name="cancellationToken">Caller cancellation for the complete bounded read.</param>
    /// <returns>A page with its committed cut and continuation indicator.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="KeyLoadException">Authorization, retention, generation, or read budgets reject the operation.</exception>
    public StreamPage ReadStream(string principalId, StreamRef stream, long afterRevision = DefaultStreamAfterRevision, int limit = DefaultStreamReadLimit,
        CancellationToken cancellationToken = default)
        => ReadStream(principalId, new ReadStreamRequest(stream, afterRevision, limit), cancellationToken);
}
