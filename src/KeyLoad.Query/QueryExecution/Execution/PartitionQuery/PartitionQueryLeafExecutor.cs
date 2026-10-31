using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.QueryExecution;

internal sealed class PartitionQueryLeafExecutor(QueryEngine engine, DatabaseEngine database,
    ReadExecutionBudget budget, PhysicalShardRecord? expectedOwner = null, bool receivingLeaf = false)
{
    private const int EmptyElementCount = 0;
    private const int VersionOne = 1;

    internal PartitionQueryLeafResult Execute(string principalId, PartitionQueryLeafPlan plan,
        AstQueryRequest normalized)
    {
        PartitionQueryLeafResult Read(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource)
            => Capture(view, principal, resource, plan, normalized);
        return !receivingLeaf
            ? database.WithQueryView(principalId, plan.Partition, normalized.Query.Collection, Capability.None, Read, budget)
            : database.WithPartitionQueryView(principalId, plan.Partition, normalized.Query.Collection, Read, budget);
    }

    internal PartitionQueryLeafResult ExecuteLifetime(string principalId, PartitionQueryLeafPlan plan,
        AstQueryRequest normalized, PartitionQueryLifetimeMetadata metadata)
    {
        return database.WithPartitionQueryLifetimeView(principalId, plan.Partition, normalized.Query.Collection, budget, metadata, (view, principal, resource, _) =>
                Capture(view, principal, resource, plan, normalized, metadata));
    }

    private PartitionQueryLeafResult Capture(IKeyValueView view, PrincipalRecord principal,
        ResourceDefinition resource, PartitionQueryLeafPlan plan, AstQueryRequest request,
        PartitionQueryLifetimeMetadata? metadata = null)
    {
        budget.Check();
        QueryFieldAuthorization.RequireSchema(database, principal, resource, request);
        if (metadata is not null)
        {
            QueryFieldAuthorization.RequireSchema(database, metadata.Principal, metadata.Definition, request);
            engine.Bind(metadata.Principal, metadata.Definition, request);
        }
        engine.Bind(principal, resource, request);
        ValidatePlacement(view, plan.Partition);
        PreparedQuery? prepared = null;
        prepared = new(request.Query, EmptyElementCount, plan.MaxCandidates, ordinalFullReferenceOrder: true,
            projectCandidate: document => metadata is null
                ? engine.Project(principal, resource, document, request.Query.Projection, prepared!.Paths)
                : QueryRowProjection.ProjectIntersection(database, metadata, principal, resource, document,
                    request.Query.Projection, prepared!.Paths));
        var reader = new QueryCandidateReader(database, view, principal, resource, request, budget,
            document =>
            {
                if (metadata is null || database.Authorization.CanReadRow(metadata.Principal, document.Access))
                {
                    Accept(document, request, prepared, budget);
                }
            });
        var accessPath = reader.Visit();
        var candidates = prepared.DrainCandidates(request.Query.Limit, budget);
        var identity = database.Store.Identity;
        return new(VersionOne, plan.Partition, identity.NodeId, identity.Incarnation, identity.ReadGeneration,
            database.Store.Position, principal.PolicyEpoch, resource.SchemaVersion, accessPath, candidates);
    }

    private void ValidatePlacement(IKeyValueView view, PartitionRef partition)
    {
        if (expectedOwner is null)
        {
            return;
        }

        var resolution = database.ReadAtomicPartitionPlacementForAuthorizedQuery(view, partition);
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
