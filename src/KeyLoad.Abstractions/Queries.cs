using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad;

/// <summary>Identifies the document to retrieve.</summary>
/// <param name="Reference">The document's partition and entity identity.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.GetDocumentRequest)]
public sealed record GetDocumentRequest([property: Orleans.Id(0)] EntityRef Reference);

/// <summary>Describes a bounded read from an event stream.</summary>
/// <param name="Stream">The stream to read.</param>
/// <param name="AfterRevision">The exclusive stream revision from which to continue.</param>
/// <param name="Limit">The maximum number of records to return.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadStreamRequest)]
public sealed record ReadStreamRequest([property: Orleans.Id(0)] StreamRef Stream, [property: Orleans.Id(1)] long AfterRevision = 0, [property: Orleans.Id(2)] int Limit = 100);

/// <summary>Identifies a queued message to inspect.</summary>
/// <param name="Lane">The queue lane containing the message.</param>
/// <param name="Id">The message identifier.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.InspectMessageRequest)]
public sealed record InspectMessageRequest([property: Orleans.Id(0)] QueueLaneRef Lane, [property: Orleans.Id(1)] string Id);

/// <summary>Describes a bounded graph traversal from a starting entity.</summary>
/// <param name="Partition">The partition containing the graph.</param>
/// <param name="Graph">The graph name.</param>
/// <param name="Start">The entity where traversal begins.</param>
/// <param name="MaxDepth">The maximum traversal depth.</param>
/// <param name="MaxVertices">The maximum number of vertices to return.</param>
/// <param name="MaxEdges">The maximum number of edges to return.</param>
/// <param name="Labels">Optional edge labels used to constrain traversal.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.TraverseRequest)]
public sealed record TraverseRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Graph, [property: Orleans.Id(2)] EntityRef Start, [property: Orleans.Id(3)] int MaxDepth = 3,
    [property: Orleans.Id(4)] int MaxVertices = 1_000, [property: Orleans.Id(5)] int MaxEdges = 5_000, [property: Orleans.Id(6)] ImmutableArray<string>? Labels = null);

/// <summary>Contains the vertices and edges found by a graph traversal.</summary>
/// <param name="Vertices">The visited vertices.</param>
/// <param name="Edges">The traversed edges.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.GraphTraversal)]
public sealed record GraphTraversal([property: Orleans.Id(0)] ImmutableArray<EntityRef> Vertices, [property: Orleans.Id(1)] ImmutableArray<EdgeRecord> Edges);

/// <summary>Describes a bounded read of samples in a time range.</summary>
/// <param name="Partition">The partition containing the sample set.</param>
/// <param name="Set">The sample set name.</param>
/// <param name="SeriesId">The series identifier.</param>
/// <param name="From">The inclusive beginning of the requested time range.</param>
/// <param name="Until">The inclusive end of the requested UTC time range.</param>
/// <param name="Limit">The maximum number of samples to return.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReadSamplesRequest)]
public sealed record ReadSamplesRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Set, [property: Orleans.Id(2)] string SeriesId, [property: Orleans.Id(3)] DateTimeOffset From, [property: Orleans.Id(4)] DateTimeOffset Until, [property: Orleans.Id(5)] int Limit = 1_000);

/// <summary>Describes a read-only query over one partition.</summary>
/// <param name="Partition">The partition to query.</param>
/// <param name="Sql">The query in the supported SQL dialect.</param>
/// <param name="Parameters">Named values bound by the query.</param>
/// <param name="AllowFullScan">Whether the caller explicitly permits a full scan.</param>
/// <param name="Cursor">The continuation cursor for a prior page, if any.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueryRequest)]
public sealed record QueryRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Sql, [property: Orleans.Id(2)] Dictionary<string, JsonElement>? Parameters = null,
    [property: Orleans.Id(3)] bool AllowFullScan = false, [property: Orleans.Id(4)] string? Cursor = null);

/// <summary>Represents one query result row.</summary>
/// <param name="EntityId">The entity identifier.</param>
/// <param name="Revision">The entity revision represented by this row.</param>
/// <param name="Json">The serialized row value.</param>
/// <param name="Redacted">Whether one or more values were redacted.</param>
/// <param name="RedactedFields">The field paths whose values were redacted.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueryRow)]
public sealed record QueryRow([property: Orleans.Id(0)] string EntityId, [property: Orleans.Id(1)] long Revision, [property: Orleans.Id(2)] string Json, [property: Orleans.Id(3)] bool Redacted = false, [property: Orleans.Id(4)] ImmutableArray<string>? RedactedFields = null);

/// <summary>Contains a page of query rows and its continuation metadata.</summary>
/// <param name="Rows">The rows in this page.</param>
/// <param name="Cursor">The cursor for the next page, if one exists.</param>
/// <param name="CutPosition">The committed position at which the query snapshot was taken.</param>
/// <param name="AccessPath">The access path used to execute the query.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.QueryPage)]
public sealed record QueryPage([property: Orleans.Id(0)] ImmutableArray<QueryRow> Rows, [property: Orleans.Id(1)] string? Cursor, [property: Orleans.Id(2)] long CutPosition, [property: Orleans.Id(3)] string AccessPath);

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.SearchRequest)]
public sealed record SearchRequest([property: Orleans.Id(0)] PartitionRef Partition, [property: Orleans.Id(1)] string Collection, [property: Orleans.Id(2)] string? TextField = null, [property: Orleans.Id(3)] string? Text = null,
    [property: Orleans.Id(4)] string? VectorField = null, [property: Orleans.Id(5)] ImmutableArray<float>? Vector = null, [property: Orleans.Id(6)] VectorSpace? Space = null, [property: Orleans.Id(7)] int Limit = 10,
    [property: Orleans.Id(8)] double TextWeight = 1, [property: Orleans.Id(9)] double VectorWeight = 1, [property: Orleans.Id(10)] int FusionConstant = 60);

/// <summary>Confirms the creation of a backup and the committed position it contains.</summary>
/// <param name="Id">The backup identifier.</param>
/// <param name="Position">The committed position captured by the backup.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.BackupReceipt)]
public sealed record BackupReceipt([property: Orleans.Id(0)] string Id, [property: Orleans.Id(1)] long Position);

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
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.NodeStatus)]
public sealed record NodeStatus([property: Orleans.Id(0)] string NodeId, [property: Orleans.Id(1)] Guid Incarnation, [property: Orleans.Id(2)] long Applied, [property: Orleans.Id(3)] string? Leader, [property: Orleans.Id(4)] int Voters, [property: Orleans.Id(5)] DurabilityProfile Durability, [property: Orleans.Id(6)] bool RoutingReady, [property: Orleans.Id(7)] int ProcessId,
    [property: Orleans.Id(8)] long ReadGeneration = 0);
