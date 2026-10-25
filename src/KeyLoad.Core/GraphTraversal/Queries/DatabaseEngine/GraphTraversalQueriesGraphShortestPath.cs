using KeyLoad.Core.GraphTraversal;

namespace KeyLoad.Core;
/// <summary>Executes GraphTraversalQueriesGraphShortestPath operations against the caller's database context.</summary>
public static class GraphTraversalQueriesGraphShortestPath
{
    internal const int GraphShortestPathEmptyElementCount = 0;
    internal const string LabelField = "/label";
    /// <summary>Finds one bounded shortest directed path inside the current authorized read cut.</summary>
    /// <param name = "principalId">Identifies the persisted caller principal.</param>
    /// <param name = "request">Defines the graph path and finite traversal limits.</param>
    /// <param name = "timeProvider">Provides the operation clock used for the deadline.</param>
    /// <param name = "cancellationToken">Cancels parsing, storage work and result projection.</param>
    /// <returns>The ordered shortest path, or the privacy-preserving no-path result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static GraphShortestPathResult ShortestPath(this DatabaseEngine engine, string principalId, GraphShortestPathRequest request, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(timeProvider ?? engine.Clock, cancellationToken);
        return engine.ShortestPath(principalId, request, budget);
    }

    /// <summary>Executes the same path operator inside an existing budget and storage read cut.</summary>
    /// <param name = "principalId">Identifies the persisted caller principal.</param>
    /// <param name = "request">Defines the graph path and finite traversal limits.</param>
    /// <param name = "budget">The operation budget started by the owning request.</param>
    /// <returns>The ordered shortest path, or the privacy-preserving no-path result.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    internal static GraphShortestPathResult ShortestPath(this DatabaseEngine engine, string principalId, GraphShortestPathRequest request, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        var labels = GraphShortestPathValidation.Validate(request, budget, engine.GraphExecution);
        return engine.Store.Read(view =>
        {
            var budgetedView = budget.CreateView(view);
            var principal = engine.Principal(budgetedView, principalId, engine.Clock.GetUtcNow());
            engine.Authorization.Require(principal, request.Partition, request.Graph, Capability.GraphRead);
            var graph = ResourceLifetimeReadBinding.BindAuthorizedReadResource(budgetedView, request.Partition, request.Graph, ResourceKind.Graph);
            if (labels is { Length: > GraphShortestPathEmptyElementCount })
            {
                engine.Authorization.RequireFieldUse(principal, graph, LabelField);
            }

            return new GraphShortestPathReader(engine, view, principal, graph, request, labels, budget).Read();
        });
    }
}
