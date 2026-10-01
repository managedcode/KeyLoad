using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private sealed record ChangeFeedClaims(string Purpose, Guid Incarnation, PartitionRef Partition, string Collection,
        string PrincipalId, long PolicyEpoch, long SchemaVersion, long VisibilityEpoch, long After, DateTimeOffset ExpiresAt);
    private long VisibilityEpoch(IKeyValueView view, PartitionRef partition, string collection)
        => view.Get(KeySpace.Partition("visibility-epoch", partition, collection)) is { } bytes ? JsonDefaults.Deserialize<long>(bytes) : 0;
    private void AdvanceVisibilityEpoch(IAtomicTransaction tx, PartitionRef partition, string collection)
        => tx.PutRecord(KeySpace.Partition("visibility-epoch", partition, collection), checked(VisibilityEpoch(tx, partition, collection) + 1));
    private string ChangeCursor(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource, PartitionRef partition, long after)
        => Sign(new ChangeFeedClaims("document-change-feed", Store.Identity.Incarnation, partition, resource.Name, principal.Id,
            principal.PolicyEpoch, resource.SchemaVersion, VisibilityEpoch(view, partition, resource.Name), after, DateTimeOffset.UtcNow.AddHours(24)));
    public (string Cursor, long Tail) CaptureChangeFeedCursor(IKeyValueView view, PrincipalRecord principal, PartitionRef partition, string collection)
    {
        Authorization.Require(principal, partition, collection, Capability.ChangesRead | Capability.DocumentsRead);
        var resource = Resource(view, partition, collection, ResourceKind.Collection); var tail = ReadOutboxHead(view, partition).Tail;
        return (ChangeCursor(view, principal, resource, partition, tail), tail);
    }
    public ChangeFeedPage ReadChangeFeed(string principalId, ReadChangeFeedRequest request) => Store.Read<ChangeFeedPage>(view =>
    {
        var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
        var page = ReadChangeFeedView(view, principal, request, (resource, change) => new DocumentChange(change.Sequence, change.Commit,
            change.CommittedAt, change.After.Reference, change.After.Revision, change.After.Deleted,
            change.Before is { } before ? Project(principal, resource, before) : null,
            change.After.Deleted ? null : Project(principal, resource, change.After)));
        return new(page.Changes, page.Cursor, page.ThroughSequence, page.Tail, page.FirstAvailable, page.HasMore, page.CutPosition);
    });
    public AuthorizedDocumentChangePage<T> ReadChangeFeedView<T>(IKeyValueView view, PrincipalRecord principal,
        ReadChangeFeedRequest request, Func<ResourceDefinition, AuthorizedDocumentChange, T?> project) where T : class
    {
        ValidatePartition(request.Partition); JsonData.Identifier(request.Collection);
        var now = DateTimeOffset.UtcNow;
        Authorization.Require(principal, request.Partition, request.Collection, Capability.ChangesRead | Capability.DocumentsRead);
        var resource = Resource(view, request.Partition, request.Collection, ResourceKind.Collection);
        if (request.Limit < 1 || request.Limit > Limits.MaxResults || request.MaxBytes < 1 || request.MaxBytes > Limits.MaxBatchBytes
            || request.Start is not (ChangeFeedStart.Beginning or ChangeFeedStart.Now))
            throw Errors.Fail(ErrorCode.BudgetExceeded, "The change-feed request exceeds its supported bounds.");
        var head = ReadOutboxHead(view, request.Partition);
        var after = request.Start == ChangeFeedStart.Now ? head.Tail : head.FirstAvailable - 1;
        if (request.Cursor is { } cursor)
        {
            var claims = Verify<ChangeFeedClaims>(cursor);
            if (claims.Purpose != "document-change-feed" || claims.Incarnation != Store.Identity.Incarnation || claims.Partition != request.Partition
                || claims.Collection != request.Collection || claims.PrincipalId != principal.Id || claims.PolicyEpoch != principal.PolicyEpoch
                || claims.SchemaVersion != resource.SchemaVersion || claims.VisibilityEpoch != VisibilityEpoch(view, request.Partition, request.Collection)
                || claims.ExpiresAt <= now)
                throw Errors.Fail(ErrorCode.TokenInvalidated, "The change-feed cursor requires a fresh authorized snapshot.");
            after = claims.After;
        }
        CheckOutboxPosition(head, after);
        var changes = new List<T>(); var through = after; var bytes = 0;
        foreach (var entry in ReadOutboxRange(view, request.Partition, after, request.Limit))
        {
            var visible = VisibleChange(view, principal, resource, request.Partition, entry);
            var change = visible is null ? null : project(resource, visible);
            if (change is not null)
            {
                var size = JsonDefaults.Serialize(change).Length;
                if (size > request.MaxBytes - bytes)
                {
                    if (through == after) throw Errors.Fail(ErrorCode.BudgetExceeded, "The first change exceeds the page byte budget.");
                    break;
                }
                changes.Add(change); bytes += size;
            }
            through = entry.Sequence;
        }
        return new(changes.ToArray(), ChangeCursor(view, principal, resource, request.Partition, through), through, head.Tail,
            head.FirstAvailable, through < head.Tail, Store.Position);
    }
    private AuthorizedDocumentChange? VisibleChange(IKeyValueView view, PrincipalRecord principal, ResourceDefinition resource,
        PartitionRef partition, OutboxEntry entry)
    {
        if (entry.Receipt.Resource != resource.Name || entry.After is not { } after) return null;
        var current = view.GetRecord<DocumentRecord>(DocumentKey(partition, resource.Name, after.Reference.Id));
        // Historical grants and current row visibility both constrain a historical projection.
        if (current is null || !Authorization.CanReadRow(principal, current.Access) || !Authorization.CanReadRow(principal, after.Access)) return null;
        var before = entry.Before is { Deleted: false } old && Authorization.CanReadRow(principal, old.Access) ? old : null;
        return new(entry.Sequence, entry.Commit, entry.CommittedAt, before, after);
    }
}
public sealed record AuthorizedDocumentChange(long Sequence, CommitToken Commit, DateTimeOffset CommittedAt,
    DocumentRecord? Before, DocumentRecord After);
public sealed record AuthorizedDocumentChangePage<T>(T[] Changes, string Cursor, long ThroughSequence, long Tail,
    long FirstAvailable, bool HasMore, long CutPosition);
