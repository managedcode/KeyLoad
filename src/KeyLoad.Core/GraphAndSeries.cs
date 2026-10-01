using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private DocumentRecord VisibleVertex(IKeyValueView view, PrincipalRecord principal, EntityRef vertex)
    {
        Authorization.Require(principal, vertex.Partition, vertex.Collection, Capability.DocumentsRead);
        Resource(view, vertex.Partition, vertex.Collection, ResourceKind.Collection);
        var document = view.GetRecord<DocumentRecord>(DocumentKey(vertex.Partition, vertex.Collection, vertex.Id));
        if (document is null || document.Deleted || !Authorization.CanReadRow(principal, document.Access))
            throw Errors.Fail(ErrorCode.NotFound, "The graph vertex is unavailable.");
        return document;
    }
    private static byte[] EdgeKey(PartitionRef partition, string graph, string id) => KeySpace.Partition("edge", partition, graph, id);
    private static byte[] AdjacencyKey(PartitionRef partition, string graph, string direction, EntityRef vertex, string? edgeId = null)
        => KeySpace.Partition("adjacency", partition, edgeId is null ? [graph, direction, vertex.Collection, vertex.Id]
            : [graph, direction, vertex.Collection, vertex.Id, edgeId]);
    private MutationReceipt Upsert(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, UpsertEdge edge)
    {
        JsonData.Identifier(edge.EdgeId); JsonData.Identifier(edge.Label);
        var resource = Resource(tx, partition, edge.Graph, ResourceKind.Graph);
        if (edge.From.Partition != partition || edge.To.Partition != partition)
            throw Errors.Fail(ErrorCode.UnsupportedCapability, "This graph write requires endpoints in the same atomic partition.");
        VisibleVertex(tx, principal, edge.From); VisibleVertex(tx, principal, edge.To);
        foreach (var policy in resource.FieldPolicies) Authorization.RequireFieldWrite(principal, resource, policy.Path);
        var key = EdgeKey(partition, edge.Graph, edge.EdgeId);
        var previous = tx.GetRecord<EdgeRecord>(key);
        CheckRevision(previous?.Revision ?? 0, edge.ExpectedRevision);
        if (previous is not null) RemoveAdjacency(tx, partition, edge.Graph, previous);
        var record = new EdgeRecord(edge.EdgeId, edge.From, edge.To, edge.Label, JsonData.Validate(edge.AttributesJson, Limits), checked((previous?.Revision ?? 0) + 1));
        tx.PutRecord(key, record);
        tx.PutRecord(AdjacencyKey(partition, edge.Graph, "out", edge.From, edge.EdgeId), edge.EdgeId);
        tx.PutRecord(AdjacencyKey(partition, edge.Graph, "in", edge.To, edge.EdgeId), edge.EdgeId);
        return new("upsertEdge", edge.Graph, edge.EdgeId, record.Revision);
    }
    private static void RemoveAdjacency(IAtomicTransaction tx, PartitionRef partition, string graph, EdgeRecord edge)
    {
        tx.Delete(AdjacencyKey(partition, graph, "out", edge.From, edge.Id));
        tx.Delete(AdjacencyKey(partition, graph, "in", edge.To, edge.Id));
    }
    private MutationReceipt RemoveEdge(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, DeleteEdge edge)
    {
        Resource(tx, partition, edge.Graph, ResourceKind.Graph);
        var key = EdgeKey(partition, edge.Graph, edge.EdgeId);
        var previous = tx.GetRecord<EdgeRecord>(key) ?? throw Errors.Fail(ErrorCode.NotFound, "The edge is unavailable.");
        VisibleVertex(tx, principal, previous.From); VisibleVertex(tx, principal, previous.To);
        CheckRevision(previous.Revision, edge.ExpectedRevision);
        RemoveAdjacency(tx, partition, edge.Graph, previous);
        tx.Delete(key);
        return new("deleteEdge", edge.Graph, edge.EdgeId, previous.Revision + 1);
    }
    public GraphTraversal Traverse(string principalId, PartitionRef partition, string graph, EntityRef start,
        int maxDepth = 3, int maxVertices = 1_000, int maxEdges = 5_000, string[]? labels = null)
        => Store.Read(view =>
        {
            if (maxDepth is < 0 or > 16 || maxVertices is < 1 or > 10_000 || maxEdges is < 1 or > 50_000 || start.Partition != partition)
                throw Errors.Fail(ErrorCode.BudgetExceeded, "The graph traversal budget is invalid.");
            var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
            Authorization.Require(principal, partition, graph, Capability.GraphRead);
            var resource = Resource(view, partition, graph, ResourceKind.Graph);
            VisibleVertex(view, principal, start);
            var visited = new HashSet<EntityRef> { start };
            var edges = new Dictionary<string, EdgeRecord>(StringComparer.Ordinal);
            var pending = new Queue<(EntityRef Vertex, int Depth)>();
            pending.Enqueue((start, 0));
            while (pending.TryDequeue(out var item))
            {
                if (item.Depth >= maxDepth) continue;
                var page = view.Scan(AdjacencyKey(partition, graph, "out", item.Vertex), Math.Min(maxEdges, 100_000));
                if (page.HasMore) throw Errors.Fail(ErrorCode.BudgetExceeded, "The edge visit budget was exhausted.");
                foreach (var adjacency in page.Records)
                {
                    var id = JsonDefaults.Deserialize<string>(adjacency.Value);
                    var edge = view.GetRecord<EdgeRecord>(EdgeKey(partition, graph, id)) ?? throw Errors.Fail(ErrorCode.Corruption, "An adjacency record has no edge.");
                    if (labels is { Length: > 0 } && !labels.Contains(edge.Label, StringComparer.Ordinal)) continue;
                    try { VisibleVertex(view, principal, edge.To); }
                    catch (KeyLoadException exception) when (exception.Code is ErrorCode.NotFound or ErrorCode.PermissionDenied) { continue; }
                    if (edges.Count >= maxEdges) throw Errors.Fail(ErrorCode.BudgetExceeded, "The edge visit budget was exhausted.");
                    edges[edge.Id] = edge with { AttributesJson = Authorization.Project(principal, resource.FieldPolicies, edge.AttributesJson, out _) };
                    if (visited.Add(edge.To))
                    {
                        if (visited.Count > maxVertices) throw Errors.Fail(ErrorCode.BudgetExceeded, "The vertex visit budget was exhausted.");
                        pending.Enqueue((edge.To, item.Depth + 1));
                    }
                }
            }
            return new GraphTraversal(visited.OrderBy(v => v.Collection, StringComparer.Ordinal).ThenBy(v => v.Id, StringComparer.Ordinal).ToArray(), edges.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToArray());
        });

    private MutationReceipt Append(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendSamples append)
    {
        JsonData.Identifier(append.SeriesId);
        var resource = Resource(tx, partition, append.SeriesSet, ResourceKind.TimeSeries);
        foreach (var policy in resource.FieldPolicies) Authorization.RequireFieldWrite(principal, resource, policy.Path);
        if (append.Samples.Length is < 1 or > 10_000) throw Errors.Fail(ErrorCode.ResourceExhausted, "The sample batch exceeds its budget.");
        var tags = JsonData.Validate(append.TagsJson, Limits);
        var sequenceKey = KeySpace.Partition("sample-sequence", partition, append.SeriesSet, append.SeriesId);
        var sequence = tx.Get(sequenceKey) is { } bytes ? JsonDefaults.Deserialize<long>(bytes) : 0;
        foreach (var sample in append.Samples)
        {
            JsonData.Identifier(sample.EventId);
            if (!double.IsFinite(sample.Value)) throw Errors.Fail(ErrorCode.Validation, "A sample value must be finite.");
            var idKey = KeySpace.Partition("sample-id", partition, append.SeriesSet, append.SeriesId, sample.EventId);
            var fingerprint = JsonData.Fingerprint(new { sample, Tags = tags });
            if (tx.Get(idKey) is { } existing)
            {
                if (JsonDefaults.Deserialize<string>(existing) != fingerprint) throw Errors.Fail(ErrorCode.Conflict, "A sample ID was reused with different content.");
                continue;
            }
            var record = new SampleRecord(append.SeriesId, sample, checked(++sequence), tags);
            tx.PutRecord(KeySpace.Partition("sample", partition, append.SeriesSet, append.SeriesId, sample.Timestamp, sequence), record);
            tx.PutRecord(idKey, fingerprint);
        }
        tx.PutRecord(sequenceKey, sequence);
        return new("appendSamples", append.SeriesSet, append.SeriesId, sequence);
    }
    public SampleRecord[] ReadSamples(string principalId, PartitionRef partition, string set, string seriesId,
        DateTimeOffset from, DateTimeOffset until, int limit = 1_000) => Store.Read(view =>
    {
        if (from > until || limit < 1 || limit > Limits.MaxResults) throw Errors.Fail(ErrorCode.BudgetExceeded, "The series read budget is invalid.");
        var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
        Authorization.Require(principal, partition, set, Capability.SeriesRead);
        var resource = Resource(view, partition, set, ResourceKind.TimeSeries);
        var prefix = KeySpace.Partition("sample", partition, set, seriesId);
        var after = KeySpace.Partition("sample", partition, set, seriesId, from);
        var records = view.Scan(prefix, limit, after).Records.Select(kv => JsonDefaults.Deserialize<SampleRecord>(kv.Value))
            .TakeWhile(s => s.Sample.Timestamp <= until).Select(s => s with { TagsJson = Authorization.Project(principal, resource.FieldPolicies, s.TagsJson, out _) }).ToArray();
        return records;
    });
    private MutationReceipt Upsert(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PutVector vector)
    {
        var resource = Resource(tx, partition, vector.Collection, ResourceKind.Collection);
        Authorization.RequireFieldWrite(principal, resource, vector.Field);
        if (vector.Space.Dimension is < 1 or > 4_096 || vector.Values.Length != vector.Space.Dimension || vector.Values.Any(v => !float.IsFinite(v)))
            throw Errors.Fail(ErrorCode.Validation, "The vector dimension or values are invalid.");
        JsonData.Identifier(vector.Space.Id); JsonData.PathSegments(vector.Field);
        var document = VisibleVertex(tx, principal, new(partition, vector.Collection, vector.Id));
        Authorization.RequireWriteRow(principal, document.Access);
        CheckRevision(document.Revision, vector.ExpectedDocumentRevision);
        tx.PutRecord(KeySpace.Partition("vector", partition, vector.Collection, vector.Field, vector.Id),
            new VectorRecord(vector.Id, vector.Field, vector.Space, vector.Values, document.Revision));
        return new("putVector", vector.Collection, vector.Id, document.Revision);
    }
    public T WithVectors<T>(string principalId, PartitionRef partition, string collection, string field,
        Func<PrincipalRecord, ResourceDefinition, (DocumentRecord Document, VectorRecord Vector)[], T> read) => Store.Read(view =>
    {
        var principal = Principal(view, principalId, DateTimeOffset.UtcNow);
        Authorization.Require(principal, partition, collection, Capability.VectorSearch);
        var resource = Resource(view, partition, collection, ResourceKind.Collection);
        Authorization.RequireFieldUse(principal, resource, field);
        return read(principal, resource, ReadVisibleVectors(view, principal, partition, collection, field));
    });
    public (DocumentRecord Document, VectorRecord Vector)[] ReadVisibleVectors(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, string field)
    {
        var page = view.Scan(KeySpace.Partition("vector", partition, collection, field), Limits.MaxScanRecords);
        if (page.HasMore) throw Errors.Fail(ErrorCode.BudgetExceeded, "The exact vector scan exceeds its budget.");
        var eligible = new List<(DocumentRecord, VectorRecord)>();
        foreach (var kv in page.Records)
        {
            var vector = JsonDefaults.Deserialize<VectorRecord>(kv.Value);
            var document = view.GetRecord<DocumentRecord>(DocumentKey(partition, collection, vector.DocumentId));
            if (document is null || document.Deleted || document.Revision != vector.DocumentRevision || !Authorization.CanReadRow(principal, document.Access)) continue;
            eligible.Add((document, vector));
        }
        return eligible.ToArray();
    }
}
