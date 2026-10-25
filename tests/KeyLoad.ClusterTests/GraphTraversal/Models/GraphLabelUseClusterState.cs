using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.GraphTraversal;

internal sealed record GraphLabelUseClusterState(McpPersistedIdentity Reader, CommandRequest Seed, CommitReceipt Receipt,
    EntityRefLifetime VertexLifetime)
{
    internal EntityRef Stored(EntityRef vertex) => vertex with { Lifetime = VertexLifetime };

    internal global::KeyLoad.GraphTraversal Expected(string[]? labels)
        => GraphStoredReferences.Traversal(GraphLabelUseClusterProtocol.Expected(Seed.Partition, labels), Stored);
}
