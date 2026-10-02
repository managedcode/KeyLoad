using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Represents one committed mutation retained in the system outbox.</summary>
/// <param name="Sequence">The monotonically increasing outbox sequence.</param>
/// <param name="Ordinal">The mutation's ordinal within its commit.</param>
/// <param name="Commit">The commit token for the mutation.</param>
/// <param name="CommittedAt">The time at which the commit was recorded.</param>
/// <param name="Mutation">The committed mutation.</param>
/// <param name="Receipt">The result of applying the mutation.</param>
/// <param name="Before">The prior document value when the mutation targets a document.</param>
/// <param name="After">The resulting document value when the mutation targets a document.</param>
/// <remarks>The system outbox is separate from business topics, event streams, and the private redo journal.</remarks>
public sealed record OutboxEntry(long Sequence, int Ordinal, CommitToken Commit, DateTimeOffset CommittedAt,
    Mutation Mutation, MutationReceipt Receipt, DocumentRecord? Before = null, DocumentRecord? After = null);

/// <summary>Reports the retained range and storage usage of the system outbox.</summary>
/// <param name="Tail">The latest outbox sequence.</param>
/// <param name="FirstAvailable">The earliest retained sequence.</param>
/// <param name="StoredRecords">The number of retained records.</param>
/// <param name="StoredBytes">The bytes occupied by retained records.</param>
public sealed record OutboxHead(long Tail, long FirstAvailable, long StoredRecords, long StoredBytes);

/// <summary>Identifies a projection consumer within a partition.</summary>
/// <param name="Partition">The partition containing the outbox.</param>
/// <param name="Name">The projection consumer name.</param>
public sealed record ProjectionConsumerRef(PartitionRef Partition, string Name);

/// <summary>Defines the indexes and mutation kinds used by a projection consumer.</summary>
/// <param name="IndexGeneration">The index generation required by the consumer.</param>
/// <param name="Resources">The resources consumed by the projection.</param>
/// <param name="MutationKinds">The mutation kinds consumed by the projection.</param>
public sealed record ProjectionConsumerDefinition(long IndexGeneration, ImmutableArray<string> Resources, ImmutableArray<string> MutationKinds);

/// <summary>Reports a projection consumer's definition and progress.</summary>
/// <param name="Consumer">The consumer identity.</param>
/// <param name="Definition">The active consumer definition.</param>
/// <param name="Checkpoint">The highest outbox sequence acknowledged by the consumer.</param>
/// <param name="Released">Whether the consumer has been released.</param>
public sealed record ProjectionConsumerInfo(ProjectionConsumerRef Consumer, ProjectionConsumerDefinition Definition,
    long Checkpoint, bool Released)
{
    /// <summary>Gets the latest reserve-using commit cut recorded for this consumer.</summary>
    public long LastProgressReservationCut { get; init; } = -1;
}

/// <summary>Requests creation or replacement of a projection consumer.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Consumer">The projection consumer to configure.</param>
/// <param name="Definition">The consumer definition.</param>
/// <param name="StartAfter">The optional outbox sequence after which consumption begins.</param>
public sealed record ConfigureProjectionConsumerRequest(Guid CommandId, ProjectionConsumerRef Consumer,
    ProjectionConsumerDefinition Definition, long? StartAfter = null);

/// <summary>Requests a bounded projection batch.</summary>
/// <param name="Consumer">The projection consumer to read for.</param>
/// <param name="Limit">The maximum number of outbox entries to return.</param>
/// <param name="MaxBytes">The maximum batch size in bytes.</param>
public sealed record ReadProjectionBatchRequest(ProjectionConsumerRef Consumer, int Limit = 100, int MaxBytes = 4_194_304);

/// <summary>Contains outbox entries returned to a projection consumer.</summary>
/// <param name="Consumer">The consumer status for this batch.</param>
/// <param name="Entries">The entries in the batch.</param>
/// <param name="ThroughSequence">The final sequence included in the batch.</param>
/// <param name="Token">The token used to acknowledge the batch.</param>
/// <param name="HasMore">Whether another entry is available.</param>
public sealed record ProjectionBatch(ProjectionConsumerInfo Consumer, ImmutableArray<OutboxEntry> Entries, long ThroughSequence,
    string Token, bool HasMore);

/// <summary>Requests acknowledgement of a projection batch and submission of its effects.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Consumer">The projection consumer acknowledging the batch.</param>
/// <param name="Token">The batch acknowledgement token.</param>
/// <param name="Effects">The mutations produced by projection processing.</param>
public sealed record CommitProjectionBatchRequest(Guid CommandId, ProjectionConsumerRef Consumer, string Token, ImmutableArray<Mutation> Effects);

/// <summary>Reports the commit result and updated projection checkpoint.</summary>
/// <param name="Receipt">The commit receipt for the projection effects.</param>
/// <param name="AlreadyProcessed">Whether the batch had already been committed.</param>
/// <param name="Checkpoint">The updated consumer checkpoint.</param>
public sealed record ProjectionBatchResult(CommitReceipt Receipt, bool AlreadyProcessed, long Checkpoint);

/// <summary>Requests release of a projection consumer.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Consumer">The projection consumer to release.</param>
/// <param name="IndexGeneration">The expected index generation for the release.</param>
public sealed record ReleaseProjectionConsumerRequest(Guid CommandId, ProjectionConsumerRef Consumer, long IndexGeneration);

/// <summary>Requests bounded deletion of outbox entries through a sequence.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Partition">The partition whose outbox is purged.</param>
/// <param name="ThroughSequence">The inclusive upper sequence bound.</param>
/// <param name="Limit">The maximum number of entries to purge.</param>
public sealed record PurgeOutboxRequest(Guid CommandId, PartitionRef Partition, long ThroughSequence, int Limit = 1_000);

/// <summary>Reports the outbox head and configured projection consumers.</summary>
/// <param name="Head">The outbox retention and storage summary.</param>
/// <param name="Consumers">The projection consumers in the partition.</param>
public sealed record OutboxStatus(OutboxHead Head, ImmutableArray<ProjectionConsumerInfo> Consumers);

/// <summary>Identifies the partition whose outbox status is requested.</summary>
/// <param name="Partition">The partition to inspect.</param>
public sealed record GetOutboxStatusRequest(PartitionRef Partition);

/// <summary>Specifies where a change feed begins when no cursor is supplied.</summary>
public enum ChangeFeedStart
{
    /// <summary>Read from the earliest retained change.</summary>
    Beginning,
    /// <summary>Begin at the change feed tail observed when the request is processed.</summary>
    Now
}

/// <summary>Requests a bounded page from a collection change feed.</summary>
/// <param name="Partition">The partition containing the collection.</param>
/// <param name="Collection">The collection to read changes for.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
/// <param name="Start">The starting point when no cursor is supplied.</param>
/// <param name="Limit">The maximum number of changes to return.</param>
/// <param name="MaxBytes">The maximum page size in bytes.</param>
public sealed record ReadChangeFeedRequest(PartitionRef Partition, string Collection, string? Cursor = null,
    ChangeFeedStart Start = ChangeFeedStart.Beginning, int Limit = 100, int MaxBytes = 4_194_304);

/// <summary>Describes a document change recorded in the system outbox.</summary>
/// <param name="Sequence">The outbox sequence of the change.</param>
/// <param name="Commit">The commit token for the change.</param>
/// <param name="CommittedAt">The time at which the change committed.</param>
/// <param name="Reference">The changed document reference.</param>
/// <param name="Revision">The resulting document revision.</param>
/// <param name="Deleted">Whether the document was deleted.</param>
/// <param name="Before">The prior document result, when retained and visible.</param>
/// <param name="After">The resulting document value, when present and visible.</param>
public sealed record DocumentChange(long Sequence, CommitToken Commit, DateTimeOffset CommittedAt,
    EntityRef Reference, long Revision, bool Deleted, DocumentResult? Before, DocumentResult? After);

/// <summary>Contains a page of collection changes and retention metadata.</summary>
/// <param name="Changes">The changes in this page.</param>
/// <param name="Cursor">The continuation cursor.</param>
/// <param name="ThroughSequence">The final sequence included in this page.</param>
/// <param name="Tail">The current outbox tail sequence.</param>
/// <param name="FirstAvailable">The earliest retained sequence.</param>
/// <param name="HasMore">Whether additional changes are available.</param>
/// <param name="CutPosition">The committed position at which the page was read.</param>
public sealed record ChangeFeedPage(ImmutableArray<DocumentChange> Changes, string Cursor, long ThroughSequence, long Tail,
    long FirstAvailable, bool HasMore, long CutPosition);

/// <summary>Requests an initial bounded snapshot for a live query.</summary>
/// <param name="Query">The typed query to observe.</param>
/// <remarks>The live query profile does not use ordering or top-k semantics.</remarks>
public sealed record StartLiveQueryRequest(KeyLoad.Query.AstQueryRequest Query);

/// <summary>Contains the initial live query snapshot and its continuation metadata.</summary>
/// <param name="Rows">The rows in the snapshot.</param>
/// <param name="Cursor">The cursor used to read subsequent changes.</param>
/// <param name="ThroughSequence">The outbox sequence through which the snapshot is consistent.</param>
/// <param name="CutPosition">The committed position at which the snapshot was taken.</param>
/// <remarks>The snapshot is complete within its configured row and byte bounds.</remarks>
public sealed record LiveQuerySnapshot(ImmutableArray<QueryRow> Rows, string Cursor, long ThroughSequence, long CutPosition);

/// <summary>Requests a bounded page of changes for a live query.</summary>
/// <param name="Query">The typed query being observed.</param>
/// <param name="Cursor">The cursor returned by the snapshot or a prior page.</param>
/// <param name="Limit">The maximum number of changes to return.</param>
/// <param name="MaxBytes">The maximum page size in bytes.</param>
/// <remarks>Live query deltas are bounded and unordered; ordering and top-k use separate query profiles.</remarks>
public sealed record ReadLiveQueryRequest(KeyLoad.Query.AstQueryRequest Query, string Cursor, int Limit = 100, int MaxBytes = 4_194_304);

/// <summary>Specifies how a row changed in a live query result.</summary>
public enum LiveQueryChangeKind
{
    /// <summary>The row is present or has changed.</summary>
    Upsert,
    /// <summary>The row is no longer present in the result.</summary>
    Remove
}

/// <summary>Describes one change to a live query result.</summary>
/// <param name="Sequence">The outbox sequence of the change.</param>
/// <param name="Commit">The commit token for the change.</param>
/// <param name="Kind">Whether the result row was added/updated or removed.</param>
/// <param name="Reference">The changed entity reference.</param>
/// <param name="Revision">The changed entity revision.</param>
/// <param name="Row">The resulting query row when the kind is <see cref="LiveQueryChangeKind.Upsert"/>.</param>
public sealed record LiveQueryChange(long Sequence, CommitToken Commit, LiveQueryChangeKind Kind, EntityRef Reference,
    long Revision, QueryRow? Row);

/// <summary>Contains a page of live query changes.</summary>
/// <param name="Changes">The changes in this page.</param>
/// <param name="Cursor">The continuation cursor.</param>
/// <param name="ThroughSequence">The final sequence included in this page.</param>
/// <param name="HasMore">Whether additional changes are available.</param>
/// <param name="CutPosition">The committed position at which the page was read.</param>
public sealed record LiveQueryPage(ImmutableArray<LiveQueryChange> Changes, string Cursor, long ThroughSequence, bool HasMore, long CutPosition);
