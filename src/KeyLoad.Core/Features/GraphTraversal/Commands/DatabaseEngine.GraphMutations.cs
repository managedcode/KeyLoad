using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
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
}
