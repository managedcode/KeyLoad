using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record QueryCapabilityVersionClusterState(CommandRequest Seed, CommitReceipt Receipt,
    McpPersistedIdentity Reader, OutboxStatus Outbox, EntityRefLifetime Lifetime)
{
    internal PartitionRef Partition => Seed.Partition;
    internal EntityRef Stored(string id) => new(Partition, QueryCapabilityVersionClusterRequests.Collection, id) { Lifetime = Lifetime };
}
