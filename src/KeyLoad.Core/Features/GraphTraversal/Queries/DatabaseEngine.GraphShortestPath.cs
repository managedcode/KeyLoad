using KeyLoad.Core.Features.GraphTraversal;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string LabelField = "/label";

    /// <summary>Finds one bounded shortest directed path inside the current authorized read cut.</summary>
    /// <param name="principalId">Identifies the persisted caller principal.</param>
    /// <param name="request">Defines the graph path and finite traversal limits.</param>
    /// <param name="timeProvider">Provides the operation clock used for the deadline.</param>
    /// <param name="cancellationToken">Cancels parsing, storage work and result projection.</param>
    /// <returns>The ordered shortest path, or the privacy-preserving no-path result.</returns>
    public GraphShortestPathResult ShortestPath(string principalId, GraphShortestPathRequest request,
        TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
    {
        var budget = new ReadExecutionBudget(Limits, timeProvider ?? Clock, cancellationToken);
        return ShortestPath(principalId, request, budget);
    }

    /// <summary>Executes the same path operator inside an existing budget and storage read cut.</summary>
    /// <param name="principalId">Identifies the persisted caller principal.</param>
    /// <param name="request">Defines the graph path and finite traversal limits.</param>
    /// <param name="budget">The operation budget started by the owning request.</param>
    /// <returns>The ordered shortest path, or the privacy-preserving no-path result.</returns>
    internal GraphShortestPathResult ShortestPath(string principalId, GraphShortestPathRequest request,
        ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var labels = GraphShortestPathValidation.Validate(request, Limits, budget, graphExecution);
        return Store.Read(view =>
        {
            var budgetedView = budget.CreateView(view);
            var principal = Principal(budgetedView, principalId, Clock.GetUtcNow());
            Authorization.Require(principal, request.Partition, request.Graph, Capability.GraphRead);
            var graph = Resource(budgetedView, request.Partition, request.Graph, ResourceKind.Graph);
            if (labels is { Length: > 0 })
            {
                Authorization.RequireFieldUse(principal, graph, LabelField);
            }
            return new GraphShortestPathReader(this, view, principal, graph, request, labels, budget).Read();
        });
    }
}
