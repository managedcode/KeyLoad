using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class ModelQueryExecutor(DatabaseEngine database, QueryEngine queries)
{
    private const string EventModelScanPath = "model-scan:events";
    private const string QueueModelScanPath = "model-scan:queue-messages";
    private const int EmptyElementCount = 0;
    private const int InitialSequence = 0;
    private const long NoRetainedBytesLong = 0L;
    private const int NoRetainedBytes = 0;

    internal QueryPage Execute(string principalId, AstQueryRequest request, ReadExecutionBudget budget)
    {
        var query = request.Query;
        var source = query.ModelSource!;
        return database.WithModelQueryView(principalId, request.Partition, query.Collection, source, budget,
            (view, principal, resource) => ExecuteModelView(view, principal, resource, request, budget));
    }

    private QueryPage ExecuteModelView(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        AstQueryRequest request, ReadExecutionBudget budget)
    {
        var query = request.Query;
        var source = query.ModelSource!;
        var modelResource = ModelQueryPolicies.Rebase(resource);
        queries.Bind(principal, modelResource, request);
        var accessPath = source.Kind == ModelQuerySourceKind.Events
            ? EventModelScanPath : QueueModelScanPath;
        if (query.Explain)
        {
            return BuildModelExplain(view, principal, request, source, modelResource, budget, accessPath);
        }

        var prepared = new PreparedQuery(query, EmptyElementCount, query.Limit);
        database.VisitModelQueryRows(view, principal, request.Partition, modelResource, source, budget, false,
            document => ConsiderModelCandidate(document, request, prepared, budget));
        return BuildModelPage(principal, request, modelResource, prepared, budget, accessPath);
    }

    private QueryPage BuildModelExplain(IKeyValueView view, PrincipalRecord principal, AstQueryRequest request,
        ModelQuerySource source, ResourceDefinition resource, ReadExecutionBudget budget, string accessPath)
    {
        database.VisitModelQueryRows(view, principal, request.Partition, resource, source,
            budget, true, static _ => { });
        var row = new QueryRow(QueryEngine.ExplainId, InitialSequence, JsonSerializer.Serialize(new
        {
            AccessPath = accessPath,
            ModelViews = true,
            ScanBudget = database.Limits.MaxScanRecords
        }, JsonDefaults.Options));
        var page = new QueryPage([row], null, database.Store.Position, accessPath);
        budget.CheckResult(page);
        return page;
    }

    private static void ConsiderModelCandidate(DocumentRecord document, AstQueryRequest request,
        PreparedQuery prepared, ReadExecutionBudget budget)
    {
        budget.Check();
        using var json = JsonDocument.Parse(document.Json);
        if (request.Query.Filter is null || PredicateEvaluator.Evaluate(request.Query.Filter, document,
                json.RootElement, request.Parameters, prepared.Paths) == true)
        {
            prepared.Consider(document, json.RootElement);
        }
    }

    private QueryPage BuildModelPage(PrincipalRecord principal, AstQueryRequest request, ResourceDefinition resource,
        PreparedQuery prepared, ReadExecutionBudget budget, string accessPath)
    {
        var rows = ImmutableArray.CreateBuilder<QueryRow>();
        var resultBytes = NoRetainedBytesLong;
        foreach (var row in prepared.Page(NoRetainedBytes, request.Query.Limit, budget))
        {
            budget.Check();
            var projected = queries.Project(principal, resource, row.Document, request.Query.Projection, prepared.Paths);
            resultBytes += Encoding.UTF8.GetByteCount(projected.Json);
            if (resultBytes > database.Limits.MaxBatchBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, QueryEngine.ResultLimitExceeded);
            }
            rows.Add(projected);
        }
        var page = new QueryPage(rows.ToImmutable(), null, database.Store.Position, accessPath);
        budget.CheckResult(page);
        return page;
    }
}
