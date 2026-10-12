using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record LeftFilteredJoinState(PartitionRef Partition, CommandRequest Seed,
    CommitReceipt Receipt, McpPersistedIdentity Identity);
