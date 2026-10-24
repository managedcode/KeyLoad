using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record QueryCapabilityVersionClusterState(CommandRequest Seed, CommitReceipt Receipt,
    McpPersistedIdentity Reader, OutboxStatus Outbox, EntityRefLifetime Lifetime)
{
    internal PartitionRef Partition => Seed.Partition;
    internal EntityRef Stored(string id) => new(Partition, QueryCapabilityVersionClusterRequests.Collection, id) { Lifetime = Lifetime };
}
