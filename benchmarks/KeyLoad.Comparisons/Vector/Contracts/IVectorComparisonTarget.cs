namespace KeyLoad.Comparisons;

/// <summary>Owns one target's actual execution vector database operations.</summary>
public interface IVectorComparisonTarget : IAsyncDisposable
{
    /// <summary>Gets the canonical target name.</summary>
    string Name
    {
        get;
    }
    /// <summary>Gets the actual execution target profile and topology evidence.</summary>
    TargetProfile Profile
    {
        get;
    }
    /// <summary>Returns whether this target ly implements the requested method and workload.</summary>
    /// <param name="indexKind">The execution index algorithm.</param>
    /// <param name="queryMode">The execution query predicate and update schedule.</param>
    /// <returns>Whether the execution contract is supported.</returns>
    bool Supports(VectorIndexKind indexKind, VectorQueryMode queryMode);
    /// <summary>Ingests a bounded lazy record stream through the target's execution storage.</summary>
    /// <param name="documents">The bounded lazy corpus stream.</param>
    /// <param name="cancellationToken">Cancels execution ingestion.</param>
    /// <returns>The acknowledged inserted record count.</returns>
    Task<int> IngestAsync(IAsyncEnumerable<VectorDocument> documents, CancellationToken cancellationToken);
    /// <summary>Builds the selected execution index and returns its actual build receipt.</summary>
    /// <param name="profile">The immutable execution profile.</param>
    /// <param name="cancellationToken">Cancels execution construction.</param>
    /// <returns>The observed execution build receipt.</returns>
    Task<VectorIndexReceipt> BuildIndexAsync(VectorComparisonProfile profile, CancellationToken cancellationToken);
    /// <summary>Reads back every persisted vector and payload in deterministic number order.</summary>
    /// <param name="cancellationToken">Cancels persisted readback.</param>
    /// <returns>Every stored record in number order.</returns>
    IAsyncEnumerable<VectorReadback> ReadbackAsync(CancellationToken cancellationToken);
    /// <summary>Executes execution top-k search with the selected execution predicate.</summary>
    /// <param name="query">The cosine query vector.</param>
    /// <param name="topK">The maximum neighbors.</param>
    /// <param name="mode">The execution eligibility predicate.</param>
    /// <param name="cancellationToken">Cancels execution search.</param>
    /// <returns>The actual execution neighbors.</returns>
    Task<IReadOnlyList<VectorNeighbor>> SearchAsync(ReadOnlyMemory<float> query, int topK,
        VectorQueryMode mode, CancellationToken cancellationToken);
    /// <summary>Returns the actual execution plan for the same query and predicate as the workload.</summary>
    /// <param name="query">The cosine query vector.</param>
    /// <param name="mode">The execution eligibility predicate.</param>
    /// <param name="cancellationToken">Cancels execution planning.</param>
    /// <returns>The actual measured query plan.</returns>
    Task<string> ExplainAsync(ReadOnlyMemory<float> query, VectorQueryMode mode, CancellationToken cancellationToken);
    /// <summary>Applies one execution embedding update.</summary>
    /// <param name="update">The deterministic excluded-record update.</param>
    /// <param name="cancellationToken">Cancels the execution update.</param>
    Task UpdateAsync(VectorUpdate update, CancellationToken cancellationToken);
    /// <summary>Reads one persisted vector row after an acknowledged update.</summary>
    /// <param name="id">The canonical record ID.</param>
    /// <param name="cancellationToken">Cancels the persisted read.</param>
    /// <returns>The persisted record or null if absent.</returns>
    Task<(bool HasValue, VectorReadback? Value)> ReadAsync(string id, CancellationToken cancellationToken);
}
