using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record CapabilityOutputClusterState(McpPersistedIdentity Reader,
    (CommandRequest Command, CommitReceipt Receipt)[] Seeds, QueryCapabilityManifest Manifest, EntityRefLifetime Lifetime)
{
    internal EntityRef Stored(string id) => new(PostgresClusterState.Partition, CapabilityOutputClusterProtocol.Collection, id)
    {
        Lifetime = Lifetime
    };
}
