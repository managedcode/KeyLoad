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

/// <summary>A real native client's identity and unrestricted ordered actual-state readback.</summary>
public interface IDocumentComparisonSession : IComparisonSession
{
    /// <summary>Identity assigned once to the actual independently owned client, never to borrowed wrappers.</summary>
    Guid ClientIdentity { get; }
    /// <summary>True only when the operation contains no verification read in addition to the stated operation.</summary>
    bool SingleOperationTiming { get; }
    /// <summary>Enumerates actual stored document identities in ordinal order; the runner owns the final-state oracle.</summary>
    IAsyncEnumerable<FoundDocument> ReadDocumentsAsync(CancellationToken cancellationToken);
}

/// <summary>Fresh document-only native setup, with explicit seed versus configure-only intent.</summary>
public sealed record DocumentComparisonInitialization(IComparisonCorpus Corpus, bool Seed, int MaximumIdentityExclusive);
