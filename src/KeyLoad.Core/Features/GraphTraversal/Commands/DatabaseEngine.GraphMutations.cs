using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.GraphTraversal.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string GraphEdgeSourceMismatch = "The graph edge source must match its command partition.";
    private const string GraphEdgeUnavailable = "The edge is unavailable.";
    private const string GraphCanonicalKeyMismatch = "A canonical graph edge key is inconsistent.";
    private const string GraphOwnerVersionMismatch = "The source graph edge and its owner version are inconsistent.";
    private const string GraphRevisionExhausted = "The graph edge revision is exhausted.";
    private const string GraphIncomingAdjacencyDirection = "in";
    private const string GraphOutgoingAdjacencyDirection = "out";
    private MutationReceipt Upsert(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition,
        UpsertEdge edge)
    {
        const int AbsentRevision = 0;

        JsonData.Identifier(edge.EdgeId);
        JsonData.Identifier(edge.Label);
        var resource = Resource(tx, partition, edge.Graph, ResourceKind.Graph);
        if (edge.From.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, GraphEdgeSourceMismatch);
        }
        VisibleVertex(tx, principal, edge.From);
        VisibleVertex(tx, principal, edge.To);
        foreach (var policy in resource.FieldPolicies)
        {
            Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        var key = EdgeKey(partition, edge.Graph, edge.EdgeId);
        var previous = tx.GetRecord<EdgeRecord>(key);
        if (previous is not null)
        {
            ValidateCanonicalEdge(previous, partition, edge.EdgeId);
        }
        var ownerVersion = CurrentOwnerVersion(tx, partition, edge.Graph, edge.EdgeId, previous);
        CheckRevision(ownerVersion?.Revision ?? previous?.Revision ?? AbsentRevision, edge.ExpectedRevision);
        var nextRevision = NextRevision(ownerVersion?.Revision ?? previous?.Revision ?? AbsentRevision);
        var record = new EdgeRecord(edge.EdgeId, edge.From, edge.To, edge.Label,
            JsonData.Validate(edge.AttributesJson, Limits), nextRevision);
        if (edge.To.Partition != partition)
        {
            RequireGraphWrite(tx, principal, partition, edge.To.Partition, edge.Graph);
            RequireSameGraphOwner(tx, partition, edge.To.Partition);
        }

        PersistCanonicalEdge(tx, partition, edge, key, previous, record);
        PersistOwnerVersion(tx, partition, edge.Graph, edge.EdgeId, nextRevision, deleted: false);
        GraphCrossPartitionIntentWriter.Persist(this, tx, principal, partition, edge.Graph,
            edge.EdgeId, previous, record, nextRevision, Limits);
        return new(MutationDiscriminatorNames.UpsertEdge, edge.Graph, edge.EdgeId, nextRevision);
    }

    private MutationReceipt RemoveEdge(IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition,
        DeleteEdge edge)
    {
        Resource(tx, partition, edge.Graph, ResourceKind.Graph);
        var key = EdgeKey(partition, edge.Graph, edge.EdgeId);
        var previous = tx.GetRecord<EdgeRecord>(key)
            ?? throw Errors.Fail(ErrorCode.NotFound, GraphEdgeUnavailable);
        ValidateCanonicalEdge(previous, partition, edge.EdgeId);
        VisibleVertex(tx, principal, previous.From);
        VisibleVertex(tx, principal, previous.To);
        var ownerVersion = CurrentOwnerVersion(tx, partition, edge.Graph, edge.EdgeId, previous);
        var currentRevision = ownerVersion?.Revision ?? previous.Revision;
        CheckRevision(currentRevision, edge.ExpectedRevision);
        var nextRevision = NextRevision(currentRevision);
        RemoveAdjacency(tx, partition, edge.Graph, previous);
        tx.Delete(key);
        PersistOwnerVersion(tx, partition, edge.Graph, edge.EdgeId, nextRevision, deleted: true);
        GraphCrossPartitionIntentWriter.Persist(this, tx, principal, partition, edge.Graph,
            edge.EdgeId, previous, null, nextRevision, Limits);
        return new(MutationDiscriminatorNames.DeleteEdge, edge.Graph, edge.EdgeId, nextRevision);
    }

    private static void PersistCanonicalEdge(IAtomicTransaction tx, PartitionRef partition,
        UpsertEdge request, byte[] key, EdgeRecord? previous, EdgeRecord record)
    {
        if (previous is not null)
        {
            RemoveAdjacency(tx, partition, request.Graph, previous);
        }
        tx.PutRecord(key, record);
        tx.PutRecord(AdjacencyKey(partition, request.Graph, GraphOutgoingAdjacencyDirection, record.From, record.Id), record.Id);
        if (record.To.Partition == partition)
        {
            tx.PutRecord(AdjacencyKey(partition, request.Graph, GraphIncomingAdjacencyDirection, record.To, record.Id), record.Id);
        }
    }

    private static void RemoveAdjacency(IAtomicTransaction tx, PartitionRef partition, string graph,
        EdgeRecord edge)
    {
        tx.Delete(AdjacencyKey(partition, graph, GraphOutgoingAdjacencyDirection, edge.From, edge.Id));
        tx.Delete(AdjacencyKey(partition, graph, GraphIncomingAdjacencyDirection, edge.To, edge.Id));
    }

    private void ValidateCanonicalEdge(EdgeRecord edge, PartitionRef source, string edgeId)
    {
        GraphCrossPartitionValidation.ValidateEdge(edge, Limits);
        if (edge.Id != edgeId || edge.From.Partition != source)
        {
            throw Errors.Fail(ErrorCode.Corruption, GraphCanonicalKeyMismatch);
        }
    }

    private static GraphEdgeOwnerVersionV1? CurrentOwnerVersion(IKeyValueView view, PartitionRef partition,
        string graph, string edgeId, EdgeRecord? edge)
    {
        var owner = GraphCrossPartitionRecords.Read<GraphEdgeOwnerVersionV1>(view,
            GraphCrossPartitionKeys.OwnerVersion(partition, graph, edgeId));
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

    private static long NextRevision(long revision)
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

    private static void PersistOwnerVersion(IAtomicTransaction tx, PartitionRef partition,
        string graph, string edgeId, long revision, bool deleted)
        => tx.PutRecord(GraphCrossPartitionKeys.OwnerVersion(partition, graph, edgeId),
            new GraphEdgeOwnerVersionV1(GraphCrossPartitionProtocol.CurrentVersion, revision, deleted));

}
