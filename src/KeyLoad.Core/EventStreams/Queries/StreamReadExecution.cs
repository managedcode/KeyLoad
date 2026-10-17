using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string StreamReadBudgetInvalid = "The stream read budget is invalid.";
    private const string StreamGenerationStale = "The stream generation is stale.";
    private const string StreamHistoryUnavailable = "The requested event history was retained away.";
    private const string StreamHeadSpace = "stream-head";
    private const string EventSpace = "event";

    private void ValidateStreamRead(long afterRevision, int limit)
    {
        const int LimitFirstCount = 1;
        const int AfterRevisionValidationBoundary = 0;

        if (limit is < LimitFirstCount || limit > Limits.MaxResults || afterRevision < AfterRevisionValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, StreamReadBudgetInvalid);
        }
    }

    private static StreamHead ReadStreamHead(IKeyValueView view, StreamRef stream, ReadExecutionBudget budget)
    {
        const int TailRevisionEmptyCount = 0;
        const int FirstAvailableRevisionSingleItemCount = 1;

        var key = KeySpace.Partition(StreamHeadSpace, stream.Partition, stream.StreamSet, stream.StreamId);
        return budget.ReadRecord<StreamHead>(view, key) ?? new(TailRevisionEmptyCount, FirstAvailableRevisionSingleItemCount, stream.Generation);
    }

    private static void ValidateStreamHead(StreamHead head, StreamRef stream, long afterRevision)
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

    private EventRecord ProjectEvent(PrincipalRecord principal, ResourceDefinition resource, EventRecord record)
        => record with
        {
            Data = record.Data with
            {
                PayloadJson = Authorization.Project(principal, resource.FieldPolicies, record.Data.PayloadJson, out _),
                HeadersJson = Authorization.Project(principal, resource.HeaderPolicies, record.Data.HeadersJson, out _)
            }
        };

}
