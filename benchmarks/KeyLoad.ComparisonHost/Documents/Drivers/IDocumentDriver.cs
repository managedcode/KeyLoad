using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.Documents;

/// <summary>
/// The only operations a document database implements. The dataset, schedule and checks live in
/// <see cref="DocumentScenario"/>; a driver is thread-safe because all clients share it.
/// </summary>
internal interface IDocumentDriver : IAsyncDisposable
{
    /// <summary>Documents per <see cref="InsertAsync"/> call during the load.</summary>
    int BatchSize { get; }

    /// <summary>Creates the collection and a secondary index on <c>number</c>, before any document is written.</summary>
    Task PrepareAsync(CancellationToken token);

    /// <summary>Inserts new documents; returns after the database acknowledged them.</summary>
    Task InsertAsync(IReadOnlyList<DocumentRecord> documents, CancellationToken token);

    /// <summary>Reads the stored JSON body; throws <see cref="KeyNotFoundException"/> when the id is missing.</summary>
    Task<string> ReadAsync(string id, CancellationToken token);

    /// <summary>Returns the ids of all documents whose <c>number</c> is in <c>[from, to]</c>, in any order.</summary>
    Task<IReadOnlyList<string>> FindRangeAsync(long from, long to, CancellationToken token);

    /// <summary>Replaces the whole body of an existing document.</summary>
    Task ReplaceAsync(DocumentRecord document, CancellationToken token);

    /// <summary>Deletes an existing document.</summary>
    Task DeleteAsync(string id, CancellationToken token);
}

/// <summary>Creates the documents driver of the deployed database.</summary>
internal static class DocumentDrivers
{
    internal static IDocumentDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadDocumentDriver(connection),
        BenchmarkDatabases.Mongo => new MongoDocumentDriver(connection),
        BenchmarkDatabases.SurrealDb => new SurrealDocumentDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.Documents + BenchmarkTypes.ForDatabase + connection.Database)
    };
}
