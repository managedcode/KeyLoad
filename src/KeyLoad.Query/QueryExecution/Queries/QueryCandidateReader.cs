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
        else if (plan.Kind == QueryAccessKind.FullScan)
        {
            FullScan();
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

    private void FullScan()
    {
        if (keyset is null)
        {
            RowVisibilityFullScan.Visit(database, principal, resource, request, budget, visibilitySourceEpoch,
                visitor => budget.VisitAll(view, plan.Prefix, visitor), accept);
            return;
        }
        var afterKey = keyset.AfterId is null ? null
            : KeySpace.Partition(DocumentSpace, request.Partition, request.Query.Collection, keyset.AfterId);
        RowVisibilityFullScan.Visit(database, principal, resource, request, budget, visibilitySourceEpoch,
            visitor => budget.VisitAll(view, plan.Prefix,
                (key, value) => visitor(key, value) && !keyset.Satisfied(), afterKey), accept, partial: afterKey is not null);
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
