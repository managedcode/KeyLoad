using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private MutationReceipt Append(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendEvents append, DateTimeOffset now)
    {
        JsonData.Identifier(append.StreamId);
        var resource = Resource(tx, partition, append.StreamSet, ResourceKind.StreamSet);
        if (append.Events.Length is < 1 or > 256 || append.Generation < 1)
            throw Errors.Fail(ErrorCode.Validation, "The event batch or stream generation is invalid.");
        var stream = new StreamRef(partition, append.StreamSet, append.StreamId, append.Generation);
        var headKey = KeySpace.Partition("stream-head", partition, append.StreamSet, append.StreamId);
        var head = tx.GetRecord<StreamHead>(headKey);
        if (head is not null && head.Generation != append.Generation)
            throw Errors.Fail(ErrorCode.TokenInvalidated, "The stream generation is stale.");
        if (append.ExpectedRevision.State == ExpectedStreamState.NoStream && head is not null
            || append.ExpectedRevision.State == ExpectedStreamState.Exact && append.ExpectedRevision.Revision != (head?.TailRevision ?? 0))
            throw Errors.Fail(ErrorCode.RevisionConflict, "The expected stream revision does not match.");
        var tail = head?.TailRevision ?? 0;
        var sequenceKey = KeySpace.Partition("event-sequence", partition);
        var sequence = tx.Get(sequenceKey) is { } bytes ? JsonDefaults.Deserialize<long>(bytes) : 0;
        foreach (var item in append.Events)
        {
            JsonData.Identifier(item.EventId); JsonData.Identifier(item.EventType);
            if (item.SchemaVersion < 1) throw Errors.Fail(ErrorCode.Validation, "The event schema version is invalid.");
            var idKey = KeySpace.Partition("event-id", partition, append.StreamSet, item.EventId);
            if (tx.Get(idKey) is not null) throw Errors.Fail(ErrorCode.DuplicateEventId, "An event ID is already present in this partition.");
            foreach (var policy in resource.FieldPolicies) Authorization.RequireFieldWrite(principal, resource, policy.Path);
            foreach (var policy in resource.HeaderPolicies)
                Authorization.RequireFieldWrite(principal, resource with { FieldPolicies = resource.HeaderPolicies }, policy.Path);
            var data = item with { PayloadJson = JsonData.Validate(item.PayloadJson, Limits), HeadersJson = JsonData.Validate(item.HeadersJson, Limits) };
            var record = new EventRecord(stream, checked(++tail), checked(++sequence), data, now);
            tx.PutRecord(KeySpace.Partition("event", partition, append.StreamSet, append.StreamId, append.Generation, tail), record);
            tx.PutRecord(KeySpace.Partition("event-feed", partition, sequence), record);
            tx.PutRecord(idKey, new { append.StreamId, append.Generation, Revision = tail });
        }
        tx.PutRecord(headKey, new StreamHead(tail, head?.FirstAvailableRevision ?? 1, append.Generation));
        tx.PutRecord(sequenceKey, sequence);
        return new("appendEvents", append.StreamSet, append.StreamId, tail);
    }
    public StreamPage ReadStream(string principalId, StreamRef stream, long afterRevision = 0, int limit = 100)
        => Store.Read<StreamPage>(view =>
        {
            if (limit is < 1 || limit > Limits.MaxResults || afterRevision < 0) throw Errors.Fail(ErrorCode.BudgetExceeded, "The stream read budget is invalid.");
            var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
            Authorization.Require(principal, stream.Partition, stream.StreamSet, Capability.EventsRead);
            var resource = Resource(view, stream.Partition, stream.StreamSet, ResourceKind.StreamSet);
            var head = view.GetRecord<StreamHead>(KeySpace.Partition("stream-head", stream.Partition, stream.StreamSet, stream.StreamId))
                ?? new(0, 1, stream.Generation);
            if (head.Generation != stream.Generation) throw Errors.Fail(ErrorCode.TokenInvalidated, "The stream generation is stale.");
            if (afterRevision < head.FirstAvailableRevision - 1) throw Errors.Fail(ErrorCode.HistoryUnavailable, "The requested event history was retained away.");
            var prefix = KeySpace.Partition("event", stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation);
            var after = KeySpace.Partition("event", stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation, afterRevision);
            var page = view.Scan(prefix, limit, after);
            var events = page.Records.Select(kv => JsonDefaults.Deserialize<EventRecord>(kv.Value)).Select(record => record with
            {
                Data = record.Data with
                {
                    PayloadJson = Authorization.Project(principal, resource.FieldPolicies, record.Data.PayloadJson, out _),
                    HeadersJson = Authorization.Project(principal, resource.HeaderPolicies, record.Data.HeadersJson, out _)
                }
            }).ToArray();
            return new(stream, head, events, Store.Position, page.HasMore);
        });
}
