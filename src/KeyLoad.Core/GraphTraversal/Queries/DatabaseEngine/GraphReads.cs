using KeyLoad.Core.GraphTraversal;

namespace KeyLoad.Core;
/// <summary>Executes GraphReads operations against the caller's database context.</summary>
public static class GraphReads
{
    internal const int DefaultTraversalDepth = 3;
    internal const int DefaultTraversalVertices = 1_000;
    internal const int DefaultTraversalEdges = 5_000;
    internal const int MinimumTraversalDepth = 0;
    internal const int MinimumTraversalVertices = 1;
    internal const int MinimumTraversalEdges = 1;
    internal const int EmptyTraversalLabels = 0;
    /// <summary>Traverses visible graph edges in breadth-first order within one committed read cut.</summary>
    /// <param name = "principalId">Persisted caller identity.</param>
    /// <param name = "partition">Atomic graph partition.</param>
    /// <param name = "graph">Configured graph resource.</param>
    /// <param name = "start">Qualified starting vertex, whose errors propagate.</param>
    /// <param name = "maxDepth">Maximum edge depth from the start.</param>
    /// <param name = "maxVertices">Maximum retained visible vertices.</param>
    /// <param name = "maxEdges">Maximum examined adjacency edges, including hidden and filtered edges.</param>
    /// <param name = "labels">Optional allowed edge labels.</param>
    /// <param name = "cancellationToken">Cancellation across storage and result work.</param>
    /// <returns>Qualified vertices and projected edges in deterministic order.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static global::KeyLoad.GraphTraversal Traverse(this DatabaseEngine engine, string principalId, PartitionRef partition, string graph, EntityRef start, int maxDepth = DefaultTraversalDepth, int maxVertices = DefaultTraversalVertices, int maxEdges = DefaultTraversalEdges, string[]? labels = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(engine.OperationLimitsOptions, engine.Clock, cancellationToken);
        return engine.TraverseRead(principalId, partition, graph, start, maxDepth, maxVertices, maxEdges, labels, budget);
    }

    internal static global::KeyLoad.GraphTraversal TraverseRead(this DatabaseEngine engine, string principalId, PartitionRef partition, string graph, EntityRef start, int maxDepth, int maxVertices, int maxEdges, string[]? labels, ReadExecutionBudget budget)
    {
        const string InvalidTraversalBudget = "The graph traversal budget is invalid.";
        budget.Check();
        return engine.Store.Read(view =>
        {
            if (maxDepth < MinimumTraversalDepth || maxDepth > engine.GraphExecution.MaximumDepth || maxVertices < MinimumTraversalVertices || maxVertices > engine.GraphExecution.MaximumVertices || maxEdges < MinimumTraversalEdges || maxEdges > engine.GraphExecution.MaximumEdges || start.Partition != partition)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidTraversalBudget);
            }

            var metadata = budget.CreateView(view);
            var principal = engine.Principal(metadata, principalId, engine.Clock.GetUtcNow());
            engine.Authorization.Require(principal, partition, graph, Capability.GraphRead);
            var resource = ResourceLifetimeReadBinding.BindAuthorizedReadResource(metadata, partition, graph, ResourceKind.Graph);
            if (labels is { Length: > EmptyTraversalLabels })
            {
                engine.Authorization.RequireFieldUse(principal, resource, GraphTraversalQueriesGraphShortestPath.LabelField);
            }

            return new GraphTraversalReader(engine, view, principal, resource, partition, graph, start, maxDepth, maxVertices, maxEdges, labels, budget).Read();
        });
    }
}
