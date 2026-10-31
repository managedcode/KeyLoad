namespace KeyLoad.Query.QueryExecution;

/// <summary>A candidate retained only while it belongs to the requested top prefix.</summary>
internal sealed record PreparedRow(DocumentRecord? SourceDocument, byte[][]? PendingOrderKeys, byte[]? IdKey,
    PartitionQueryCandidate? Candidate)
{
    internal DocumentRecord Document => SourceDocument ?? throw new InvalidOperationException();
    internal EntityRef Reference => SourceDocument?.Reference ?? Candidate!.Reference;
}
