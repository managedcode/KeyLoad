namespace KeyLoad;

// The system outbox is separate from business topics, event streams and the private redo journal.
public sealed record OutboxEntry(long Sequence, int Ordinal, CommitToken Commit, DateTimeOffset CommittedAt,
    Mutation Mutation, MutationReceipt Receipt, DocumentRecord? Before = null, DocumentRecord? After = null);
public sealed record OutboxHead(long Tail, long FirstAvailable, long StoredRecords, long StoredBytes);
public sealed record ProjectionConsumerRef(PartitionRef Partition, string Name);
public sealed record ProjectionConsumerDefinition(long IndexGeneration, string[] Resources, string[] MutationKinds);
public sealed record ProjectionConsumerInfo(ProjectionConsumerRef Consumer, ProjectionConsumerDefinition Definition,
    long Checkpoint, bool Released)
{
    // One reserve-using commit per consumer until the retained prefix advances; this fence is canonical state.
    public long LastProgressReservationCut { get; init; } = -1;
}
public sealed record ConfigureProjectionConsumerRequest(Guid CommandId, ProjectionConsumerRef Consumer,
    ProjectionConsumerDefinition Definition, long? StartAfter = null);
public sealed record ReadProjectionBatchRequest(ProjectionConsumerRef Consumer, int Limit = 100, int MaxBytes = 4_194_304);
public sealed record ProjectionBatch(ProjectionConsumerInfo Consumer, OutboxEntry[] Entries, long ThroughSequence,
    string Token, bool HasMore);
public sealed record CommitProjectionBatchRequest(Guid CommandId, ProjectionConsumerRef Consumer, string Token, Mutation[] Effects);
public sealed record ProjectionBatchResult(CommitReceipt Receipt, bool AlreadyProcessed, long Checkpoint);
public sealed record ReleaseProjectionConsumerRequest(Guid CommandId, ProjectionConsumerRef Consumer, long IndexGeneration);
public sealed record PurgeOutboxRequest(Guid CommandId, PartitionRef Partition, long ThroughSequence, int Limit = 1_000);
public sealed record OutboxStatus(OutboxHead Head, ProjectionConsumerInfo[] Consumers);
public sealed record GetOutboxStatusRequest(PartitionRef Partition);

public enum ChangeFeedStart { Beginning, Now }
public sealed record ReadChangeFeedRequest(PartitionRef Partition, string Collection, string? Cursor = null,
    ChangeFeedStart Start = ChangeFeedStart.Beginning, int Limit = 100, int MaxBytes = 4_194_304);
public sealed record DocumentChange(long Sequence, CommitToken Commit, DateTimeOffset CommittedAt,
    EntityRef Reference, long Revision, bool Deleted, DocumentResult? Before, DocumentResult? After);
public sealed record ChangeFeedPage(DocumentChange[] Changes, string Cursor, long ThroughSequence, long Tail,
    long FirstAvailable, bool HasMore, long CutPosition);

// Live queries expose a complete bounded initial snapshot and bounded unordered deltas; ORDER BY/top-k are separate profiles.
public sealed record StartLiveQueryRequest(KeyLoad.Query.AstQueryRequest Query);
public sealed record LiveQuerySnapshot(QueryRow[] Rows, string Cursor, long ThroughSequence, long CutPosition);
public sealed record ReadLiveQueryRequest(KeyLoad.Query.AstQueryRequest Query, string Cursor, int Limit = 100, int MaxBytes = 4_194_304);
public enum LiveQueryChangeKind { Upsert, Remove }
public sealed record LiveQueryChange(long Sequence, CommitToken Commit, LiveQueryChangeKind Kind, EntityRef Reference,
    long Revision, QueryRow? Row);
public sealed record LiveQueryPage(LiveQueryChange[] Changes, string Cursor, long ThroughSequence, bool HasMore, long CutPosition);
