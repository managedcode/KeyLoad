using KeyLoad.Storage;

namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Checks one graph expression under the caller's original authorized read cut.</summary>
internal static class GraphSearchFieldAuthority
{
    private const string MissingGraph = "The graph resource is unavailable.";
    private const string InvalidGraphResource = "The graph resource is invalid for this partition.";
    private const string LabelField = "/label";

    internal static void Require(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string graphName, bool usesLabels, ReadExecutionBudget budget)
    {
        database.Authorization.Require(principal, partition, graphName, Capability.GraphRead);
        var graph = budget.ReadRecord<ResourceDefinition>(view,
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, graphName))
            ?? throw Errors.Fail(ErrorCode.NotFound, MissingGraph);
        if (graph.Kind != ResourceKind.Graph || graph.TransactionDomainId != partition.TransactionDomainId)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidGraphResource);
        }
        if (usesLabels)
        {
            database.Authorization.RequireFieldUse(principal, graph, LabelField);
        }
    }
}
