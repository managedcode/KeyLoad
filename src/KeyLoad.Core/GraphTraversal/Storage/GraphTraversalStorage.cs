namespace KeyLoad.Core;
/// <summary>Executes GraphTraversalStorage operations against the caller's database context.</summary>
public static class GraphTraversalStorage
{
    internal const string GraphEdgeKeySpace = "edge";
    internal const string GraphAdjacencyKeySpace = "adjacency";
    internal static byte[] EdgeKey(PartitionRef partition, string graph, string id) => KeySpace.Partition(GraphEdgeKeySpace, partition, graph, id);
    internal static byte[] AdjacencyKey(PartitionRef partition, string graph, string direction, EntityRef vertex, string? edgeId = null) => KeySpace.Partition(GraphAdjacencyKeySpace, partition, edgeId is null ? [graph, direction, vertex.Collection, vertex.Id] : [graph, direction, vertex.Collection, vertex.Id, edgeId]);
}
