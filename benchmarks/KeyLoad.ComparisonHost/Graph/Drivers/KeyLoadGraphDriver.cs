using System.Collections.Immutable;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Graph;

/// <summary>
/// KeyLoad graph through the public SDK: vertices are <see cref="PutDocument"/> documents of one
/// <see cref="ResourceKind.Collection"/>, edges are <see cref="UpsertEdge"/> records of one <see cref="ResourceKind.Graph"/>
/// in the same partition, and both queries are one server-side breadth-first <see cref="TraverseRequest"/>: depth 1 for
/// neighbours, and the distinct vertices it visits within the depth for the reachable count, the start excluded.
/// </summary>
/// <remarks>
/// The traversal budgets are the most a full fan-out tree of the requested depth can visit, so a correct graph never
/// exhausts them; the server rejects a traversal that would.
/// </remarks>
internal sealed class KeyLoadGraphDriver(BenchmarkConnection connection) : IGraphDriver
{
    private const string Vertices = "vertices";
    private const string Links = "links";
    private const string Label = "link";
    private const string NoAttributes = "{}";
    private const string TraverseOperation = "traverse";
    private const long Absent = 0;
    private const int Batch = 256;
    private const int NeighborDepth = 1;
    private readonly KeyLoadDatabase database = new(connection);

    public int BatchSize => Batch;

    public async Task PrepareAsync(CancellationToken token)
    {
        await database.ConfigureAsync(database.Resource(Vertices, ResourceKind.Collection), token);
        await database.ConfigureAsync(database.Resource(Links, ResourceKind.Graph), token);
    }

    public Task InsertVerticesAsync(IReadOnlyList<GraphVertex> vertices, CancellationToken token)
        => database.CommitAsync([.. vertices.Select(vertex => new PutDocument(Vertices, vertex.Id, vertex.Json, Absent))], token);

    public Task InsertEdgesAsync(IReadOnlyList<GraphEdge> edges, CancellationToken token)
        => database.CommitAsync([.. edges.Select(Edge)], token);

    public async Task<IReadOnlyList<string>> NeighborsAsync(string vertex, CancellationToken token)
    {
        var visited = await TraverseAsync(vertex, NeighborDepth, token);
        return [.. visited.Where(found => found.Id != vertex).Select(found => found.Id)];
    }

    public async Task<int> CountReachableAsync(string vertex, int depth, CancellationToken token)
        => (await TraverseAsync(vertex, depth, token)).Count(found => found.Id != vertex);

    public ValueTask DisposeAsync() => database.DisposeAsync();

    private async Task<ImmutableArray<EntityRef>> TraverseAsync(string vertex, int depth, CancellationToken token)
    {
        var budget = Reach(depth);
        var request = new TraverseRequest(database.Partition, Links, database.Entity(Vertices, vertex), MaxDepth: depth,
            MaxVertices: budget, MaxEdges: budget * GraphDataset.FanOut);
        return KeyLoadDatabase.Require(await database.Client.TraverseAsync(request, token), TraverseOperation).Vertices;
    }

    private UpsertEdge Edge(GraphEdge edge)
        => new(Links, edge.Id, database.Entity(Vertices, edge.From), database.Entity(Vertices, edge.To), Label, NoAttributes,
            ExpectedRevision: Absent);

    /// <summary>The vertices of a full fan-out tree of <paramref name="depth"/> levels, the start included (1,111 for depth 3).</summary>
    private static int Reach(int depth)
    {
        var total = 1;
        var level = 1;
        for (var hop = 0; hop < depth; hop++)
        {
            level *= GraphDataset.FanOut;
            total += level;
        }
        return total;
    }
}
