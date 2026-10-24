namespace KeyLoad.Comparisons.Targets;

public sealed partial class MongoTarget
{
    // One InsertMany wire message stays below maxMessageSizeBytes 48,000,000 and maxWriteBatchSize 100,000 (https://www.mongodb.com/docs/manual/reference/limits/).
    private const int InsertManyBatchDocuments = 10_000;
    /// <inheritdoc/>
    public int IngestionBatchRecords => InsertManyBatchDocuments;
    /// <inheritdoc/>
    public Task VerifyDocumentCopiesAsync(DocumentComparisonSchedule schedule, CancellationToken token)
        => DocumentMongoClient.VerifyCopiesAsync(this, ownedClients, databaseName, graphDepth, executionOptions, schedule, token);
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        return DocumentInitialization.InitializeAsync(this, initialization, token);
    }
    /// <inheritdoc/>
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
        => DocumentMongoClient.OpenAsync(this, CreateSettings(DocumentMeasurementValues.SingleItemCount),
            databaseName, graphDepth, executionOptions, token);
}
