using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.Graph;

/// <summary>The only operations a graph database implements; thread-safe, shared by all clients.</summary>
internal interface IGraphDriver : IAsyncDisposable
{
    /// <summary>Vertices or edges per insert call during the load.</summary>
    int BatchSize { get; }

    /// <summary>Creates the graph and the vertex id index before any data is written.</summary>
    Task PrepareAsync(CancellationToken token);

    /// <summary>Inserts new vertices; returns after the database acknowledged them.</summary>
    Task InsertVerticesAsync(IReadOnlyList<GraphVertex> vertices, CancellationToken token);

    /// <summary>Inserts new directed edges between existing vertices.</summary>
    Task InsertEdgesAsync(IReadOnlyList<GraphEdge> edges, CancellationToken token);

    /// <summary>Returns the ids of the vertices one outgoing edge away, in any order.</summary>
    Task<IReadOnlyList<string>> NeighborsAsync(string vertex, CancellationToken token);

    /// <summary>Counts the distinct vertices other than the start reachable over 1..<paramref name="depth"/> outgoing edges.</summary>
    Task<int> CountReachableAsync(string vertex, int depth, CancellationToken token);
}

/// <summary>Creates the graph driver of the deployed database.</summary>
internal static class GraphDrivers
{
    internal static IGraphDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadGraphDriver(connection),
        BenchmarkDatabases.Neo4j => new Neo4jGraphDriver(connection),
        BenchmarkDatabases.HelixDb => new HelixGraphDriver(connection),
        BenchmarkDatabases.SurrealDb => new SurrealGraphDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.Graph + BenchmarkTypes.ForDatabase + connection.Database)
    };
}
