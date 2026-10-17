namespace KeyLoad.Comparisons.Targets;

public sealed partial class SurrealDbTarget
{
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        DocumentNativeInitialization.Validate(initialization);
        return InitializeAsync(initialization.Corpus, token);
    }
    /// <inheritdoc/>
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
        => DocumentHttpSessionAcquisition.OpenAsync(http, executionOptions, async (owned, cancellation) => { _ = await SurrealDbServer.VerifyAsync(owned, Policy, token: cancellation, timeProvider: timeProvider).ConfigureAwait(false); },
            owned => new SurrealDbSession(owned, table, edge, DocumentMeasurementValues.NativeActualEnumeration, depth, topK, execution, provider: timeProvider), token);
}
