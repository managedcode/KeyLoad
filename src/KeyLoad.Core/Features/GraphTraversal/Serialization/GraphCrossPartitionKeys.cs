using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.GraphTraversal.Serialization;

internal static class GraphCrossPartitionKeys
{
    private const string IncomingDirection = "in";
    private const string PendingIntentCapacity = "pending-intents";
    private const string ReceiverCapacity = "receiver-states";

    internal static byte[] OwnerVersion(PartitionRef source, string graph, string edgeId)
        => KeySpace.Partition(PartitionRecordFamilies.GraphEdgeOwnerVersion, source, graph, edgeId);

    internal static byte[] CanonicalEdge(PartitionRef source, string graph, string edgeId)
        => KeySpace.Partition(PartitionRecordFamilies.Edge, source, graph, edgeId);

    internal static byte[] IntentPrefix(PartitionRef source)
        => KeySpace.Partition(PartitionRecordFamilies.GraphCrossPartitionIntent, source);

    internal static byte[] IntentEdgePrefix(PartitionRef source, string graph, string edgeId)
        => KeySpace.Partition(PartitionRecordFamilies.GraphCrossPartitionIntent, source, graph, edgeId);

    internal static byte[] Intent(GraphCrossPartitionDeliveryIntentV1 intent)
        => KeySpace.Partition(PartitionRecordFamilies.GraphCrossPartitionIntent, intent.SourcePartition, intent.Graph, intent.EdgeId,
            intent.Destination.Partition.TenantId, intent.Destination.Partition.DatabaseId,
            intent.Destination.Partition.TransactionDomainId, intent.Destination.Partition.PartitionKey,
            intent.Destination.Collection, intent.Destination.Id);

    internal static byte[] Intent(PartitionRef source, string graph, string edgeId, EntityRef destination)
        => KeySpace.Partition(PartitionRecordFamilies.GraphCrossPartitionIntent, source, graph, edgeId, destination.Partition.TenantId,
            destination.Partition.DatabaseId, destination.Partition.TransactionDomainId,
            destination.Partition.PartitionKey, destination.Collection, destination.Id);

    internal static byte[] Capacity(PartitionRef partition, GraphCrossPartitionCapacityDirection direction)
        => KeySpace.Partition(PartitionRecordFamilies.GraphCrossPartitionCapacity, partition, direction == GraphCrossPartitionCapacityDirection.PendingIntents
            ? PendingIntentCapacity : ReceiverCapacity);

    internal static byte[] ReversePartitionPrefix(PartitionRef partition)
        => KeySpace.Partition(PartitionRecordFamilies.GraphCrossReverseAdjacency, partition);

    internal static byte[] ReversePrefix(EntityRef target, string graph)
        => KeySpace.Partition(PartitionRecordFamilies.GraphCrossReverseAdjacency, target.Partition, graph, IncomingDirection,
            target.Collection, target.Id);

    internal static byte[] LocalIncomingPrefix(EntityRef target, string graph)
        => KeySpace.Partition(PartitionRecordFamilies.Adjacency, target.Partition, graph, IncomingDirection,
            target.Collection, target.Id);

    internal static byte[] LocalIncoming(PartitionRef partition, string graph,
        EntityRef target, string edgeId)
        => KeySpace.Partition(PartitionRecordFamilies.Adjacency, partition, graph, IncomingDirection,
            target.Collection, target.Id, edgeId);

    internal static byte[] Reverse(PartitionRef targetPartition, string graph, EntityRef target,
        PartitionRef sourcePartition, string edgeId)
        => KeySpace.Partition(PartitionRecordFamilies.GraphCrossReverseAdjacency, targetPartition, graph, IncomingDirection, target.Collection,
            target.Id, sourcePartition.TenantId, sourcePartition.DatabaseId,
            sourcePartition.TransactionDomainId, sourcePartition.PartitionKey, graph, edgeId);
}
