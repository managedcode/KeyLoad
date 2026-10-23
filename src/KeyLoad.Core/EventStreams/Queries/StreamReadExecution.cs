using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes StreamReadExecution operations against the caller's database context.</summary>
public static class StreamReadExecution
{
    internal const string StreamReadBudgetInvalid = "The stream read budget is invalid.";
    internal const string StreamGenerationStale = "The stream generation is stale.";
    internal const string StreamHistoryUnavailable = "The requested event history was retained away.";
    internal const string StreamHeadSpace = "stream-head";
    internal const string EventSpace = "event";
    internal static void ValidateStreamRead(this DatabaseEngine engine, long afterRevision, int limit)
    {
        const int LimitFirstCount = 1;
        const int AfterRevisionValidationBoundary = 0;
        if (limit is < LimitFirstCount || limit > engine.Limits.MaxResults || afterRevision < AfterRevisionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, StreamReadBudgetInvalid);
        }
    }

    internal static StreamHead ReadStreamHead(IKeyValueView view, StreamRef stream, ReadExecutionBudget budget)
    {
        const int TailRevisionEmptyCount = 0;
        const int FirstAvailableRevisionSingleItemCount = 1;
        var key = KeySpace.Partition(StreamHeadSpace, stream.Partition, stream.StreamSet, stream.StreamId);
        return budget.ReadRecord<StreamHead>(view, key) ?? new(TailRevisionEmptyCount, FirstAvailableRevisionSingleItemCount, stream.Generation);
    }

    internal static void ValidateStreamHead(StreamHead head, StreamRef stream, long afterRevision)
    {
        const int FirstAvailableRevisionStep = 1;
        if (head.Generation != stream.Generation)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, StreamGenerationStale);
        }

        if (afterRevision < head.FirstAvailableRevision - FirstAvailableRevisionStep)
        {
            throw Errors.Fail(ErrorCode.HistoryUnavailable, StreamHistoryUnavailable);
        }
    }

    internal static EventRecord ProjectEvent(this DatabaseEngine engine, PrincipalRecord principal, ResourceDefinition resource, EventRecord record) => record with
    {
        Data = record.Data with
        {
            PayloadJson = engine.Authorization.Project(principal, resource.FieldPolicies, record.Data.PayloadJson, out _),
            HeadersJson = engine.Authorization.Project(principal, resource.HeaderPolicies, record.Data.HeadersJson, out _)
        }
    };
}
