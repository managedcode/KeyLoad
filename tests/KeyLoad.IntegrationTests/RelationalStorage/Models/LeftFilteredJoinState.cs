using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.RelationalStorage;

internal sealed record LeftFilteredJoinState(PartitionRef Partition, CommandRequest Seed,
    CommitReceipt Receipt, McpPersistedIdentity Identity);
