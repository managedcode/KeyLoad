using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class PartitionQueryLeafExecutor(QueryEngine engine, DatabaseEngine database,
    ReadExecutionBudget budget)
{
    internal PartitionQueryLeafResultV1 Execute(string principalId, PartitionQueryLeafPlanV1 plan,
        AstQueryRequest normalized, ReadExecutionBudgetReadGrant grant)
    {
        var leafGrant = new PartitionQueryLeafReadGrant(grant, plan.MaxExaminedRecords);
        var retention = new PartitionQueryLeafRetention(plan.MaxRetainedBytes, plan.MaxCandidates);
        return database.WithQueryView(principalId, plan.Partition, normalized.Query.Collection,
            (view, principal, resource) => Capture(view, principal, resource, plan, normalized,
                leafGrant, retention));
    }

    private PartitionQueryLeafResultV1 Capture(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, PartitionQueryLeafPlanV1 plan, AstQueryRequest request,
        PartitionQueryLeafReadGrant grant, PartitionQueryLeafRetention retention)
    {
        budget.Check();
        engine.Bind(principal, resource, request);
        PreparedQuery? prepared = null;
        prepared = new(request.Query, 0, plan.MaxCandidates, ordinalFullReferenceOrder: true,
            projectCandidate: document => engine.Project(principal, resource, document,
                request.Query.Projection, prepared!.Paths),
            reserveCandidate: retention.Reserve, releaseCandidate: retention.Release);
        var reader = new QueryCandidateReader(database, view, principal, resource, request, budget,
            document => Accept(document, request, prepared, budget), grant);
        var accessPath = reader.Visit();
        var candidates = prepared.DrainCandidates(request.Query.Limit, budget,
            retention.Reserve, retention.ReleaseHeap);
        var identity = database.Store.Identity;
        return new(1, plan.Partition, identity.NodeId, identity.Incarnation, identity.ReadGeneration,
            database.Store.Position, principal.PolicyEpoch, resource.SchemaVersion, grant.ExaminedRecords,
            grant.ReadBytes, retention.CurrentBytes, accessPath, candidates);
    }

    private static void Accept(DocumentRecord document, AstQueryRequest request, PreparedQuery prepared,
        ReadExecutionBudget budget)
    {
        budget.Check();
        using var json = JsonDocument.Parse(document.Json);
        if (request.Query.Filter is null || PredicateEvaluator.Evaluate(request.Query.Filter, document,
                json.RootElement, request.Parameters, prepared.Paths) == true)
        {
            prepared.Consider(document, json.RootElement);
        }
    }
}
