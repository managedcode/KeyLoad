using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.Vectors;

/// <summary>The only operations a vector database implements; thread-safe, shared by all clients.</summary>
internal interface IVectorDriver : IAsyncDisposable
{
    /// <summary>Vectors per <see cref="InsertAsync"/> call during the load.</summary>
    int BatchSize { get; }

    /// <summary>Creates the collection for cosine vectors of <paramref name="dimensions"/> components.</summary>
    Task PrepareAsync(int dimensions, CancellationToken token);

    /// <summary>Inserts vectors; returns after the database acknowledged them.</summary>
    Task InsertAsync(IReadOnlyList<VectorRecord> vectors, CancellationToken token);

    /// <summary>Exact (not approximate) cosine search: ids of the <paramref name="limit"/> nearest vectors.</summary>
    Task<IReadOnlyList<string>> NearestAsync(float[] query, int limit, CancellationToken token);
}

/// <summary>Creates the vector driver of the deployed database.</summary>
internal static class VectorDrivers
{
    internal static IVectorDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadVectorDriver(connection),
        BenchmarkDatabases.Qdrant => new QdrantVectorDriver(connection),
        BenchmarkDatabases.Postgres => new PgVectorDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.Vector + BenchmarkTypes.ForDatabase + connection.Database)
    };
}
