namespace KeyLoad.Comparisons;

/// <summary>A target session that creates a bounded batch of new documents in one bulk request.</summary>
public interface IDocumentBulkSession
{
    /// <summary>Creates new documents with the same create-only acknowledgement contract as a single create.</summary>
    /// <param name="documents">The documents to create; none of them exists yet.</param>
    /// <param name="cancellationToken">A token used to cancel the request.</param>
    /// <returns>A task that completes after the database acknowledged every document of the batch.</returns>
    Task CreateDocumentsAsync(IReadOnlyList<BenchmarkDocument> documents, CancellationToken cancellationToken);
}
