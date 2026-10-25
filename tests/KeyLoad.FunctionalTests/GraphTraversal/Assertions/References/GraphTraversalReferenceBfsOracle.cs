using System.Collections.Immutable;

namespace KeyLoad.FunctionalTests.GraphTraversal;

/// <summary>Computes a test-owned BFS without using production graph readers or key encodings.</summary>
internal static class GraphTraversalReferenceBfsOracle
{
    internal static GraphTraversalReferenceResult Read(
        IReadOnlyCollection<GraphTraversalReferenceEdge> graph,
        IReadOnlySet<GraphTraversalReferenceVertex> visibleVertices,
        GraphTraversalReferenceVertex start,
        int maxDepth,
        int maxVertices,
        int maxEdges,
        string[]? labels = null)
    {
        var adjacency = Adjacency(graph);
        var allowedLabels = labels is { Length: > 0 }
            ? new HashSet<string>(labels, StringComparer.Ordinal)
            : null;
        var depths = new Dictionary<GraphTraversalReferenceVertex, int> { [start] = 0 };
        var pending = new Queue<GraphTraversalReferenceVertex>();
        var traversedEdges = new Dictionary<string, GraphTraversalReferenceEdge>(StringComparer.Ordinal);
        var examinedEdges = 0;
        pending.Enqueue(start);

        while (pending.TryDequeue(out var current))
        {
            var depth = depths[current];
            if (depth >= maxDepth || !adjacency.TryGetValue(current, out var outgoing))
            {
                continue;
            }

            foreach (var edge in outgoing)
            {
                if (++examinedEdges > maxEdges)
                {
                    return Exceeded(examinedEdges);
                }
                if (allowedLabels is not null && !allowedLabels.Contains(edge.Label)
                    || !visibleVertices.Contains(edge.To))
                {
                    continue;
                }

                traversedEdges[edge.Id] = edge;
                if (depths.ContainsKey(edge.To))
                {
                    continue;
                }
                if (depths.Count >= maxVertices)
                {
                    return Exceeded(examinedEdges);
                }

                RememberVertex(depths, pending, edge.To, depth);
            }
        }

        return Result(depths, traversedEdges, examinedEdges);
    }

    private static void RememberVertex(Dictionary<GraphTraversalReferenceVertex, int> depths,
        Queue<GraphTraversalReferenceVertex> pending, GraphTraversalReferenceVertex vertex, int previousDepth)
    {
        depths.Add(vertex, checked(previousDepth + 1));
        pending.Enqueue(vertex);
    }

    private static Dictionary<GraphTraversalReferenceVertex, GraphTraversalReferenceEdge[]> Adjacency(
        IReadOnlyCollection<GraphTraversalReferenceEdge> graph)
        => graph.GroupBy(edge => edge.From).ToDictionary(group => group.Key,
            group => group.OrderBy(edge => edge.Id, StringComparer.Ordinal).ToArray());

    private static GraphTraversalReferenceResult Result(
        Dictionary<GraphTraversalReferenceVertex, int> depths,
        Dictionary<string, GraphTraversalReferenceEdge> traversedEdges, int examinedEdges)
        => new(
            [.. depths.Keys.OrderBy(vertex => vertex.Collection, StringComparer.Ordinal)
                .ThenBy(vertex => vertex.Id, StringComparer.Ordinal)],
            [.. traversedEdges.Values.OrderBy(edge => edge.Id, StringComparer.Ordinal)],
            depths.ToImmutableDictionary(),
            examinedEdges,
            false);

    private static GraphTraversalReferenceResult Exceeded(int examinedEdges)
        => new([], [], ImmutableDictionary<GraphTraversalReferenceVertex, int>.Empty, examinedEdges, true);
}
