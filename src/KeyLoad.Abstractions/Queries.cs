using System.Text.Json;

namespace KeyLoad;

public sealed record GetDocumentRequest(EntityRef Reference);
public sealed record ReadStreamRequest(StreamRef Stream, long AfterRevision = 0, int Limit = 100);
public sealed record InspectMessageRequest(QueueLaneRef Lane, string Id);
public sealed record TraverseRequest(PartitionRef Partition, string Graph, EntityRef Start, int MaxDepth = 3,
    int MaxVertices = 1_000, int MaxEdges = 5_000, string[]? Labels = null);
public sealed record GraphTraversal(EntityRef[] Vertices, EdgeRecord[] Edges);
public sealed record ReadSamplesRequest(PartitionRef Partition, string Set, string SeriesId, DateTimeOffset From, DateTimeOffset Until, int Limit = 1_000);
public sealed record QueryRequest(PartitionRef Partition, string Sql, Dictionary<string, JsonElement>? Parameters = null,
    bool AllowFullScan = false, string? Cursor = null);
public sealed record QueryRow(string EntityId, long Revision, string Json, bool Redacted = false, string[]? RedactedFields = null);
public sealed record QueryPage(QueryRow[] Rows, string? Cursor, long CutPosition, string AccessPath);
public sealed record SearchRequest(PartitionRef Partition, string Collection, string? TextField = null, string? Text = null,
    string? VectorField = null, float[]? Vector = null, VectorSpace? Space = null, int Limit = 10,
    double TextWeight = 1, double VectorWeight = 1, int FusionConstant = 60);
public sealed record BackupReceipt(string Id, long Position);
public sealed record NodeStatus(string NodeId, Guid Incarnation, long Applied, string? Leader, int Voters, DurabilityProfile Durability, bool RoutingReady, int ProcessId);
