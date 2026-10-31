using System.Globalization;

namespace KeyLoad.ComparisonHost.Graph;

/// <summary>A vertex of the standard graph; <paramref name="Json"/> is <c>{"id","number"}</c>.</summary>
internal sealed record GraphVertex(string Id, int Number, string Json);

/// <summary>A directed edge of the standard graph.</summary>
internal sealed record GraphEdge(string Id, string From, string To);

/// <summary>
/// The standard graph: one vertex per ten records and ten distinct outgoing edges per vertex, so 1M records give
/// 100,000 vertices and 1,000,000 edges.
/// </summary>
internal sealed class GraphDataset(int records)
{
    internal const int FanOut = 10;
    internal const int TraversalDepth = 3;
    private const string VertexPrefix = "g";
    private const string EdgeSeparator = "-";
    private const string Format = "D9";
    private const string JsonPrefix = "{\"id\":\"";
    private const string JsonMiddle = "\",\"number\":";
    private const string JsonSuffix = "}";

    internal int Vertices { get; } = records / FanOut;

    internal int Edges => Vertices * FanOut;

    internal static string VertexId(int number) => VertexPrefix + number.ToString(Format, CultureInfo.InvariantCulture);

    internal static GraphVertex Vertex(int number)
    {
        var id = VertexId(number);
        return new(id, number, JsonPrefix + id + JsonMiddle + number.ToString(CultureInfo.InvariantCulture) + JsonSuffix);
    }

    /// <summary>Edge <paramref name="index"/> is edge <c>index % 10</c> of vertex <c>index / 10</c>.</summary>
    internal GraphEdge Edge(int index)
    {
        var from = index / FanOut;
        return new(VertexId(from) + EdgeSeparator + (index % FanOut).ToString(CultureInfo.InvariantCulture),
            VertexId(from), VertexId(Target(from, index % FanOut)));
    }

    /// <summary>Targets are distinct and never the vertex itself.</summary>
    internal int Target(int vertex, int edge) => (vertex + 1 + (edge * ((Vertices - 1) / FanOut))) % Vertices;

    internal IEnumerable<int> Neighbors(int vertex) => Enumerable.Range(0, FanOut).Select(edge => Target(vertex, edge));

    /// <summary>The number of distinct vertices other than the start reachable in 1..depth hops.</summary>
    internal int Reachable(int start, int depth)
    {
        var seen = new HashSet<int> { start };
        var frontier = new List<int> { start };
        for (var hop = 0; hop < depth; hop++)
        {
            frontier = [.. frontier.SelectMany(Neighbors).Where(seen.Add)];
        }
        return seen.Count - 1;
    }
}
