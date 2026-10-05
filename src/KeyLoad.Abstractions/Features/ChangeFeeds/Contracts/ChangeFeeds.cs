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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.OutboxEntry)]
public sealed record OutboxEntry([property: Orleans.Id(0)] long Sequence, [property: Orleans.Id(1)] int Ordinal, [property: Orleans.Id(2)] CommitToken Commit, [property: Orleans.Id(3)] DateTimeOffset CommittedAt,
    [property: Orleans.Id(4)] Mutation Mutation, [property: Orleans.Id(5)] MutationReceipt Receipt, [property: Orleans.Id(6)] DocumentRecord? Before = null, [property: Orleans.Id(7)] DocumentRecord? After = null);

/// <summary>Reports the retained range and storage usage of the system outbox.</summary>
/// <param name="Tail">The latest outbox sequence.</param>
/// <param name="FirstAvailable">The earliest retained sequence.</param>
/// <param name="StoredRecords">The number of retained records.</param>
/// <param name="StoredBytes">The bytes occupied by retained records.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.OutboxHead)]
public sealed record OutboxHead([property: Orleans.Id(0)] long Tail, [property: Orleans.Id(1)] long FirstAvailable, [property: Orleans.Id(2)] long StoredRecords, [property: Orleans.Id(3)] long StoredBytes);

/// <summary>Identifies a projection consumer within a partition.</summary>
/// <param name="Partition">The partition containing the outbox.</param>
/// <param name="Name">The projection consumer name.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProjectionConsumerRef)]
public sealed record ProjectionConsumerRef([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Name);

/// <summary>Defines the indexes and mutation kinds used by a projection consumer.</summary>
/// <param name="IndexGeneration">The index generation required by the consumer.</param>
/// <param name="Resources">The resources consumed by the projection.</param>
/// <param name="MutationKinds">The mutation kinds consumed by the projection.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProjectionConsumerDefinition)]
public sealed record ProjectionConsumerDefinition([property: Orleans.Id(0)] long IndexGeneration, [property: Orleans.Id(1)] ImmutableArray<string> Resources, [property: Orleans.Id(2)] ImmutableArray<string> MutationKinds);

/// <summary>Reports a projection consumer's definition and progress.</summary>
/// <param name="Consumer">The consumer identity.</param>
/// <param name="Definition">The active consumer definition.</param>
/// <param name="Checkpoint">The highest outbox sequence acknowledged by the consumer.</param>
/// <param name="Released">Whether the consumer has been released.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProjectionConsumerInfo)]
public sealed record ProjectionConsumerInfo([property: Orleans.Id(0)] ProjectionConsumerRef Consumer, [property: Orleans.Id(1)] ProjectionConsumerDefinition Definition,
    [property: Orleans.Id(2)] long Checkpoint, [property: Orleans.Id(3)] bool Released)
{
    private const int NoProgressReservationCut = -1;

    /// <summary>Gets the latest reserve-using commit cut recorded for this consumer.</summary>
    [Orleans.Id(4)]
    public long LastProgressReservationCut { get; init; } = NoProgressReservationCut;
}

/// <summary>Requests creation or replacement of a projection consumer.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Consumer">The projection consumer to configure.</param>
/// <param name="Definition">The consumer definition.</param>
/// <param name="StartAfter">The optional outbox sequence after which consumption begins.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ConfigureProjectionConsumerRequest)]
public sealed record ConfigureProjectionConsumerRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] ProjectionConsumerRef Consumer,
    [property: Orleans.Id(2)] ProjectionConsumerDefinition Definition, [property: Orleans.Id(3)] long? StartAfter = null);

/// <summary>Requests a bounded projection batch.</summary>
/// <param name="Consumer">The projection consumer to read for.</param>
/// <param name="Limit">The maximum number of outbox entries to return.</param>
/// <param name="MaxBytes">The maximum batch size in bytes.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadProjectionBatchRequest)]
public sealed record ReadProjectionBatchRequest([property: Orleans.Id(0)] ProjectionConsumerRef Consumer, [property: Orleans.Id(1)] int Limit = ReadProjectionBatchRequest.DefaultLimit, [property: Orleans.Id(2)] int MaxBytes = ReadProjectionBatchRequest.DefaultMaxBytes)
{
    private const int DefaultLimit = 100;
    private const int DefaultMaxBytes = 4_194_304;
}

/// <summary>Contains outbox entries returned to a projection consumer.</summary>
/// <param name="Consumer">The consumer status for this batch.</param>
/// <param name="Entries">The entries in the batch.</param>
/// <param name="ThroughSequence">The final sequence included in the batch.</param>
/// <param name="Token">The token used to acknowledge the batch.</param>
/// <param name="HasMore">Whether another entry is available.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProjectionBatch)]
public sealed record ProjectionBatch([property: Orleans.Id(0)] ProjectionConsumerInfo Consumer, [property: Orleans.Id(1)] ImmutableArray<OutboxEntry> Entries, [property: Orleans.Id(2)] long ThroughSequence,
    [property: Orleans.Id(3)] string Token, [property: Orleans.Id(4)] bool HasMore);

/// <summary>Requests acknowledgement of a projection batch and submission of its effects.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Consumer">The projection consumer acknowledging the batch.</param>
/// <param name="Token">The batch acknowledgement token.</param>
/// <param name="Effects">The mutations produced by projection processing.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommitProjectionBatchRequest)]
public sealed record CommitProjectionBatchRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] ProjectionConsumerRef Consumer, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] ImmutableArray<Mutation> Effects);

/// <summary>Reports the commit result and updated projection checkpoint.</summary>
/// <param name="Receipt">The commit receipt for the projection effects.</param>
/// <param name="AlreadyProcessed">Whether the batch had already been committed.</param>
/// <param name="Checkpoint">The updated consumer checkpoint.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ProjectionBatchResult)]
public sealed record ProjectionBatchResult([property: Orleans.Id(0)] CommitReceipt Receipt, [property: Orleans.Id(1)] bool AlreadyProcessed, [property: Orleans.Id(2)] long Checkpoint);

/// <summary>Requests release of a projection consumer.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Consumer">The projection consumer to release.</param>
/// <param name="IndexGeneration">The expected index generation for the release.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReleaseProjectionConsumerRequest)]
public sealed record ReleaseProjectionConsumerRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] ProjectionConsumerRef Consumer, [property: Orleans.Id(2)] long IndexGeneration);

/// <summary>Requests bounded deletion of outbox entries through a sequence.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Partition">The partition whose outbox is purged.</param>
/// <param name="ThroughSequence">The inclusive upper sequence bound.</param>
/// <param name="Limit">The maximum number of entries to purge.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.PurgeOutboxRequest)]
public sealed record PurgeOutboxRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] PartitionRef Partition, [property: Orleans.Id(2)] long ThroughSequence, [property: Orleans.Id(3)] int Limit = PurgeOutboxRequest.DefaultLimit)
{
    private const int DefaultLimit = 1_000;
}

/// <summary>Reports the outbox head and configured projection consumers.</summary>
/// <param name="Head">The outbox retention and storage summary.</param>
/// <param name="Consumers">The projection consumers in the partition.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.OutboxStatus)]
public sealed record OutboxStatus([property: Orleans.Id(0)] OutboxHead Head, [property: Orleans.Id(1)] ImmutableArray<ProjectionConsumerInfo> Consumers);

/// <summary>Identifies the partition whose outbox status is requested.</summary>
/// <param name="Partition">The partition to inspect.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.GetOutboxStatusRequest)]
public sealed record GetOutboxStatusRequest([property: Orleans.Id(0)] PartitionRef Partition);

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadChangeFeedRequest)]
public sealed record ReadChangeFeedRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Collection, [property: Orleans.Id(2)] string? Cursor = null,
    [property: Orleans.Id(3)] ChangeFeedStart Start = ChangeFeedStart.Beginning, [property: Orleans.Id(4)] int Limit = ReadChangeFeedRequest.DefaultLimit, [property: Orleans.Id(5)] int MaxBytes = ReadChangeFeedRequest.DefaultMaxBytes)
{
    private const int DefaultLimit = 100;
    private const int DefaultMaxBytes = 4_194_304;
}

/// <summary>Describes a document change recorded in the system outbox.</summary>
/// <param name="Sequence">The outbox sequence of the change.</param>
/// <param name="Commit">The commit token for the change.</param>
/// <param name="CommittedAt">The time at which the change committed.</param>
/// <param name="Reference">The changed document reference.</param>
/// <param name="Revision">The resulting document revision.</param>
/// <param name="Deleted">Whether the document was deleted.</param>
/// <param name="Before">The prior document result, when retained and visible.</param>
/// <param name="After">The resulting document value, when present and visible.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.DocumentChange)]
public sealed record DocumentChange([property: Orleans.Id(0)] long Sequence, [property: Orleans.Id(1)] CommitToken Commit, [property: Orleans.Id(2)] DateTimeOffset CommittedAt,
    [property: Orleans.Id(3)] EntityRef Reference, [property: Orleans.Id(4)] long Revision, [property: Orleans.Id(5)] bool Deleted, [property: Orleans.Id(6)] DocumentResult? Before, [property: Orleans.Id(7)] DocumentResult? After);

/// <summary>Contains a page of collection changes and retention metadata.</summary>
/// <param name="Changes">The changes in this page.</param>
/// <param name="Cursor">The continuation cursor.</param>
/// <param name="ThroughSequence">The final sequence included in this page.</param>
/// <param name="Tail">The current outbox tail sequence.</param>
/// <param name="FirstAvailable">The earliest retained sequence.</param>
/// <param name="HasMore">Whether additional changes are available.</param>
/// <param name="CutPosition">The committed position at which the page was read.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ChangeFeedPage)]
public sealed record ChangeFeedPage([property: Orleans.Id(0)] ImmutableArray<DocumentChange> Changes, [property: Orleans.Id(1)] string Cursor, [property: Orleans.Id(2)] long ThroughSequence, [property: Orleans.Id(3)] long Tail,
    [property: Orleans.Id(4)] long FirstAvailable, [property: Orleans.Id(5)] bool HasMore, [property: Orleans.Id(6)] long CutPosition);

/// <summary>Requests an initial bounded snapshot for a live query.</summary>
/// <param name="Query">The typed query to observe.</param>
/// <remarks>The live query profile does not use ordering or top-k semantics.</remarks>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.StartLiveQueryRequest)]
public sealed record StartLiveQueryRequest([property: Orleans.Id(0)] KeyLoad.Query.AstQueryRequest Query);

/// <summary>Contains the initial live query snapshot and its continuation metadata.</summary>
/// <param name="Rows">The rows in the snapshot.</param>
/// <param name="Cursor">The cursor used to read subsequent changes.</param>
/// <param name="ThroughSequence">The outbox sequence through which the snapshot is consistent.</param>
/// <param name="CutPosition">The committed position at which the snapshot was taken.</param>
/// <remarks>The snapshot is complete within its configured row and byte bounds.</remarks>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.LiveQuerySnapshot)]
public sealed record LiveQuerySnapshot([property: Orleans.Id(0)] ImmutableArray<QueryRow> Rows, [property: Orleans.Id(1)] string Cursor, [property: Orleans.Id(2)] long ThroughSequence, [property: Orleans.Id(3)] long CutPosition);

/// <summary>Requests a bounded page of changes for a live query.</summary>
/// <param name="Query">The typed query being observed.</param>
/// <param name="Cursor">The cursor returned by the snapshot or a prior page.</param>
/// <param name="Limit">The maximum number of changes to return.</param>
/// <param name="MaxBytes">The maximum page size in bytes.</param>
/// <remarks>Live query deltas are bounded and unordered; ordering and top-k use separate query profiles.</remarks>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadLiveQueryRequest)]
public sealed record ReadLiveQueryRequest([property: Orleans.Id(0)] KeyLoad.Query.AstQueryRequest Query, [property: Orleans.Id(1)] string Cursor, [property: Orleans.Id(2)] int Limit = ReadLiveQueryRequest.DefaultLimit, [property: Orleans.Id(3)] int MaxBytes = ReadLiveQueryRequest.DefaultMaxBytes)
{
    private const int DefaultLimit = 100;
    private const int DefaultMaxBytes = 4_194_304;
}

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.LiveQueryChange)]
public sealed record LiveQueryChange([property: Orleans.Id(0)] long Sequence, [property: Orleans.Id(1)] CommitToken Commit, [property: Orleans.Id(2)] LiveQueryChangeKind Kind, [property: Orleans.Id(3)] EntityRef Reference,
    [property: Orleans.Id(4)] long Revision, [property: Orleans.Id(5)] QueryRow? Row);

/// <summary>Contains a page of live query changes.</summary>
/// <param name="Changes">The changes in this page.</param>
/// <param name="Cursor">The continuation cursor.</param>
/// <param name="ThroughSequence">The final sequence included in this page.</param>
/// <param name="HasMore">Whether additional changes are available.</param>
/// <param name="CutPosition">The committed position at which the page was read.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.LiveQueryPage)]
public sealed record LiveQueryPage([property: Orleans.Id(0)] ImmutableArray<LiveQueryChange> Changes, [property: Orleans.Id(1)] string Cursor, [property: Orleans.Id(2)] long ThroughSequence, [property: Orleans.Id(3)] bool HasMore, [property: Orleans.Id(4)] long CutPosition);
