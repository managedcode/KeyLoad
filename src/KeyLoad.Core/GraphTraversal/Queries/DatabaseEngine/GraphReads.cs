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
        => Traverse(engine, principalId, new TraverseRequest(partition, graph, start, maxDepth, maxVertices, maxEdges,
            labels is null ? null : [.. labels]), cancellationToken);

    /// <summary>Traverses the graph and materializes only the requested authorized projection.</summary>
    /// <param name="engine">The database execution owner.</param>
    /// <param name="principalId">Persisted subject whose permissions are read in this operation.</param>
    /// <param name="request">Traversal scope, work bounds and result projection.</param>
    /// <param name="cancellationToken">Cancellation throughout the original traversal.</param>
    /// <returns>The requested visible traversal result.</returns>
    public static global::KeyLoad.GraphTraversal Traverse(this DatabaseEngine engine, string principalId,
        TraverseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        ArgumentNullException.ThrowIfNull(request) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        var budget = new ReadExecutionBudget(cancellationToken);
        return engine.TraverseRead(principalId, request.Partition, request.Graph, request.Start, request.MaxDepth,
            request.MaxVertices, request.MaxEdges, request.Labels?.ToArray(), budget, request.Projection);
    }

    internal static global::KeyLoad.GraphTraversal TraverseRead(this DatabaseEngine engine, string principalId, PartitionRef partition, string graph, EntityRef start, int maxDepth, int maxVertices, int maxEdges, string[]? labels, ReadExecutionBudget budget,
        GraphTraversalProjection projection = GraphTraversalProjection.Full)
    {
        const string InvalidTraversalBudget = "The graph traversal budget is invalid.";
        const string InvalidProjection = "The graph traversal projection is invalid.";
        budget.Check();
        if (!Enum.IsDefined(projection))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidProjection);
        }
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

            return new GraphTraversalReader(engine, view, principal, resource, partition, graph, start, maxDepth,
                maxVertices, maxEdges, labels, budget, projection).Read();
        });
    }
}
