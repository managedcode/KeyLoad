using System.Collections.Immutable;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string StreamReadBudgetInvalid = "The stream read budget is invalid.";
    private const string StreamGenerationStale = "The stream generation is stale.";
    private const string StreamHistoryUnavailable = "The requested event history was retained away.";
    private const string ProjectedEventsExceedBudget = "The projected stream events exceed the result byte budget.";
    private const string StreamHeadSpace = "stream-head";
    private const string EventSpace = "event";

    private StreamPage ReadStream(IKeyValueView view, string principalId, StreamRef stream, long afterRevision, int limit,
        ReadExecutionBudget budget)
    {
        budget.Check();
        ValidateStreamRead(afterRevision, limit);
        var now = Clock.GetUtcNow();
        var principal = Principal(view, principalId, now);
        Authorization.Require(principal, stream.Partition, stream.StreamSet, Capability.EventsRead);
        var resource = Resource(view, stream.Partition, stream.StreamSet, ResourceKind.StreamSet);
        var head = ReadStreamHead(view, stream, budget);
        ValidateStreamHead(head, stream, afterRevision);
        var range = ReadProjectedEvents(view, stream, principal, resource, afterRevision, limit, budget);
        var page = new StreamPage(stream, head, range.Events, Store.Position, range.HasMore);
        budget.CheckResult(page);
        return page;
    }

    private void ValidateStreamRead(long afterRevision, int limit)
    {
        if (limit is < 1 || limit > Limits.MaxResults || afterRevision < 0)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, StreamReadBudgetInvalid);
        }
    }

    private static StreamHead ReadStreamHead(IKeyValueView view, StreamRef stream, ReadExecutionBudget budget)
    {
        var key = KeySpace.Partition(StreamHeadSpace, stream.Partition, stream.StreamSet, stream.StreamId);
        return budget.ReadRecord<StreamHead>(view, key) ?? new(0, 1, stream.Generation);
    }

    private static void ValidateStreamHead(StreamHead head, StreamRef stream, long afterRevision)
    {
        if (head.Generation != stream.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, StreamGenerationStale);
        }
        if (afterRevision < head.FirstAvailableRevision - 1)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, StreamHistoryUnavailable);
        }
    }

    private (ImmutableArray<EventRecord> Events, bool HasMore) ReadProjectedEvents(IKeyValueView view, StreamRef stream,
        PrincipalRecord principal, ResourceDefinition resource, long afterRevision, int limit, ReadExecutionBudget budget)
    {
        var prefix = KeySpace.Partition(EventSpace, stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation);
        var after = KeySpace.Partition(EventSpace, stream.Partition, stream.StreamSet, stream.StreamId, stream.Generation, afterRevision);
        var events = new List<EventRecord>();
        var projectedBytes = 0L;
        var result = budget.VisitRange(view, prefix, limit, (_, value) =>
        {
            var record = JsonDefaults.Deserialize<EventRecord>(value);
            var projected = ProjectEvent(principal, resource, record);
            projectedBytes = AddProjectedEvent(events, projected, budget, projectedBytes);
            return true;
        }, after);
        return (events.ToImmutableArray(), result.HasMore);
    }

    private EventRecord ProjectEvent(PrincipalRecord principal, ResourceDefinition resource, EventRecord record)
        => record with
        {
            Data = record.Data with
            {
                PayloadJson = Authorization.Project(principal, resource.FieldPolicies, record.Data.PayloadJson, out _),
                HeadersJson = Authorization.Project(principal, resource.HeaderPolicies, record.Data.HeadersJson, out _)
            }
        };

    private long AddProjectedEvent(List<EventRecord> events, EventRecord projected, ReadExecutionBudget budget,
        long projectedBytes)
    {
        var eventBytes = budget.MeasureResult(projected);
        if (eventBytes > Limits.MaxBatchBytes - projectedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ProjectedEventsExceedBudget);
        }
        events.Add(projected);
        return projectedBytes + eventBytes;
    }
}
