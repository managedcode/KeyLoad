namespace KeyLoad.Comparisons.Targets;

public sealed partial class HelixDbTarget
{
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
