using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.GraphTraversal;

internal sealed record GraphIncomingClusterSeed(
    PartitionRef TargetPartition,
    PartitionRef FirstSourcePartition,
    PartitionRef SecondSourcePartition,
    EntityRef Target,
    EntityRef LocalSource,
    EntityRef FirstSource,
    EntityRef SecondSource,
    McpPersistedIdentity Reader,
    McpPersistedIdentity TargetOnlyReader,
    string Graph,
    ImmutableDictionary<string, EntityRefLifetime> CollectionLifetimes)
{
    private const string UnseededVertex = "The vertex is outside the seeded incoming-edge collections.";

    internal EntityRef Stored(EntityRef vertex)
        => vertex.Collection == GraphIncomingClusterScenario.Collection
            && vertex.Partition.TenantId == TargetPartition.TenantId
            && CollectionLifetimes.TryGetValue(vertex.Partition.DatabaseId, out var lifetime)
            ? vertex with { Lifetime = lifetime }
            : throw new InvalidOperationException(UnseededVertex);

    internal EdgeRecord StoredEdge(EdgeRecord edge) => GraphStoredReferences.Edge(edge, Stored);

    internal global::KeyLoad.GraphTraversal StoredTraversal(global::KeyLoad.GraphTraversal traversal)
        => GraphStoredReferences.Traversal(traversal, Stored);

    public override string ToString() => "GraphIncomingClusterSeed";
}
