namespace KeyLoad.Comparisons;

/// <summary>A lazily generated deterministic vector document for native ingestion.</summary>
public sealed record VectorDocument(int Number, string Id, ReadOnlyMemory<float> Embedding, string Payload);
public sealed record VectorNeighbor(string Id, double Distance);
public sealed record VectorUpdate(int Number, string Id, ReadOnlyMemory<float> Embedding);
public sealed record VectorIndexReceipt(VectorIndexKind IndexKind, string Definition,
    IReadOnlyDictionary<string, string> Parameters, double BuildMilliseconds);
public sealed record VectorReadback(int Number, string Id, int Dimensions, string VectorSha256, string PayloadSha256);

/// <summary>Owns one target's actual native vector database operations.</summary>
public interface IVectorComparisonTarget : IAsyncDisposable
{
    string Name { get; }
    TargetProfile Profile { get; }
    bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode);
    Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken);
    Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile profile, CancellationToken cancellationToken);
    IAsyncEnumerable<VectorReadback> ReadbackAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken cancellationToken);
    Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken);
    Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken);
    Task<VectorReadback?> ReadAsync(string id, CancellationToken cancellationToken);
}
