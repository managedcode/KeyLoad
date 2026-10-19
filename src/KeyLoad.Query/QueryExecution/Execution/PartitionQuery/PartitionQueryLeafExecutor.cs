using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class PartitionQueryLeafExecutor(QueryEngine engine, DatabaseEngine database,
    ReadExecutionBudget budget, PhysicalShardRecord? expectedOwner = null, bool receivingLeaf = false)
{
    private const int EmptyElementCount = 0;
    private const int VersionOne = 1;

    internal PartitionQueryLeafResult Execute(string principalId, PartitionQueryLeafPlan plan,
        AstQueryRequest normalized, ReadExecutionBudgetReadGrant grant)
    {
        var leafGrant = new PartitionQueryLeafReadGrant(grant, plan.MaxExaminedRecords);
        var retention = new PartitionQueryLeafRetention(plan.MaxRetainedBytes, plan.MaxCandidates);
        PartitionQueryLeafResult Read(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource)
            => Capture(view, principal, resource, plan, normalized, leafGrant, retention);
        return !receivingLeaf
            ? database.WithQueryView(principalId, plan.Partition, normalized.Query.Collection, Capability.None, Read, budget)
            : database.WithPartitionQueryView(principalId, plan.Partition, normalized.Query.Collection,
                grant, Read, budget);
    }

    internal PartitionQueryLeafResult ExecuteLifetime(string principalId, PartitionQueryLeafPlan plan,
        AstQueryRequest normalized, ReadExecutionBudgetReadGrant grant, PartitionQueryLifetimeMetadata metadata)
    {
        var leafGrant = new PartitionQueryLeafReadGrant(grant, plan.MaxExaminedRecords);
        var retention = new PartitionQueryLeafRetention(plan.MaxRetainedBytes, plan.MaxCandidates);
        return database.WithPartitionQueryLifetimeView(principalId, plan.Partition, normalized.Query.Collection,
            grant, budget, metadata, (view, principal, resource, _) =>
                Capture(view, principal, resource, plan, normalized, leafGrant, retention, metadata));
    }

    private PartitionQueryLeafResult Capture(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, PartitionQueryLeafPlan plan, AstQueryRequest request,
        PartitionQueryLeafReadGrant grant, PartitionQueryLeafRetention retention, PartitionQueryLifetimeMetadata? metadata = null)
    {
        budget.Check();
        QueryFieldAuthorization.RequireSchema(database, principal, resource, request);
        if (metadata is not null)
        {
            QueryFieldAuthorization.RequireSchema(database, metadata.Principal, metadata.Definition, request);
            engine.Bind(metadata.Principal, metadata.Definition, request);
        }
        engine.Bind(principal, resource, request);
        ValidatePlacement(view, plan.Partition, grant);
        PreparedQuery? prepared = null;
        prepared = new(request.Query, EmptyElementCount, plan.MaxCandidates, ordinalFullReferenceOrder: true,
            projectCandidate: document => metadata is null
                ? engine.Project(principal, resource, document, request.Query.Projection, prepared!.Paths)
                : QueryRowProjection.ProjectIntersection(database, metadata, principal, resource, document,
                    request.Query.Projection, prepared!.Paths),
            reserveCandidate: retention.Reserve, releaseCandidate: retention.Release);
        var reader = new QueryCandidateReader(database, view, principal, resource, request, budget,
            document =>
            {
                if (metadata is null || database.Authorization.CanReadRow(metadata.Principal, document.Access))
                {
                    Accept(document, request, prepared, budget);
                }
            }, grant);
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

        var resolution = database.ReadAtomicPartitionPlacementForAuthorizedQuery(view, partition,
            grant.RuntimeGrant);
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
