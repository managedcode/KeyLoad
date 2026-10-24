using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record QueryCapabilityVersionClusterState(CommandRequest Seed, CommitReceipt Receipt,
    McpPersistedIdentity Reader, OutboxStatus Outbox)
{
    internal PartitionRef Partition => Seed.Partition;
}
