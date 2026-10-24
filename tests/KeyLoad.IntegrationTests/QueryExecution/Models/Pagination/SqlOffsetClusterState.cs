using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record SqlOffsetClusterState(PartitionRef Partition, CommandRequest Seed, CommitReceipt Receipt,
    McpPersistedIdentity Identity);
