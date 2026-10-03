using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.Search;
using KeyLoad.Core.Features.TimeSeries;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private DocumentRecord VisibleVertex(IKeyValueView view, PrincipalRecord principal, EntityRef vertex, ReadExecutionBudget? budget = null)
    {
        Authorization.Require(principal, vertex.Partition, vertex.Collection, Capability.DocumentsRead);
        Resource(view, vertex.Partition, vertex.Collection, ResourceKind.Collection);
        var key = DocumentKey(vertex.Partition, vertex.Collection, vertex.Id);
        var document = budget is null ? view.GetRecord<DocumentRecord>(key) : budget.ReadRecord<DocumentRecord>(view, key);
        if (document is null || document.Deleted || !Authorization.CanReadRow(principal, document.Access))
        {
            throw Errors.Fail(ErrorCode.NotFound, "The graph vertex is unavailable.");
        }

        return document;
    }
    private static byte[] EdgeKey(PartitionRef partition, string graph, string id) => KeySpace.Partition("edge", partition, graph, id);
    private static byte[] AdjacencyKey(PartitionRef partition, string graph, string direction, EntityRef vertex, string? edgeId = null)
        => KeySpace.Partition("adjacency", partition, edgeId is null ? [graph, direction, vertex.Collection, vertex.Id]
            : [graph, direction, vertex.Collection, vertex.Id, edgeId]);
    private MutationReceipt Upsert(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, UpsertEdge edge)
    {
        JsonData.Identifier(edge.EdgeId);
        JsonData.Identifier(edge.Label);
        var resource = Resource(tx, partition, edge.Graph, ResourceKind.Graph);
        if (edge.From.Partition != partition || edge.To.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, "This graph write requires endpoints in the same atomic partition.");
        }

        VisibleVertex(tx, principal, edge.From);
        VisibleVertex(tx, principal, edge.To);
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        var key = EdgeKey(partition, edge.Graph, edge.EdgeId);
        var previous = tx.GetRecord<EdgeRecord>(key);
        CheckRevision(previous?.Revision ?? 0, edge.ExpectedRevision);
        if (previous is not null)
        {
            RemoveAdjacency(tx, partition, edge.Graph, previous);
        }

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
        VisibleVertex(tx, principal, previous.From);
        VisibleVertex(tx, principal, previous.To);
        CheckRevision(previous.Revision, edge.ExpectedRevision);
        RemoveAdjacency(tx, partition, edge.Graph, previous);
        tx.Delete(key);
        return new("deleteEdge", edge.Graph, edge.EdgeId, previous.Revision + 1);
    }
    /// <summary>Traverses visible graph edges in breadth-first order within one committed read cut.</summary>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="partition">Atomic graph partition.</param>
    /// <param name="graph">Configured graph resource.</param>
    /// <param name="start">Qualified starting vertex, whose errors propagate.</param>
    /// <param name="maxDepth">Maximum edge depth from the start.</param>
    /// <param name="maxVertices">Maximum retained visible vertices.</param>
    /// <param name="maxEdges">Maximum examined adjacency edges, including hidden and filtered edges.</param>
    /// <param name="labels">Optional allowed edge labels.</param>
    /// <param name="cancellationToken">Cancellation across storage and result work.</param>
    /// <returns>Qualified vertices and projected edges in deterministic order.</returns>
    public GraphTraversal Traverse(string principalId, PartitionRef partition, string graph, EntityRef start,
        int maxDepth = 3, int maxVertices = 1_000, int maxEdges = 5_000, string[]? labels = null,
        CancellationToken cancellationToken = default)
    {
        const string InvalidTraversalBudget = "The graph traversal budget is invalid.";
        var budget = new ReadExecutionBudget(Limits, Clock, cancellationToken);
        budget.Check();
        return Store.Read(view =>
        {
            if (maxDepth is < 0 or > 16 || maxVertices is < 1 or > 10_000 || maxEdges is < 1 or > 50_000 || start.Partition != partition)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidTraversalBudget);
            }
            var principal = Principal(view, principalId, Clock.GetUtcNow());
            Authorization.Require(principal, partition, graph, Capability.GraphRead);
            var resource = Resource(view, partition, graph, ResourceKind.Graph);
            return new GraphTraversalReader(this, view, principal, resource, partition, graph, start,
                maxDepth, maxVertices, maxEdges, labels, budget).Read();
        });
    }

    private MutationReceipt Append(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, AppendSamples append)
    {
        JsonData.Identifier(append.SeriesId);
        var resource = Resource(tx, partition, append.SeriesSet, ResourceKind.TimeSeries);
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        if (append.Samples.Length is < 1 or > 10_000)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, "The sample batch exceeds its budget.");
        }

        var tags = JsonData.Validate(append.TagsJson, Limits);
        var sequenceKey = KeySpace.Partition("sample-sequence", partition, append.SeriesSet, append.SeriesId);
        var sequence = tx.ReadOwnedValue(sequenceKey) is { } bytes ? NativeSerialization.Deserialize<long>(bytes) : 0;
        var retention = SampleRetentionStateReader.Read(tx, partition, append.SeriesSet, append.SeriesId);
        foreach (var sample in append.Samples)
        {
            JsonData.Identifier(sample.EventId);
            if (!double.IsFinite(sample.Value))
            {
                throw Errors.Fail(ErrorCode.Validation, "A sample value must be finite.");
            }

            var idKey = KeySpace.Partition("sample-id", partition, append.SeriesSet, append.SeriesId, sample.EventId);
            var fingerprint = JsonData.Fingerprint(new { sample, Tags = tags });
            if (tx.ReadOwnedValue(idKey) is { } existing)
            {
                if (NativeSerialization.Deserialize<string>(existing) != fingerprint)
                {
                    throw Errors.Fail(ErrorCode.Conflict, "A sample ID was reused with different content.");
                }

                continue;
            }
            if (retention is not null && sample.Timestamp.UtcTicks < retention.BeforeUtcTicks)
            {
                throw Errors.Fail(ErrorCode.HistoryUnavailable, "A sample cannot be appended before the series retention floor.");
            }
            var record = new SampleRecord(append.SeriesId, sample, checked(++sequence), tags);
            tx.PutRecord(KeySpace.Partition("sample", partition, append.SeriesSet, append.SeriesId, sample.Timestamp, sequence), record);
            tx.PutRecord(idKey, fingerprint);
        }
        tx.PutRecord(sequenceKey, sequence);
        return new("appendSamples", append.SeriesSet, append.SeriesId, sequence);
    }
    /// <summary>Reads projected samples in an inclusive UTC timestamp range.</summary>
    /// <param name="principalId">Persisted caller identity.</param>
    /// <param name="partition">Atomic partition containing the series.</param>
    /// <param name="set">Configured series set.</param>
    /// <param name="seriesId">Series identifier.</param>
    /// <param name="from">Inclusive UTC beginning.</param>
    /// <param name="until">Inclusive UTC end.</param>
    /// <param name="limit">Maximum returned samples.</param>
    /// <param name="cancellationToken">Cancellation throughout storage and projection.</param>
    /// <returns>Owned projected samples in timestamp and sequence order.</returns>
    public SampleRecord[] ReadSamples(string principalId, PartitionRef partition, string set, string seriesId,
        DateTimeOffset from, DateTimeOffset until, int limit = 1_000, CancellationToken cancellationToken = default)
    {
        var budget = new ReadExecutionBudget(Limits, Clock, cancellationToken);
        budget.Check();
        var request = new ReadSamplesRequest(partition, set, seriesId, from, until, limit);
        return Store.Read(view => SampleRangeReader.Read(this, Clock, view, principalId, request, budget));
    }
    private MutationReceipt Upsert(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, PutVector vector)
    {
        var resource = Resource(tx, partition, vector.Collection, ResourceKind.Collection);
        Authorization.RequireFieldWrite(principal, resource, vector.Field);
        if (vector.Space.Dimension is < 1 or > 4_096 || vector.Values.Length != vector.Space.Dimension || vector.Values.Any(v => !float.IsFinite(v)))
        {
            throw Errors.Fail(ErrorCode.Validation, "The vector dimension or values are invalid.");
        }

        JsonData.Identifier(vector.Space.Id);
        JsonData.PathSegments(vector.Field);
        var document = VisibleVertex(tx, principal, new(partition, vector.Collection, vector.Id));
        Authorization.RequireWriteRow(principal, document.Access);
        CheckRevision(document.Revision, vector.ExpectedDocumentRevision);
        tx.PutRecord(KeySpace.Partition("vector", partition, vector.Collection, vector.Field, vector.Id),
            new VectorRecord(vector.Id, vector.Field, vector.Space, vector.Values, document.Revision));
        return new("putVector", vector.Collection, vector.Id, document.Revision);
    }
    /// <summary>Runs an authorized callback over visible vectors at one storage cut.</summary>
    /// <typeparam name="T">Owned callback result type.</typeparam>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Document collection.</param>
    /// <param name="field">Authorized vector field.</param>
    /// <param name="read">Callback over visible document-vector pairs.</param>
    /// <returns>The callback's owned result.</returns>
    public T WithVectors<T>(string principalId, PartitionRef partition, string collection, string field,
        Func<PrincipalRecord, ResourceDefinition, (DocumentRecord Document, VectorRecord Vector)[], T> read) => Store.Read(view =>
    {
        var principal = Principal(view, principalId, Clock.GetUtcNow());
        Authorization.Require(principal, partition, collection, Capability.VectorSearch);
        var resource = Resource(view, partition, collection, ResourceKind.Collection);
        Authorization.RequireFieldUse(principal, resource, field);
        return read(principal, resource, ReadVisibleVectors(view, principal, partition, collection, field));
    });
    /// <summary>Materializes visible revision-matching vector pairs under a shared budget.</summary>
    /// <param name="view">Current gated storage view.</param>
    /// <param name="principal">Persisted principal.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Document collection.</param>
    /// <param name="field">Authorized vector field.</param>
    /// <param name="budget">Shared read budget, or the configured default.</param>
    /// <returns>Owned visible document-vector pairs.</returns>
    public (DocumentRecord Document, VectorRecord Vector)[] ReadVisibleVectors(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, string collection, string field, ReadExecutionBudget? budget = null)
    {
        budget ??= new(Limits, timeProvider: Clock);
        var eligible = new List<(DocumentRecord, VectorRecord)>();
        VisitVisibleVectors(view, principal, partition, collection, field, budget,
            (document, vector) => eligible.Add((document, vector)));
        return eligible.ToArray();
    }

    /// <summary>Visits visible revision-matching vector pairs inside the existing read cut.</summary>
    /// <param name="view">The gate-scoped read view.</param>
    /// <param name="principal">Persisted, verified principal.</param>
    /// <param name="partition">Owning atomic partition.</param>
    /// <param name="collection">Configured collection.</param>
    /// <param name="field">Authorized vector field.</param>
    /// <param name="budget">Shared cancellation and cumulative resource limits.</param>
    /// <param name="visitor">Synchronous callback; it cannot escape or mutate the read view.</param>
    public void VisitVisibleVectors(IKeyValueView view, PrincipalRecord principal, PartitionRef partition,
        string collection, string field, ReadExecutionBudget budget, Action<DocumentRecord, VectorRecord> visitor)
        => VisibleVectorReads.Visit(this, view, principal, partition, collection, field, budget, visitor);
}
