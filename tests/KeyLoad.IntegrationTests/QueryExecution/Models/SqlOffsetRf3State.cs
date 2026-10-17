using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlOffsetRf3State(PartitionRef Partition, CommandRequest Seed, CommitReceipt Receipt,
    McpPersistedIdentity Identity);
