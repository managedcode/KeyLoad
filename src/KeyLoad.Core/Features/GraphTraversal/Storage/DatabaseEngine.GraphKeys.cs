namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static byte[] EdgeKey(PartitionRef partition, string graph, string id) => KeySpace.Partition("edge", partition, graph, id);
    private static byte[] AdjacencyKey(PartitionRef partition, string graph, string direction, EntityRef vertex, string? edgeId = null)
        => KeySpace.Partition("adjacency", partition, edgeId is null ? [graph, direction, vertex.Collection, vertex.Id]
            : [graph, direction, vertex.Collection, vertex.Id, edgeId]);
}
