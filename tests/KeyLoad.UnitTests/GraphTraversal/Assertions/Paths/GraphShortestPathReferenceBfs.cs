namespace KeyLoad.UnitTests.Features.GraphTraversal;

/// <summary>Independent directed BFS over test-owned logical vertices and edges.</summary>
internal static class GraphShortestPathReferenceBfs
{
    private const int StartDepth = 0;

    internal static GraphShortestPathReference Find(
        IReadOnlyCollection<GraphShortestPathReferenceEdge> graph,
        GraphShortestPathReferenceVertex start,
        GraphShortestPathReferenceVertex target,
        int maxDepth,
        IReadOnlySet<GraphShortestPathReferenceVertex>? visible = null,
        IReadOnlySet<string>? labels = null)
    {
        if (start == target)
        {
            return new(true, [start], [], 0);
        }
        var previous = new Dictionary<GraphShortestPathReferenceVertex, GraphShortestPathReferenceEdge>();
        var depths = new Dictionary<GraphShortestPathReferenceVertex, int> { [start] = StartDepth };
        var pending = new Queue<GraphShortestPathReferenceVertex>();
        var examinedEdges = 0;
        pending.Enqueue(start);
        while (pending.TryDequeue(out var current))
        {
            var depth = depths[current];
            if (depth >= maxDepth)
            {
                continue;
            }
            foreach (var edge in graph.Where(candidate => candidate.From == current)
                         .OrderBy(candidate => candidate.Id, StringComparer.Ordinal))
            {
                examinedEdges++;
                if (labels is { Count: > 0 } && !labels.Contains(edge.Label)
                    || visible is not null && !visible.Contains(edge.To)
                    || depths.ContainsKey(edge.To))
                {
                    continue;
                }
                previous.Add(edge.To, edge);
                depths.Add(edge.To, checked(depth + 1));
                if (edge.To == target)
                {
                    return BuildPath(start, target, previous, examinedEdges);
                }
                pending.Enqueue(edge.To);
            }
        }
        return new(false, [], [], examinedEdges);
    }

    private static GraphShortestPathReference BuildPath(
        GraphShortestPathReferenceVertex start,
        GraphShortestPathReferenceVertex target,
        Dictionary<GraphShortestPathReferenceVertex, GraphShortestPathReferenceEdge> previous,
        int examinedEdges)
    {
        var vertices = new List<GraphShortestPathReferenceVertex> { target };
        var edges = new List<GraphShortestPathReferenceEdge>();
        var current = target;
        while (current != start)
        {
            var edge = previous[current];
            edges.Add(edge);
            current = edge.From;
            vertices.Add(current);
        }
        vertices.Reverse();
        edges.Reverse();
        return new(true, [.. vertices], [.. edges], examinedEdges);
    }
}
