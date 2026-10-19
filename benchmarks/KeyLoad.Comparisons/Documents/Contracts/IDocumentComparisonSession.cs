namespace KeyLoad.Comparisons;

/// <summary>A real execution client's identity and unrestricted ordered actual-state readback.</summary>
public interface IDocumentComparisonSession : IComparisonSession
{
    /// <summary>Identity assigned once to the actual independently owned client, never to borrowed wrappers.</summary>
    Guid ClientIdentity
    {
        get;
    }
    /// <summary>True only when the operation contains no verification read in addition to the stated operation.</summary>
    bool SingleOperationTiming
    {
        get;
    }
    /// <summary>Enumerates actual stored document identities in ordinal order; the runner owns the final-state oracle.</summary>
    IAsyncEnumerable<FoundDocument> ReadDocumentsAsync(CancellationToken cancellationToken);
}
