namespace KeyLoad.Comparisons.Targets;

public sealed partial class SurrealDbTarget
{
    // The INSERT array form is SurrealDB's bulk import (https://surrealdb.com/docs/learn/querying/concepts-and-guides/bulk-operations-and-data-import); 500 records are about 0.59 MiB, under the 1 MiB SURREAL_HTTP_MAX_SQL_BODY_SIZE default.
    private const int InsertBatchRecords = 500;
    /// <inheritdoc/>
    public int IngestionBatchRecords => InsertBatchRecords;
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        DocumentInitialization.Validate(initialization);
        return InitializeAsync(initialization.Corpus, token);
    }
    /// <inheritdoc/>
    public async Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
    {
        // New clients copy the target's default credential, so each one starts with a token issued now.
        await bearer.RenewAsync(http, Policy, token).ConfigureAwait(false);
        return await DocumentHttpSessionAcquisition.OpenAsync(http, executionOptions, async (owned, cancellation) => { _ = await SurrealDbServer.VerifyAsync(owned, Policy, token: cancellation, timeProvider: timeProvider).ConfigureAwait(false); },
            owned => new SurrealDbSession(owned, table, edge, DocumentMeasurementValues.ActualEnumeration, depth, topK, execution, provider: timeProvider), token).ConfigureAwait(false);
    }
}
