using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.Search;

/// <summary>The only operations a full-text engine implements; thread-safe, shared by all clients.</summary>
internal interface ISearchDriver : IAsyncDisposable
{
    /// <summary>Documents per <see cref="InsertAsync"/> call during the load.</summary>
    int BatchSize { get; }

    /// <summary>Creates the index or collection with one full-text field <c>text</c>.</summary>
    Task PrepareAsync(CancellationToken token);

    /// <summary>Inserts documents; returns after the engine acknowledged them.</summary>
    Task InsertAsync(IReadOnlyList<SearchDocument> documents, CancellationToken token);

    /// <summary>Builds or refreshes the full-text index; returns once every inserted document is searchable.</summary>
    Task BuildIndexAsync(CancellationToken token);

    /// <summary>Full-text search with the engine's own ranking: ids of up to <paramref name="limit"/> documents containing every term.</summary>
    Task<IReadOnlyList<string>> SearchAsync(IReadOnlyList<string> terms, int limit, CancellationToken token);
}

/// <summary>Creates the full-text driver of the deployed database.</summary>
internal static class SearchDrivers
{
    internal static ISearchDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadSearchDriver(connection),
        BenchmarkDatabases.OpenSearch => new OpenSearchSearchDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.Search + BenchmarkTypes.ForDatabase + connection.Database)
    };
}
