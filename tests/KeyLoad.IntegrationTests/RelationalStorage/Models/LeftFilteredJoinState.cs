using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.RelationalStorage;

internal sealed record LeftFilteredJoinState(PartitionRef Partition, CommandRequest Seed,
    CommitReceipt Receipt, McpPersistedIdentity Identity, EntityRefLifetime LeftLifetime, EntityRefLifetime RightLifetime)
{
    internal EntityRef Stored(string collection, string id) => new(Partition, collection, id)
    {
        Lifetime = collection == LeftFilteredJoinProtocol.Left ? LeftLifetime : RightLifetime
    };
}
