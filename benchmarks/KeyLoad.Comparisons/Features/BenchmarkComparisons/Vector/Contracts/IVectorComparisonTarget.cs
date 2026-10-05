namespace KeyLoad.Comparisons;

/// <summary>A lazily generated deterministic vector document for native ingestion.</summary>
/// <param name="Number">The deterministic corpus number.</param>
/// <param name="Id">The canonical ordinal record ID.</param>
/// <param name="Embedding">The owned float32 vector.</param>
/// <param name="Payload">The exact UTF8 payload.</param>
public sealed record VectorDocument(int Number, string Id, ReadOnlyMemory<float> Embedding, string Payload);
/// <summary>A document returned by native nearest-neighbor search.</summary>
/// <param name="Id">The native document ID.</param>
/// <param name="Distance">The native cosine distance.</param>
public sealed record VectorNeighbor(string Id, double Distance);
/// <summary>A deterministic embedding update in the mixed profile.</summary>
/// <param name="Number">The corpus number being updated.</param>
/// <param name="Id">The canonical record ID.</param>
/// <param name="Embedding">The replacement embedding.</param>
public sealed record VectorUpdate(int Number, string Id, ReadOnlyMemory<float> Embedding);
/// <summary>Actual native index creation receipt.</summary>
/// <param name="IndexKind">The method created by the database.</param>
/// <param name="Definition">The native index definition.</param>
/// <param name="Parameters">The actual index and query parameters.</param>
/// <param name="BuildMilliseconds">The observed index build duration.</param>
public sealed record VectorIndexReceipt(VectorIndexKind IndexKind, string Definition,
    IReadOnlyDictionary<string, string> Parameters, double BuildMilliseconds);
/// <summary>Native vector and payload values read back from persisted database state.</summary>
/// <param name="Number">The persisted corpus number.</param>
/// <param name="Id">The persisted record ID.</param>
/// <param name="Dimensions">The persisted vector dimension.</param>
/// <param name="VectorSha256">SHA256 over ordered little-endian float32 bits.</param>
/// <param name="PayloadSha256">SHA256 over persisted UTF8 payload bytes.</param>
public sealed record VectorReadback(int Number, string Id, int Dimensions, string VectorSha256, string PayloadSha256);

/// <summary>Owns one target's actual native vector database operations.</summary>
public interface IVectorComparisonTarget : IAsyncDisposable
{
    /// <summary>Gets the canonical target name.</summary>
    string Name { get; }
    /// <summary>Gets the actual native target profile and topology evidence.</summary>
    TargetProfile Profile { get; }
    /// <summary>Returns whether this target natively implements the requested method and workload.</summary>
    /// <param name="indexKind">The native index algorithm.</param>
    /// <param name="queryMode">The native query predicate and update schedule.</param>
    /// <returns>Whether the native contract is supported.</returns>
    bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode);
    /// <summary>Ingests a bounded lazy record stream through the target's native storage.</summary>
    /// <param name="documents">The bounded lazy corpus stream.</param>
    /// <param name="cancellationToken">Cancels native ingestion.</param>
    /// <returns>The acknowledged inserted record count.</returns>
    Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken);
    /// <summary>Builds the selected native index and returns its actual build receipt.</summary>
    /// <param name="profile">The immutable native profile.</param>
    /// <param name="cancellationToken">Cancels native construction.</param>
    /// <returns>The observed native build receipt.</returns>
    Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile profile, CancellationToken cancellationToken);
    /// <summary>Reads back every persisted vector and payload in deterministic number order.</summary>
    /// <param name="cancellationToken">Cancels persisted readback.</param>
    /// <returns>Every stored record in number order.</returns>
    IAsyncEnumerable<VectorReadback> ReadbackAsync(CancellationToken cancellationToken);
    /// <summary>Executes native top-k search with the selected native predicate.</summary>
    /// <param name="query">The cosine query vector.</param>
    /// <param name="topK">The maximum neighbors.</param>
    /// <param name="mode">The native eligibility predicate.</param>
    /// <param name="cancellationToken">Cancels native search.</param>
    /// <returns>The actual native neighbors.</returns>
    Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken cancellationToken);
    /// <summary>Returns the actual native plan for the same query and predicate as the workload.</summary>
    /// <param name="query">The cosine query vector.</param>
    /// <param name="mode">The native eligibility predicate.</param>
    /// <param name="cancellationToken">Cancels native planning.</param>
    /// <returns>The actual measured query plan.</returns>
    Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken);
    /// <summary>Applies one native embedding update.</summary>
    /// <param name="update">The deterministic excluded-record update.</param>
    /// <param name="cancellationToken">Cancels the native update.</param>
    Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken);
    /// <summary>Reads one persisted vector row after an acknowledged update.</summary>
    /// <param name="id">The canonical record ID.</param>
    /// <param name="cancellationToken">Cancels the persisted read.</param>
    /// <returns>The persisted record or null if absent.</returns>
    Task<VectorReadback?> ReadAsync(string id, CancellationToken cancellationToken);
}
