namespace KeyLoad.Comparisons.Targets;

public sealed partial class MongoTarget
{
    /// <inheritdoc/>
    public Task VerifyDocumentCopiesAsync(DocumentComparisonSchedule schedule, CancellationToken token)
        => DocumentMongoClient.VerifyCopiesAsync(this, ownedClients, databaseName, graphDepth, executionOptions, schedule, token);
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        return DocumentNativeInitialization.InitializeAsync(this, initialization, token);
    }
    /// <inheritdoc/>
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
        => DocumentMongoClient.OpenAsync(this, CreateSettings(connectionString, DocumentMeasurementValues.SingleItemCount, executionOptions),
            databaseName, graphDepth, executionOptions, token);
}
