using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{

    private static void RejectDuplicateEvent(EventData existing, EventData incoming)
        => throw (JsonData.Fingerprint(existing) == JsonData.Fingerprint(incoming)
            ? Errors.Fail(ErrorCode.DuplicateEventId, "The event ID is already retained in this source generation.")
            : Errors.Fail(ErrorCode.Conflict, "The event ID was reused with different content."));
    private MutationReceipt Append(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendEvents append, DateTimeOffset now)
    {
        JsonData.Identifier(append.StreamId);
        var resource = Resource(tx, partition, append.StreamSet, ResourceKind.StreamSet);
        if (append.Events.Length is < 1 or > 256 || append.Generation < 1)
        {
            throw Errors.Fail(ErrorCode.Validation, "The event batch or stream generation is invalid.");
        }

        var stream = new StreamRef(partition, append.StreamSet, append.StreamId, append.Generation);
        var headKey = KeySpace.Partition("stream-head", partition, append.StreamSet, append.StreamId);
        var head = tx.GetRecord<StreamHead>(headKey);
        if (head is not null && head.Generation != append.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The stream generation is stale.");
        }

        if (append.ExpectedRevision.State == ExpectedStreamState.NoStream && head is not null
            || append.ExpectedRevision.State == ExpectedStreamState.Exact && append.ExpectedRevision.Revision != (head?.TailRevision ?? 0))
        {
            throw Errors.Fail(ErrorCode.RevisionConflict, "The expected stream revision does not match.");
        }

        var tail = head?.TailRevision ?? 0;
        var sequenceKey = KeySpace.Partition("event-sequence", partition);
        var sequence = tx.ReadOwnedValue(sequenceKey) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : 0;
        foreach (var item in append.Events)
        {
            AppendEvent(tx, principal, partition, append, resource, stream, item, now, ref tail, ref sequence);
        }
        tx.PutRecord(headKey, new StreamHead(tail, head?.FirstAvailableRevision ?? 1, append.Generation));
        tx.PutRecord(sequenceKey, sequence);
        return new("appendEvents", append.StreamSet, append.StreamId, tail);
    }
    private void AppendEvent(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendEvents append,
        ResourceDefinition resource, StreamRef stream, EventData item, DateTimeOffset now, ref long tail, ref long sequence)
    {
        JsonData.Identifier(item.EventId);
        JsonData.Identifier(item.EventType);
        if (item.SchemaVersion < 1)
        {
            throw Errors.Fail(ErrorCode.Validation, "The event schema version is invalid.");
        }

        var idKey = KeySpace.Partition("event-id", partition, append.StreamSet, append.StreamId, append.Generation, item.EventId);
        var retained = tx.GetRecord<EventIdentity>(idKey);
        // The first kernel stored a resource-wide ID pointer. Existing stores keep its dedup guarantee during this format upgrade.
        if (retained is null && tx.GetRecord<EventIdentity>(KeySpace.Partition("event-id", partition, append.StreamSet, item.EventId)) is { } legacy
            && legacy.StreamId == append.StreamId && legacy.Generation == append.Generation)
        {
            retained = legacy;
        }

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
            var existing = tx.GetRecord<EventRecord>(KeySpace.Partition("event", partition, append.StreamSet, retained.StreamId, retained.Generation, retained.Revision))
                ?? throw Errors.Fail(ErrorCode.Corruption, "A retained event ID has no event.");
            RejectDuplicateEvent(existing.Data, data);
        }
        var record = new EventRecord(stream, checked(++tail), checked(++sequence), data, now);
        tx.PutRecord(KeySpace.Partition("event", partition, append.StreamSet, append.StreamId, append.Generation, tail), record);
        tx.PutRecord(KeySpace.Partition("event-feed", partition, sequence), record);
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
    public StreamPage ReadStream(string principalId, StreamRef stream, long afterRevision = 0, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var budget = new ReadExecutionBudget(Limits, Clock, cancellationToken);
        return Store.Read(view => ReadStream(view, principalId, stream, afterRevision, limit, budget));
    }
}
