using System.Collections.Immutable;
using KeyLoad.Storage;
using GraphTraversalResult = global::KeyLoad.GraphTraversal;

namespace KeyLoad.Core.Features.GraphTraversal;

/// <summary>Runs bounded breadth-first traversal inside a single storage read gate.</summary>
internal sealed class GraphTraversalReader(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
    ResourceDefinition resource, PartitionRef partition, string graph, EntityRef start, int maxDepth,
    int maxVertices, int maxEdges, string[]? labels, ReadExecutionBudget budget)
{
    private const string AdjacencySpace = "adjacency";
    private const string EdgeSpace = "edge";
    private const string OutDirection = "out";
    private const string EdgeVisitExceeded = "The edge visit budget was exhausted.";
    private const string VertexVisitExceeded = "The vertex visit budget was exhausted.";
    private const string MissingEdge = "An adjacency record has no edge.";
    private const string ResultExceeded = "The graph result byte budget is exceeded.";
    private readonly GraphVertexVisibility visibility = new(database, view, principal, budget);
    private readonly HashSet<EntityRef> visited = [];
    private readonly Dictionary<string, (EdgeRecord Record, long Bytes)> edges = new(StringComparer.Ordinal);
    private readonly Queue<(EntityRef Vertex, int Depth)> pending = new();
    private readonly HashSet<string>? labelSet = labels is { Length: > 0 } ? new(labels, StringComparer.Ordinal) : null;
    private long resultLowerBound;
    private int examinedEdges;

    /// <summary>Returns deterministic qualified vertices and projected edges after exact response accounting.</summary>
    internal GraphTraversalResult Read()
    {
        visibility.RequireStart(start);
        AddVertex(start, 0);
        while (pending.TryDequeue(out var item))
        {
            budget.Check();
            if (item.Depth >= maxDepth)
            {
                continue;
            }
            VisitAdjacency(item);
        }
        var result = new GraphTraversalResult(visited.OrderBy(vertex => vertex.Collection, StringComparer.Ordinal)
            .ThenBy(vertex => vertex.Id, StringComparer.Ordinal).ToImmutableArray(),
            edges.Values.Select(entry => entry.Record).OrderBy(edge => edge.Id, StringComparer.Ordinal).ToImmutableArray());
        budget.CheckResult(result);
        return result;
    }

    private void VisitAdjacency((EntityRef Vertex, int Depth) item)
    {
        var prefix = KeySpace.Partition(AdjacencySpace, partition, graph, OutDirection,
            item.Vertex.Collection, item.Vertex.Id);
        var remaining = Math.Min(maxEdges, maxEdges - examinedEdges + 1);
        var scan = budget.VisitRange(view, prefix, remaining, (_, value) => VisitEdge(value, item.Depth));
        if (scan.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, EdgeVisitExceeded);
        }
    }

    private bool VisitEdge(ReadOnlySpan<byte> value, int depth)
    {
        if (++examinedEdges > maxEdges)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, EdgeVisitExceeded);
        }
        var id = NativeSerialization.Deserialize<string>(value);
        var edge = budget.ReadRecord<EdgeRecord>(view, KeySpace.Partition(EdgeSpace, partition, graph, id))
            ?? throw Errors.Fail(ErrorCode.Corruption, MissingEdge);
        if (labelSet is not null && !labelSet.Contains(edge.Label))
        {
            return true;
        }
        if (!visibility.CanVisit(edge.To))
        {
            return true;
        }
        AddEdge(edge);
        AddVertex(edge.To, depth + 1);
        return true;
    }

    private void AddEdge(EdgeRecord edge)
    {
        var replacing = edges.TryGetValue(edge.Id, out var previous);
        if (edges.Count >= maxEdges && !replacing)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, EdgeVisitExceeded);
        }
        var projected = edge with
        {
            AttributesJson = database.Authorization.Project(principal, resource.FieldPolicies, edge.AttributesJson, out _)
        };
        var bytes = budget.MeasureResult(projected);
        AddResultBytes(bytes, replacing ? previous.Bytes : 0);
        edges[edge.Id] = (projected, bytes);
    }

    private void AddVertex(EntityRef vertex, int depth)
    {
        if (visited.Contains(vertex))
        {
            return;
        }
        if (visited.Count >= maxVertices)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, VertexVisitExceeded);
        }
        AddResultBytes(budget.MeasureResult(vertex));
        visited.Add(vertex);
        pending.Enqueue((vertex, depth));
    }

    private void AddResultBytes(long bytes, long replacedBytes = 0)
    {
        var retainedBytes = resultLowerBound - replacedBytes;
        if (bytes > database.Limits.MaxBatchBytes - retainedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResultExceeded);
        }
        resultLowerBound = retainedBytes + bytes;
    }
}
