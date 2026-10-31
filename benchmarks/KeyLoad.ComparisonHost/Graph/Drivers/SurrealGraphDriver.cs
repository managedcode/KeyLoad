using System.Globalization;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Graph;

/// <summary>
/// SurrealDB graph through SurrealQL over HTTP (<see cref="SurrealSql"/>): vertices are <c>vertex</c> records keyed by the
/// vertex id, edges are <c>link</c> relation records written with <c>INSERT RELATION</c>, neighbours are the
/// <c>-&gt;link-&gt;vertex</c> traversal and the reachable count is computed by SurrealQL over the 1..depth hop paths
/// (<c>array::distinct</c>, <c>array::complement</c> of the start, <c>array::len</c>).
/// </summary>
internal sealed class SurrealGraphDriver(BenchmarkConnection connection) : IGraphDriver
{
    private const string Vertex = "vertex";
    private const string Link = "link";
    private const string Hop = "->" + Link + "->" + Vertex;
    private const string Define = "DEFINE TABLE " + Vertex + " SCHEMALESS; DEFINE TABLE " + Link + " SCHEMALESS;";
    private const string InsertVertices = "INSERT INTO " + Vertex + " [";
    private const string InsertLinks = "INSERT RELATION INTO " + Link + " [";
    private const string ReturnNone = "] RETURN NONE;";
    private const string IdField = "{ id: ";
    private const string NumberField = ", number: ";
    private const string InField = ", in: ";
    private const string OutField = ", out: ";
    private const string RecordEnd = " }";
    private const string NeighborsFrom = "SELECT VALUE " + Hop + " FROM ";
    private const string ReachablePaths = "RETURN array::len(array::complement(array::distinct(array::flatten((SELECT VALUE [";
    private const string ReachableFrom = "] FROM ";
    private const string ReachableExcept = ")[0])), [";
    private const string ReachableEnd = "]));";
    private const string StatementEnd = ";";
    private const string Separator = ", ";
    private const string MissingVertex = "SurrealDB has no vertex ";
    private const int Batch = 256;
    private const int FirstStatement = 0;
    private readonly SurrealSql sql = new(connection);

    public int BatchSize => Batch;

    public Task PrepareAsync(CancellationToken token) => sql.ExecuteAsync(Define, token);

    public Task InsertVerticesAsync(IReadOnlyList<GraphVertex> vertices, CancellationToken token)
        => sql.ExecuteAsync(InsertVertices + string.Join(Separator, vertices.Select(VertexRecord)) + ReturnNone, token);

    public Task InsertEdgesAsync(IReadOnlyList<GraphEdge> edges, CancellationToken token)
        => sql.ExecuteAsync(InsertLinks + string.Join(Separator, edges.Select(LinkRecord)) + ReturnNone, token);

    public async Task<IReadOnlyList<string>> NeighborsAsync(string vertex, CancellationToken token)
    {
        using var response = await sql.QueryAsync(NeighborsFrom + SurrealSql.Record(Vertex, vertex) + StatementEnd, token);
        var rows = SurrealSql.Result(response, FirstStatement);
        if (rows.GetArrayLength() == 0)
        {
            throw new KeyNotFoundException(MissingVertex + vertex);
        }
        return [.. rows[0].EnumerateArray().Select(id => SurrealSql.Key(Vertex, id))];
    }

    public async Task<int> CountReachableAsync(string vertex, int depth, CancellationToken token)
    {
        var start = SurrealSql.Record(Vertex, vertex);
        var paths = Enumerable.Range(1, depth).Select(hops => string.Concat(Enumerable.Repeat(Hop, hops)));
        var statement = ReachablePaths + string.Join(Separator, paths) + ReachableFrom + start + ReachableExcept + start + ReachableEnd;
        using var response = await sql.QueryAsync(statement, token);
        return SurrealSql.Result(response, FirstStatement).GetInt32();
    }

    public ValueTask DisposeAsync()
    {
        sql.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string VertexRecord(GraphVertex vertex)
        => IdField + SurrealSql.Record(Vertex, vertex.Id) + NumberField + vertex.Number.ToString(CultureInfo.InvariantCulture) + RecordEnd;

    private static string LinkRecord(GraphEdge edge)
        => IdField + SurrealSql.Record(Link, edge.Id) + InField + SurrealSql.Record(Vertex, edge.From) + OutField
            + SurrealSql.Record(Vertex, edge.To) + RecordEnd;
}
