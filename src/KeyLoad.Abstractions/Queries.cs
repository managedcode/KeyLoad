using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad;

/// <summary>Identifies the document to retrieve.</summary>
/// <param name="Reference">The document's partition and entity identity.</param>
public sealed record GetDocumentRequest(EntityRef Reference);

/// <summary>Describes a bounded read from an event stream.</summary>
/// <param name="Stream">The stream to read.</param>
/// <param name="AfterRevision">The exclusive stream revision from which to continue.</param>
/// <param name="Limit">The maximum number of records to return.</param>
public sealed record ReadStreamRequest(StreamRef Stream, long AfterRevision = 0, int Limit = 100);

/// <summary>Identifies a queued message to inspect.</summary>
/// <param name="Lane">The queue lane containing the message.</param>
/// <param name="Id">The message identifier.</param>
public sealed record InspectMessageRequest(QueueLaneRef Lane, string Id);

/// <summary>Describes a bounded graph traversal from a starting entity.</summary>
/// <param name="Partition">The partition containing the graph.</param>
/// <param name="Graph">The graph name.</param>
/// <param name="Start">The entity where traversal begins.</param>
/// <param name="MaxDepth">The maximum traversal depth.</param>
/// <param name="MaxVertices">The maximum number of vertices to return.</param>
/// <param name="MaxEdges">The maximum number of edges to return.</param>
/// <param name="Labels">Optional edge labels used to constrain traversal.</param>
public sealed record TraverseRequest(PartitionRef Partition, string Graph, EntityRef Start, int MaxDepth = 3,
    int MaxVertices = 1_000, int MaxEdges = 5_000, ImmutableArray<string>? Labels = null);

/// <summary>Contains the vertices and edges found by a graph traversal.</summary>
/// <param name="Vertices">The visited vertices.</param>
/// <param name="Edges">The traversed edges.</param>
public sealed record GraphTraversal(ImmutableArray<EntityRef> Vertices, ImmutableArray<EdgeRecord> Edges);

/// <summary>Describes a bounded read of samples in a time range.</summary>
/// <param name="Partition">The partition containing the sample set.</param>
/// <param name="Set">The sample set name.</param>
/// <param name="SeriesId">The series identifier.</param>
/// <param name="From">The inclusive beginning of the requested time range.</param>
/// <param name="Until">The inclusive end of the requested UTC time range.</param>
/// <param name="Limit">The maximum number of samples to return.</param>
public sealed record ReadSamplesRequest(PartitionRef Partition, string Set, string SeriesId, DateTimeOffset From, DateTimeOffset Until, int Limit = 1_000);

/// <summary>Describes a read-only query over one partition.</summary>
/// <param name="Partition">The partition to query.</param>
/// <param name="Sql">The query in the supported SQL dialect.</param>
/// <param name="Parameters">Named values bound by the query.</param>
/// <param name="AllowFullScan">Whether the caller explicitly permits a full scan.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
public sealed record QueryRequest(PartitionRef Partition, string Sql, Dictionary<string, JsonElement>? Parameters = null,
    bool AllowFullScan = false, string? Cursor = null);

/// <summary>Represents one query result row.</summary>
/// <param name="EntityId">The entity identifier.</param>
/// <param name="Revision">The entity revision represented by this row.</param>
/// <param name="Json">The serialized row value.</param>
/// <param name="Redacted">Whether one or more values were redacted.</param>
/// <param name="RedactedFields">The field paths whose values were redacted.</param>
public sealed record QueryRow(string EntityId, long Revision, string Json, bool Redacted = false, ImmutableArray<string>? RedactedFields = null);

/// <summary>Contains a page of query rows and its continuation metadata.</summary>
/// <param name="Rows">The rows in this page.</param>
/// <param name="Cursor">The cursor for the next page, if one exists.</param>
/// <param name="CutPosition">The committed position at which the query snapshot was taken.</param>
/// <param name="AccessPath">The access path used to execute the query.</param>
public sealed record QueryPage(ImmutableArray<QueryRow> Rows, string? Cursor, long CutPosition, string AccessPath);

/// <summary>Describes a combined text and vector search.</summary>
/// <param name="Partition">The partition to search.</param>
/// <param name="Collection">The document collection to search.</param>
/// <param name="TextField">The optional text field to search.</param>
/// <param name="Text">The optional text query.</param>
/// <param name="VectorField">The optional vector field to search.</param>
/// <param name="Vector">The optional query vector.</param>
/// <param name="Space">The vector space used for vector similarity.</param>
/// <param name="Limit">The maximum number of results to return.</param>
/// <param name="TextWeight">The ranking weight assigned to text results.</param>
/// <param name="VectorWeight">The ranking weight assigned to vector results.</param>
/// <param name="FusionConstant">The rank-fusion constant used when combining result lists.</param>
public sealed record SearchRequest(PartitionRef Partition, string Collection, string? TextField = null, string? Text = null,
    string? VectorField = null, ImmutableArray<float>? Vector = null, VectorSpace? Space = null, int Limit = 10,
    double TextWeight = 1, double VectorWeight = 1, int FusionConstant = 60);

/// <summary>Confirms the creation of a backup and the committed position it contains.</summary>
/// <param name="Id">The backup identifier.</param>
/// <param name="Position">The committed position captured by the backup.</param>
public sealed record BackupReceipt(string Id, long Position);

/// <summary>Reports node identity, replication progress, and readiness.</summary>
/// <param name="NodeId">The configured node identifier.</param>
/// <param name="Incarnation">The unique identifier for this process incarnation.</param>
/// <param name="Applied">The highest committed position applied by this node.</param>
/// <param name="Leader">The current leader identifier, if known.</param>
/// <param name="Voters">The number of voting nodes in the cluster.</param>
/// <param name="Durability">The configured durability profile.</param>
/// <param name="RoutingReady">Whether the node is ready to receive routed requests.</param>
/// <param name="ProcessId">The operating system process identifier.</param>
/// <param name="ReadGeneration">The generation of the node's read view.</param>
public sealed record NodeStatus(string NodeId, Guid Incarnation, long Applied, string? Leader, int Voters, DurabilityProfile Durability, bool RoutingReady, int ProcessId,
    long ReadGeneration = 0);
