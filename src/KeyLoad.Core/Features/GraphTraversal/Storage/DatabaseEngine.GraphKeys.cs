namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string GraphEdgeKeySpace = "edge";
    private const string GraphAdjacencyKeySpace = "adjacency";

    private static byte[] EdgeKey(PartitionRef partition, string graph, string id) => KeySpace.Partition(GraphEdgeKeySpace, partition, graph, id);
    private static byte[] AdjacencyKey(PartitionRef partition, string graph, string direction, EntityRef vertex, string? edgeId = null)
        => KeySpace.Partition(GraphAdjacencyKeySpace, partition, edgeId is null ? [graph, direction, vertex.Collection, vertex.Id]
            : [graph, direction, vertex.Collection, vertex.Id, edgeId]);
}
