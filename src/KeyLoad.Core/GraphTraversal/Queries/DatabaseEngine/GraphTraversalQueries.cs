using KeyLoad.Core.ResourceExecution;

namespace KeyLoad.Core;
/// <summary>Executes GraphTraversalQueries operations against the caller's database context.</summary>
public static class GraphTraversalQueries
{
    internal static global::KeyLoad.GraphTraversal TraverseBound(this DatabaseEngine engine, string principalId, TraverseRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        return engine.TraverseRead(principalId, request.Partition, request.Graph, request.Start, request.MaxDepth,
            request.MaxVertices, request.MaxEdges, request.Labels?.ToArray(), budget, request.Projection);
    }

    internal static GraphShortestPathResult ShortestPathBound(this DatabaseEngine engine, string principalId, GraphShortestPathRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        return engine.ShortestPath(principalId, request, budget);
    }

    internal static GraphIncomingEdgesPage ReadIncomingGraphEdgesBound(this DatabaseEngine engine, string principalId, ReadIncomingGraphEdgesRequest request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(budget) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(binding) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        budget.AttachResourceLifetimeBinding(binding);
        return engine.ReadIncomingGraphEdgesRead(principalId, request, engine.Clock, budget);
    }
}
