using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record SqlOffsetClusterState(PartitionRef Partition, CommandRequest Seed, CommitReceipt Receipt,
    McpPersistedIdentity Identity, EntityRefLifetime LeftLifetime, EntityRefLifetime RightLifetime)
{
    internal EntityRef Stored(string collection, string id) => new(Partition, collection, id)
    {
        Lifetime = collection == SqlOffsetClusterProtocol.Left ? LeftLifetime : RightLifetime
    };
}
