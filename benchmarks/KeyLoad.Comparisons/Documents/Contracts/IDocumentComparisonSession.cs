namespace KeyLoad.Comparisons;

/// <summary>A real execution client's identity and unrestricted ordered actual-state readback.</summary>
public interface IDocumentComparisonSession : IComparisonSession
{
    /// <summary>Identity assigned once to the actual independently owned client, never to borrowed wrappers.</summary>
    Guid ClientIdentity
    {
        get;
    }
    /// <summary>True only when the operation contains no verification read in addition to the stated operation.</summary>
    bool SingleOperationTiming
    {
        get;
    }
    /// <summary>Enumerates actual stored document identities in ordinal order; the runner owns the final-state oracle.</summary>
    IAsyncEnumerable<FoundDocument> ReadDocumentsAsync(CancellationToken cancellationToken);
    /// <summary>Creates new documents through the database's bulk API with the same create-only acknowledgement as a single create.</summary>
    /// <param name="documents">At most <see cref="IDocumentComparisonTarget.IngestionBatchRecords"/> documents that must not exist yet.</param>
    /// <param name="cancellationToken">A token used to cancel the request.</param>
    /// <returns>A task that completes after the database acknowledged every document of the batch.</returns>
    Task CreateDocumentsAsync(IReadOnlyList<BenchmarkDocument> documents, CancellationToken cancellationToken);
}
