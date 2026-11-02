using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Executes the selected physical plan through the original authorized read budget.</summary>
internal sealed class QueryCandidateReader(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
    ResourceDefinition resource, AstQueryRequest request, QueryAccessPlan plan, ReadExecutionBudget budget,
    Action<DocumentRecord> accept, long? visibilitySourceEpoch = null, QueryKeysetPage? keyset = null)
{
    private const string DocumentSpace = "document";
    private const string CandidateLimitExceeded = "The query access path exceeds its candidate budget.";

    internal string Visit()
    {
        if (plan.Kind == QueryAccessKind.Point)
        {
            Read(plan.PointId!);
        }
        else if (plan.Kind is QueryAccessKind.FullScan or QueryAccessKind.PrimaryRange)
        {
            DocumentRange();
        }
        else
        {
            var result = budget.VisitAll(view, plan.Prefix, (_, value) =>
            {
                Read(OrleansSerialization.Deserialize<string>(value));
                return true;
            }, plan.AfterKey, plan.UntilKey);
            if (result.HasMore)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, CandidateLimitExceeded);
            }
        }
        return plan.AccessPath;
    }

    private void DocumentRange()
    {
        var afterKey = plan.AfterKey;
        if (keyset?.AfterId is { } afterId)
        {
            var cursorKey = KeySpace.Partition(DocumentSpace, request.Partition, request.Query.Collection, afterId);
            if (afterKey is null || BinaryKeyComparer.Instance.Compare(afterKey, cursorKey) < 0)
            {
                afterKey = cursorKey;
            }
        }
        RowVisibilityFullScan.Visit(database, principal, resource, request, budget, visibilitySourceEpoch,
            visitor => budget.VisitAll(view, plan.Prefix,
                (key, value) => visitor(key, value) && keyset?.Satisfied() != true, afterKey, plan.UntilKey), accept,
            partial: plan.Kind == QueryAccessKind.PrimaryRange || afterKey is not null);
    }

    private void Candidate(ReadOnlySpan<byte> payload)
    {
        var record = OrleansSerialization.Deserialize<DocumentRecord>(payload);
        if (!record.Deleted && database.Authorization.CanReadRow(principal, record.Access))
        {
            accept(record);
        }
    }

    private void Read(string id)
    {
        budget.Check();
        view.ReadValue(KeySpace.Partition(DocumentSpace, request.Partition, request.Query.Collection, id), Candidate);
    }
}
