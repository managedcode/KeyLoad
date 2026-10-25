using System.Collections.Immutable;

namespace KeyLoad.ClusterTests.GraphTraversal;

internal static class GraphLabelUseClusterProtocol
{
    internal const string Collection = "label_use_vertices";
    internal const string Graph = "label_use_graph";
    internal const string ReadGrant = "label.read";
    internal const string UseGrant = "label.use";
    internal const string AttributeJson = "{\"label\":\"attribute-value\",\"weight\":1}";
    internal const string SdkDenied = "The query requires a protected field-use grant.";
    internal const string McpDenied = "The authenticated principal is not permitted to perform this operation.";
    internal static EntityRef Vertex(PartitionRef partition, string id) => new(partition, Collection, id);
    internal static EdgeRecord[] Edges(PartitionRef partition)
        => [new("ab", Vertex(partition, "a"), Vertex(partition, "b"), "route", AttributeJson, 1L),
            new("bc", Vertex(partition, "b"), Vertex(partition, "c"), "skip", AttributeJson, 1L)];
    internal static TraverseRequest Request(PartitionRef partition, string[]? labels, bool missing = false)
        => new(partition, Graph, Vertex(partition, missing ? "missing-start" : "a"), 2, 3, 2,
            labels is null ? null : ImmutableArray.CreateRange(labels));
    internal static global::KeyLoad.GraphTraversal Expected(PartitionRef partition, string[]? labels)
        => labels is null || labels.Length == 0
            ? new([Vertex(partition, "a"), Vertex(partition, "b"), Vertex(partition, "c")], [.. Edges(partition)])
            : labels.Contains("route", StringComparer.Ordinal)
                ? new([Vertex(partition, "a"), Vertex(partition, "b")], [Edges(partition)[0]])
                : new([Vertex(partition, "a")], []);
}
