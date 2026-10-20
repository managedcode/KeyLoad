using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes GraphTraversalCommandsGraphMutations operations against the caller's database context.</summary>
public static class GraphTraversalCommandsGraphMutations
{
    internal const string GraphEdgeSourceMismatch = "The graph edge source must match its command partition.";
    internal const string GraphEdgeUnavailable = "The edge is unavailable.";
    internal const string GraphCanonicalKeyMismatch = "A canonical graph edge key is inconsistent.";
    internal const string GraphOwnerVersionMismatch = "The source graph edge and its owner version are inconsistent.";
    internal const string GraphRevisionExhausted = "The graph edge revision is exhausted.";
    internal const string GraphIncomingAdjacencyDirection = "in";
    internal const string GraphOutgoingAdjacencyDirection = "out";
    internal static MutationReceipt Upsert(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, UpsertEdge edge)
    {
        const int AbsentRevision = 0;
        JsonData.Identifier(edge.EdgeId);
        JsonData.Identifier(edge.Label);
        var resource = engine.Resource(tx, partition, edge.Graph, ResourceKind.Graph);
        if (edge.From.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, GraphEdgeSourceMismatch);
        }

        var from = engine.VisibleVertex(tx, principal, edge.From).Reference;
        var to = engine.VisibleVertex(tx, principal, edge.To).Reference;
        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        var key = GraphTraversalStorage.EdgeKey(partition, edge.Graph, edge.EdgeId);
        var previous = tx.GetRecord<EdgeRecord>(key);
        if (previous is not null)
        {
            engine.ValidateCanonicalEdge(previous, partition, edge.EdgeId);
        }

        var ownerVersion = CurrentOwnerVersion(tx, partition, edge.Graph, edge.EdgeId, previous);
        DocumentStorageExecutionDocuments.CheckRevision(ownerVersion?.Revision ?? previous?.Revision ?? AbsentRevision, edge.ExpectedRevision);
        var nextRevision = NextRevision(ownerVersion?.Revision ?? previous?.Revision ?? AbsentRevision);
        var record = new EdgeRecord(edge.EdgeId, from, to, edge.Label, JsonData.Validate(edge.AttributesJson, engine.Limits), nextRevision);
        if (edge.To.Partition != partition)
        {
            engine.RequireGraphWrite(tx, principal, partition, edge.To.Partition, edge.Graph);
        }

        PersistCanonicalEdge(tx, partition, edge, key, previous, record);
        PersistOwnerVersion(tx, partition, edge.Graph, edge.EdgeId, nextRevision, deleted: false);
        GraphCrossPartitionIntentWriter.Persist(engine, tx, principal, partition, edge.Graph, edge.EdgeId, previous, record, nextRevision, engine.Limits);
        return new(MutationDiscriminatorNames.UpsertEdge, edge.Graph, edge.EdgeId, nextRevision);
    }

    internal static MutationReceipt RemoveEdge(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, DeleteEdge edge)
    {
        engine.Resource(tx, partition, edge.Graph, ResourceKind.Graph);
        var key = GraphTraversalStorage.EdgeKey(partition, edge.Graph, edge.EdgeId);
        var previous = tx.GetRecord<EdgeRecord>(key) ?? throw Errors.Fail(ErrorCode.NotFound, GraphEdgeUnavailable);
        engine.ValidateCanonicalEdge(previous, partition, edge.EdgeId);
        engine.VisibleVertex(tx, principal, previous.From);
        engine.VisibleVertex(tx, principal, previous.To);
        var ownerVersion = CurrentOwnerVersion(tx, partition, edge.Graph, edge.EdgeId, previous);
        var currentRevision = ownerVersion?.Revision ?? previous.Revision;
        DocumentStorageExecutionDocuments.CheckRevision(currentRevision, edge.ExpectedRevision);
        var nextRevision = NextRevision(currentRevision);
        RemoveAdjacency(tx, partition, edge.Graph, previous);
        tx.Delete(key);
        PersistOwnerVersion(tx, partition, edge.Graph, edge.EdgeId, nextRevision, deleted: true);
        GraphCrossPartitionIntentWriter.Persist(engine, tx, principal, partition, edge.Graph, edge.EdgeId, previous, null, nextRevision, engine.Limits);
        return new(MutationDiscriminatorNames.DeleteEdge, edge.Graph, edge.EdgeId, nextRevision);
    }

    internal static void PersistCanonicalEdge(IAtomicTransaction tx, PartitionRef partition, UpsertEdge request, byte[] key, EdgeRecord? previous, EdgeRecord record)
    {
        if (previous is not null)
        {
            RemoveAdjacency(tx, partition, request.Graph, previous);
        }

        tx.PutRecord(key, record);
        tx.PutRecord(GraphTraversalStorage.AdjacencyKey(partition, request.Graph, GraphOutgoingAdjacencyDirection, record.From, record.Id), record.Id);
        if (record.To.Partition == partition)
        {
            tx.PutRecord(GraphTraversalStorage.AdjacencyKey(partition, request.Graph, GraphIncomingAdjacencyDirection, record.To, record.Id), record.Id);
        }
    }

    internal static void RemoveAdjacency(IAtomicTransaction tx, PartitionRef partition, string graph, EdgeRecord edge)
    {
        tx.Delete(GraphTraversalStorage.AdjacencyKey(partition, graph, GraphOutgoingAdjacencyDirection, edge.From, edge.Id));
        tx.Delete(GraphTraversalStorage.AdjacencyKey(partition, graph, GraphIncomingAdjacencyDirection, edge.To, edge.Id));
    }

    internal static void ValidateCanonicalEdge(this DatabaseEngine engine, EdgeRecord edge, PartitionRef source, string edgeId)
    {
        GraphCrossPartitionValidation.ValidateEdge(edge, engine.Limits);
        if (edge.Id != edgeId || edge.From.Partition != source)
        {
            throw Errors.Fail(ErrorCode.Corruption, GraphCanonicalKeyMismatch);
        }
    }

    internal static GraphEdgeOwnerVersion? CurrentOwnerVersion(IKeyValueView view, PartitionRef partition, string graph, string edgeId, EdgeRecord? edge)
    {
        var owner = GraphCrossPartitionRecords.Read<GraphEdgeOwnerVersion>(view, GraphCrossPartitionKeys.OwnerVersion(partition, graph, edgeId));
        if (owner is null)
        {
            return null;
        }

        GraphCrossPartitionValidation.ValidateOwnerVersion(owner);
        if (edge is null ? !owner.Deleted : owner.Deleted || edge.Revision != owner.Revision)
        {
            throw Errors.Fail(ErrorCode.Corruption, GraphOwnerVersionMismatch);
        }

        return owner;
    }

    internal static long NextRevision(long revision)
    {
        const int RevisionIncrement = 1;
        try
        {
            return checked(revision + RevisionIncrement);
        }
        catch (OverflowException)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GraphRevisionExhausted);
        }
    }

    internal static void PersistOwnerVersion(IAtomicTransaction tx, PartitionRef partition, string graph, string edgeId, long revision, bool deleted) => tx.PutRecord(GraphCrossPartitionKeys.OwnerVersion(partition, graph, edgeId), new GraphEdgeOwnerVersion(GraphCrossPartitionProtocol.CurrentVersion, revision, deleted));
}
