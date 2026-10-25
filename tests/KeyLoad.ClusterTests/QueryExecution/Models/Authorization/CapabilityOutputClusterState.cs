using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record CapabilityOutputClusterState(McpPersistedIdentity Reader,
    (CommandRequest Command, CommitReceipt Receipt)[] Seeds, QueryCapabilityManifest Manifest, EntityRefLifetime Lifetime)
{
    internal EntityRef Stored(string id) => new(PostgresClusterState.Partition, CapabilityOutputClusterProtocol.Collection, id)
    {
        Lifetime = Lifetime
    };
}
