using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.GraphTraversal;

internal sealed record GraphPathClusterSeed(
    PartitionRef Partition,
    EntityRef Source,
    EntityRef FirstBranch,
    EntityRef SecondBranch,
    EntityRef Target,
    EntityRef HiddenSource,
    EntityRef HiddenIntermediate,
    EntityRef HiddenTarget,
    EntityRef Isolated,
    McpPersistedIdentity Reader,
    ImmutableDictionary<string, EntityRefLifetime> CollectionLifetimes)
{
    private const string UnseededVertex = "The vertex is outside the seeded graph-path collections.";

    internal EntityRef Stored(EntityRef vertex)
        => vertex.Partition.TenantId == Partition.TenantId && vertex.Partition.DatabaseId == Partition.DatabaseId
            && CollectionLifetimes.TryGetValue(vertex.Collection, out var lifetime)
            ? vertex with { Lifetime = lifetime }
            : throw new InvalidOperationException(UnseededVertex);

    internal EntityRef[] StoredVertices(IEnumerable<EntityRef> vertices) => [.. vertices.Select(Stored)];

    internal EdgeRecord StoredEdge(EdgeRecord edge) => GraphStoredReferences.Edge(edge, Stored);

    internal global::KeyLoad.GraphTraversal StoredTraversal(global::KeyLoad.GraphTraversal traversal)
        => GraphStoredReferences.Traversal(traversal, Stored);

    public override string ToString() => "GraphPathClusterSeed";
}
