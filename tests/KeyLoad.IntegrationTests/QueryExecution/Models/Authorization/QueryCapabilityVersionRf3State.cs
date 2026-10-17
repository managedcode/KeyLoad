using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record QueryCapabilityVersionRf3State(CommandRequest Seed, CommitReceipt Receipt,
    McpPersistedIdentity Reader, OutboxStatus Outbox)
{
    internal PartitionRef Partition => Seed.Partition;
}
