using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes GraphTraversalQueries operations against the caller's database context.</summary>
public static class GraphTraversalQueries
{
    internal static GraphTraversal TraverseBound(this DatabaseEngine engine, string principalId, TraverseRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.TraverseRead(principalId, request.Partition, request.Graph, request.Start, request.MaxDepth, request.MaxVertices, request.MaxEdges, request.Labels?.ToArray(), budget);
    }

    internal static GraphShortestPathResult ShortestPathBound(this DatabaseEngine engine, string principalId, GraphShortestPathRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.ShortestPath(principalId, request, budget);
    }

    internal static GraphIncomingEdgesPage ReadIncomingGraphEdgesBound(this DatabaseEngine engine, string principalId, ReadIncomingGraphEdgesRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return engine.ReadIncomingGraphEdgesRead(principalId, request, engine.Clock, budget);
    }
}
