namespace KeyLoad.Comparisons.Targets;

public sealed partial class Neo4jTarget
{
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        return DocumentNativeInitialization.InitializeAsync(this, initialization, token);
    }
    /// <inheritdoc/>
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
        => DocumentHttpSessionAcquisition.OpenAsync(http, nativeExecutionOptions, async (owned, cancellation) => { using var admission = await QueryWithClientAsync(owned, DocumentProtocolText.RETURN1, null, cancellation).ConfigureAwait(false); },
            owned => new Session(this, owned, DocumentMeasurementValues.NativeActualEnumeration), token);
}
