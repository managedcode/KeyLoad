namespace KeyLoad.Comparisons;

/// <summary>Explicit native document lifecycle; unsupported adapters cannot impersonate configure-only ingestion.</summary>
public interface IDocumentComparisonTarget : IComparisonTarget
{
    /// <summary>Configures a fresh namespace and optionally seeds only the declared initial records.</summary>
    Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken cancellationToken);
    /// <summary>Re-observes native copies after the untimed complete final-state oracle; unsupported replicated proof rejects qualification.</summary>
    Task VerifyDocumentCopiesAsync(DocumentComparisonSchedule schedule, CancellationToken cancellationToken)
        => Profile.Cluster?.Nodes == DocumentMeasurementValues.SingleItemCount
            && Profile.Cluster.DataCopies == DocumentMeasurementValues.SingleItemCount
            ? Task.CompletedTask
            : Task.FromException(new ComparisonFailureException(DocumentProtocolText.DocumentFinalNativeCopiesUnavailable));
    /// <summary>Opens an independently owned actual native client and admits it before the start barrier.</summary>
    Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken cancellationToken);
}
