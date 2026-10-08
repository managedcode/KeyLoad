using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class PartitionQueryLeafExecutor(QueryEngine engine, DatabaseEngine database,
    ReadExecutionBudget budget, PhysicalShardRecord? expectedOwner = null, string? expectedTenant = null)
{
    private const string ReceivingIdentityChangedDetail = "The partition query receiving identity changed.";
    private const int EmptyElementCount = 0;
    private const int VersionOne = 1;

    internal PartitionQueryLeafResultV1 Execute(string principalId, PartitionQueryLeafPlanV1 plan,
        AstQueryRequest normalized, ReadExecutionBudgetReadGrant grant)
    {
        var leafGrant = new PartitionQueryLeafReadGrant(grant, plan.MaxExaminedRecords);
        var retention = new PartitionQueryLeafRetention(plan.MaxRetainedBytes, plan.MaxCandidates);
        PartitionQueryLeafResultV1 Read(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource)
            => Capture(view, principal, resource, plan, normalized, leafGrant, retention);
        return expectedTenant is null
            ? database.WithQueryView(principalId, plan.Partition, normalized.Query.Collection, Read)
            : database.WithPartitionQueryView(principalId, plan.Partition, normalized.Query.Collection,
                grant, Read);
    }

    private PartitionQueryLeafResultV1 Capture(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, PartitionQueryLeafPlanV1 plan, AstQueryRequest request,
        PartitionQueryLeafReadGrant grant, PartitionQueryLeafRetention retention)
    {
        budget.Check();
        if (expectedTenant is not null && principal.TenantId != expectedTenant)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ReceivingIdentityChangedDetail); }
        engine.Bind(principal, resource, request);
        ValidatePlacement(view, plan.Partition, grant);
        PreparedQuery? prepared = null;
        prepared = new(request.Query, EmptyElementCount, plan.MaxCandidates, ordinalFullReferenceOrder: true,
            projectCandidate: document => engine.Project(principal, resource, document,
                request.Query.Projection, prepared!.Paths),
            reserveCandidate: retention.Reserve, releaseCandidate: retention.Release);
        var reader = new QueryCandidateReader(database, view, principal, resource, request, budget,
            document => Accept(document, request, prepared, budget), grant);
        var accessPath = reader.Visit();
        var candidates = prepared.DrainCandidates(request.Query.Limit, budget,
            retention.Reserve, retention.ReleaseHeap);
        var identity = database.Store.Identity;
        return new(VersionOne, plan.Partition, identity.NodeId, identity.Incarnation, identity.ReadGeneration,
            database.Store.Position, principal.PolicyEpoch, resource.SchemaVersion, grant.ExaminedRecords,
            grant.ReadBytes, retention.CurrentBytes, accessPath, candidates);
    }

    private void ValidatePlacement(IKeyValueView view, PartitionRef partition,
        PartitionQueryLeafReadGrant grant)
    {
        if (expectedOwner is null)
        {
            return;
        }

        var resolution = DatabaseEngine.ReadAtomicPartitionPlacementForAuthorizedQuery(view, partition,
            grant.NativeGrant);
        PartitionQueryPlacementValidation.Validate(resolution, partition, expectedOwner);
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
