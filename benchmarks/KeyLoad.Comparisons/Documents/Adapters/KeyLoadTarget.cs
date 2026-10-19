using KeyLoad.Client;

namespace KeyLoad.Comparisons.Targets;

public sealed partial class KeyLoadTarget
{
    private bool ownsDocumentNamespace;
    /// <inheritdoc/>
    public Task VerifyDocumentCopiesAsync(DocumentComparisonSchedule schedule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        expectedCorpusCount = schedule.Final;
        return ObserveCopiesAsync(cancellationToken);
    }
    /// <inheritdoc/>
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken token)
    {
        DocumentInitialization.Validate(initialization);
        ownsDocumentNamespace = true;
        return InitializeAsync(initialization.Corpus, token);
    }
    /// <inheritdoc/>
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken token)
        => DocumentHttpSessionAcquisition.OpenAsync(http, runtimeExecutionOptions,
            async (owned, cancellation) => { _ = KeyLoadClientResults.Success(await new KeyLoadClient(owned, credential, clientOptions).StatusAsync(cancellation).ConfigureAwait(false)); },
            owned => new KeyLoadComparisonSession(new KeyLoadClient(owned, credential, clientOptions), partition, space, topK, graphDepth, graphVertices,
                graphEdges, DocumentMeasurementValues.ActualEnumeration, expectedNodeCount, lifecycleOptions, runtimeExecutionOptions, translationOptions, provider: timeProvider), token,
            (owned, session, operation, document, cancellation) => ((KeyLoadComparisonSession)session).ExecuteDocumentOperationAsync(operation, document, cancellation));
    private Task CleanupDocumentNamespaceAsync() => ownsDocumentNamespace
        ? DocumentKeyLoadCleanup.RunAsync(client, partition, runtimeExecutionOptions, translationOptions, timeProvider)
        : Task.CompletedTask;
}
