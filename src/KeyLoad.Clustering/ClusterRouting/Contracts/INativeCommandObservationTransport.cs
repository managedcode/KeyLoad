namespace KeyLoad.Clustering;

/// <summary>Uses only the pre-effect claim's actual original native owner; it never resolves another activation.</summary>
internal interface INativeCommandObservationTransport
{
    Task<GrainOperationReply> ExecuteAsync(string opaqueToken, CancellationToken token);
    Task<GrainOperationReply> ReadAsync(string opaqueToken, CancellationToken token);
    Task AbortAsync(string opaqueToken, CancellationToken token);
}
