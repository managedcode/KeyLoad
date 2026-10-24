namespace KeyLoad.Comparisons.Targets;

public sealed partial class HelixDbTarget
{
    // HelixDB commits a write batch atomically but publishes no batch size (https://docs.helix-db.com/database/querying-guide/mutations.md); 1,000 nodes are about 1.3 MiB of the 16 MiB local body limit.
    private const int WriteBatchNodes = 1_000;
    /// <inheritdoc/>
    public int IngestionBatchRecords => WriteBatchNodes;
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        DocumentInitialization.Validate(initialization);
        return InitializeAsync(initialization.Corpus, token);
    }
    /// <inheritdoc/>
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
        => DocumentHttpSessionAcquisition.OpenAsync(http, executionOptions, async (owned, cancellation) =>
        {
            using var health = await owned.GetAsync(new Uri(HelixDbTokens.TokenReadyz, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
            health.EnsureSuccessStatusCode();
        },
            owned => new HelixDbSession(owned, label, DocumentMeasurementValues.ActualEnumeration, depth, execution, provider: timeProvider), token);
}
