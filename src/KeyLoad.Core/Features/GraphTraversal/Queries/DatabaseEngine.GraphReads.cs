using KeyLoad.Core.Features.GraphTraversal;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int DefaultTraversalDepth = 3;
    private const int DefaultTraversalVertices = 1_000;
    private const int DefaultTraversalEdges = 5_000;
    private const int MinimumTraversalDepth = 0;
    private const int MinimumTraversalVertices = 1;
    private const int MinimumTraversalEdges = 1;
    /// <summary>Traverses visible graph edges in breadth-first order within one committed read cut.</summary>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="partition">Atomic graph partition.</param>
    /// <param name="graph">Configured graph resource.</param>
    /// <param name="start">Qualified starting vertex, whose errors propagate.</param>
    /// <param name="maxDepth">Maximum edge depth from the start.</param>
    /// <param name="maxVertices">Maximum retained visible vertices.</param>
    /// <param name="maxEdges">Maximum examined adjacency edges, including hidden and filtered edges.</param>
    /// <param name="labels">Optional allowed edge labels.</param>
    /// <param name="cancellationToken">Cancellation across storage and result work.</param>
    /// <returns>Qualified vertices and projected edges in deterministic order.</returns>
    public GraphTraversal Traverse(string principalId, PartitionRef partition, string graph, EntityRef start,
        int maxDepth = DefaultTraversalDepth, int maxVertices = DefaultTraversalVertices, int maxEdges = DefaultTraversalEdges, string[]? labels = null,
        CancellationToken cancellationToken = default)
    {
        const string InvalidTraversalBudget = "The graph traversal budget is invalid.";
        var budget = new ReadExecutionBudget(OperationLimitsOptions, Clock, cancellationToken);
        budget.Check();
        return Store.Read(view =>
        {
            if (maxDepth < MinimumTraversalDepth || maxDepth > graphExecution.MaximumDepth
                || maxVertices < MinimumTraversalVertices || maxVertices > graphExecution.MaximumVertices
                || maxEdges < MinimumTraversalEdges || maxEdges > graphExecution.MaximumEdges || start.Partition != partition)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidTraversalBudget);
            }
            var principal = Principal(view, principalId, Clock.GetUtcNow());
            Authorization.Require(principal, partition, graph, Capability.GraphRead);
            var resource = Resource(view, partition, graph, ResourceKind.Graph);
            return new GraphTraversalReader(this, view, principal, resource, partition, graph, start,
                maxDepth, maxVertices, maxEdges, labels, budget).Read();
        });
    }

}
