namespace KeyLoad.Comparisons.Targets;

public sealed partial class OpenSearchTarget
{
    // Bound each bulk request independently of the total corpus and the number of active clients.
    private const int BulkRequestDocuments = 1_000;
    /// <inheritdoc/>
    public int IngestionBatchRecords => BulkRequestDocuments;
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        DocumentInitialization.Validate(initialization);
        return InitializeAsync(initialization.Corpus, token);
    }
    /// <inheritdoc/>
    public async Task VerifyDocumentCopiesAsync(DocumentComparisonSchedule schedule, CancellationToken cancellationToken)
    {
        Profile = Profile with
        {
            Cluster = await OpenSearchDocumentCopyProof.VerifyAsync(client, index, expectedCopies,
            topology, Profile, schedule, executionOptions, cancellationToken).ConfigureAwait(false)
        };
    }
    /// <inheritdoc/>
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
        => DocumentHttpSessionAcquisition.OpenAsync(client, executionOptions, AdmitDocumentSessionAsync,
            owned => new OpenSearchSession(owned, index, topK, expectedCopies,
                -DocumentMeasurementValues.SingleItemCount, executionOptions), token, readback: RefreshAndReadDocumentsAsync);
    private static async Task AdmitDocumentSessionAsync(HttpClient owned, CancellationToken token)
    {
        using var admission = await OpenSearchHttp.SendJsonAsync(owned, HttpMethod.Get,
            OpenSearchNames.PathSeparator, null, token).ConfigureAwait(false);
        _ = admission.RootElement;
    }
    private async IAsyncEnumerable<FoundDocument> RefreshAndReadDocumentsAsync(HttpClient owned, IComparisonSession session,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        using (var refresh = await OpenSearchHttp.SendJsonAsync(owned, HttpMethod.Post,
            OpenSearchNames.PathSeparator + index + OpenSearchNames.RefreshSuffix, null, token).ConfigureAwait(false))
        {
            _ = refresh.RootElement;
        }
        await foreach (var row in session.ReadCorpusAsync(token).ConfigureAwait(false))
        {
            yield return row;
        }
    }
}
