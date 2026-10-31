using KeyLoad.Core;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.GraphTraversal;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Query.Search;
using KeyLoad.Storage;

namespace KeyLoad.Query;

internal static class GraphSearchExpansion
{
    private const string ResourceMissing = "An authorized graph context collection is unavailable.";
    private const string DocumentMissing = "A graph context document is unavailable in the current read cut.";
    private const string DomainMismatch = "A graph context collection belongs to another partition domain.";

    internal static GraphExpansionResult Execute(DatabaseEngine database, PrincipalRecord principal,
        IKeyValueView view, GraphSearchRequest request, SearchScore[] selected,
        ReadExecutionBudget budget)
    {
        var operatorSpec = request.Expansion!;
        var walk = new GraphWalkSpec(operatorSpec.Graph, [.. selected.Select(item => item.Reference)],
            operatorSpec.MaxDepth, operatorSpec.MaxVertices, operatorSpec.MaxEdges, operatorSpec.Labels);
        var reachable = database.ReadGraphSearchReachability(view, principal, request.Search.Partition, walk, budget);
        var hitReferences = selected.Select(item => item.Reference).ToHashSet();
        var documents = Project(database, principal, view, reachable, hitReferences, budget);
        return new([.. documents], GraphExpansionCompleteness.SelectedHits);
    }

    private static List<GraphContextDocument> Project(DatabaseEngine database, PrincipalRecord principal,
        IKeyValueView view, GraphSearchReachability[] reachable, HashSet<EntityRef> hitReferences, ReadExecutionBudget budget)
    {
        var resources = new Dictionary<string, ResourceDefinition>(StringComparer.Ordinal);
        var results = new List<GraphContextDocument>(reachable.Length);
        foreach (var entry in reachable)
        {
            budget.Check();
            if (hitReferences.Contains(entry.Reference))
            {
                continue;
            }
            var resource = GetResource(database, principal, view, entry.Reference, resources, budget);
            var document = ReadDocument(database, principal, view, entry.Reference, budget);
            var context = new GraphContextDocument(database.Project(principal, resource, document), entry.ShortestHops);
            results.Add(context);
        }
        return results;
    }

    private static ResourceDefinition GetResource(DatabaseEngine database, PrincipalRecord principal,
        IKeyValueView view, EntityRef reference, Dictionary<string, ResourceDefinition> resources,
        ReadExecutionBudget budget)
    {
        database.Authorization.Require(principal, reference.Partition, reference.Collection, Capability.DocumentsRead);
        if (resources.TryGetValue(reference.Collection, out var cached))
        {
            return cached;
        }
        var resource = budget.ReadRecord<ResourceDefinition>(view,
                KeySpace.Resource(reference.Partition.TenantId, reference.Partition.DatabaseId, reference.Collection));
        _ = (ResourceLifetimeCatalog.Read(budget.CreateView(view), reference.Partition.TenantId,
            reference.Partition.DatabaseId, reference.Collection, resource)).Value;
        if (resource is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceMissing);
        }
        if (resource.Kind != ResourceKind.Collection || resource.TransactionDomainId != reference.Partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Corruption, DomainMismatch);
        }
        resources.Add(reference.Collection, resource);
        return resource;
    }

    private static DocumentRecord ReadDocument(DatabaseEngine database, PrincipalRecord principal,
        IKeyValueView view, EntityRef reference, ReadExecutionBudget budget)
    {
        var document = budget.ReadRecord<DocumentRecord>(view, DocumentStorageKeys.RecordKey(reference));
        if (document is null || document.Deleted || !database.Authorization.CanReadRow(principal, document.Access))
        {
            throw Errors.Fail(ErrorCode.Corruption, DocumentMissing);
        }
        return document;
    }
}
