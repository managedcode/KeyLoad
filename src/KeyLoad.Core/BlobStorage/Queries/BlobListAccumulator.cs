using KeyLoad.Storage;

namespace KeyLoad.Core.Features.BlobStorage;

internal sealed class BlobListAccumulator(DatabaseEngine database, PrincipalRecord principal, int limit)
{
    internal const string ScopeId = "blob-list-scope";
    internal const int CandidateMultiplier = 2;
    private int visited;
    internal List<BlobMetadata> Items { get; } = [];
    internal string? LastId
    {
        get; private set;
    }

    internal bool Visit(IKeyValueView view, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        const int RevisionValidationBoundary = 0;

        var blob = BlobKeys.DecodeScope(key, BlobKeys.HeadSpace, BlobKeys.ScopeComponents);
        var head = BlobRecordReader.Decode<BlobHead>(value);
        BlobRecordReader.ValidateHead(head, blob, database.Store.Identity.Incarnation);
        visited++;
        LastId = blob.Id;
        if (head.Metadata.Revision > RevisionValidationBoundary && !head.Metadata.Deleted
                                                                && database.Authorization.CanReadRow(principal, head.Metadata.Access))
        {
            _ = new BlobRecordReader(database.Store.Identity.Incarnation).Current(view, head);
            Items.Add(head.Metadata);
        }
        return visited < limit * CandidateMultiplier && Items.Count < limit;
    }
}
