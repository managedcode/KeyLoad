using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Executes the authorized captured scalar view and builds its bounded continuation page.</summary>
internal static class QueryViewExecution
{
    private const int InitialSequence = 0;
    private const long NoRetainedBytesLong = 0L;

    internal static QueryPage Execute(DatabaseEngine database, TimeSpan cursorLifetime, IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        AstQueryRequest request, string hash, ReadExecutionBudget budget, TimeProvider clock)
    {
        var query = request.Query;
        budget.Check();
        QueryFieldAuthorization.Bind(database, principal, resource, request);
        var coverage = budget.CaptureResourceLifetimes(database, budget.CreateView(view), request.Partition, [resource]);
        var paged = QueryKeysetPage.Applies(query);
        var cursor = QueryCursorExecution.Resolve(database, view, principal, resource, request, hash, clock,
            coverage.Lifetimes.Single(), paged);
        var prepared = new PreparedQuery(query, cursor.Offset, database.Limits.MaxScanRecords);
        // One accepted row beyond the page proves that another page exists.
        var keyset = paged ? new QueryKeysetPage(cursor.AfterId, () => prepared.EligibleCount > query.Limit) : null;
        var accessPath = new QueryCandidateReader(database, view, principal, resource, request, budget, document =>
        {
            budget.Check();
            if (query.Explain || keyset?.Follows(document) == false)
            {
                return;
            }
            using var json = JsonDocument.Parse(document.Json);
            if (query.Filter is null || PredicateEvaluator.Evaluate(query.Filter, document, json.RootElement,
                    request.Parameters, prepared.Paths) == true)
            {
                budget.Check();
                prepared.Consider(document, json.RootElement);
            }
        }, visibilitySourceEpoch: cursor.SourceEpoch, keyset: keyset).Visit();
        if (query.Explain)
        {
            var explain = new QueryPage([new(QueryEngine.ExplainId, InitialSequence, JsonSerializer.Serialize(new { AccessPath = accessPath,
                AtomicPartition = request.Partition.AtomicPartitionId, ScanBudget = database.Limits.MaxScanRecords },
                JsonDefaults.Options))], null, cursor.Cut, accessPath);
            budget.CheckResult(explain);
            return explain;
        }
        return BuildPage(database, cursorLifetime, principal, resource, request, hash, cursor, prepared, accessPath, budget, clock,
            paged);
    }

    private static QueryPage BuildPage(DatabaseEngine database, TimeSpan cursorLifetime, PrincipalRecord principal, ResourceDefinition resource, AstQueryRequest request,
        string hash, QueryCursorExecution.State cursor, PreparedQuery prepared, string accessPath, ReadExecutionBudget budget, TimeProvider clock,
        bool paged)
    {
        var query = request.Query;
        var rows = new List<QueryRow>();
        var minimumResultBytes = NoRetainedBytesLong;
        string? lastId = null;
        foreach (var row in prepared.Page(cursor.Offset, query.Limit, budget))
        {
            lastId = row.Document.Reference.Id;
            budget.Check();
            var projected = QueryRowProjection.Project(database, principal, resource, row.Document, query.Projection, prepared.Paths);
            minimumResultBytes += Encoding.UTF8.GetByteCount(projected.Json);
            if (minimumResultBytes > budget.MaximumResultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, QueryEngine.ResultLimitExceeded);
            }
            rows.Add(projected);
        }
        var next = cursor.Offset + rows.Count;
        var token = QueryCursorExecution.SignNext(database, cursorLifetime, principal, resource, hash,
            cursor, next, prepared.EligibleCount, clock, paged ? lastId : null);
        var result = new QueryPage(rows.ToImmutableArray(), token, cursor.Cut, accessPath);
        budget.CheckResult(result);
        return result;
    }
}
