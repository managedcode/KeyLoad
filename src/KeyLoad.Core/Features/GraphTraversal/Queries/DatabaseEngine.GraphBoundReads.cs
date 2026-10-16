using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal GraphTraversal TraverseBound(string principalId, TraverseRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return TraverseRead(principalId, request.Partition, request.Graph, request.Start, request.MaxDepth,
            request.MaxVertices, request.MaxEdges, request.Labels?.ToArray(), budget);
    }

    internal GraphShortestPathResult ShortestPathBound(string principalId, GraphShortestPathRequest request,
        ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return ShortestPath(principalId, request, budget);
    }

    internal GraphIncomingEdgesPageV1 ReadIncomingGraphEdgesBound(string principalId,
        ReadIncomingGraphEdgesRequestV1 request, ReadExecutionBudget budget, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(binding);
        budget.AttachResourceLifetimeBinding(binding);
        return ReadIncomingGraphEdgesRead(principalId, request, Clock, budget);
    }
}
