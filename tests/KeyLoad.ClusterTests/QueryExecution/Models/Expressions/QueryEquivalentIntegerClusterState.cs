using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record QueryEquivalentIntegerClusterState(PartitionRef Partition, CommandRequest Seed,
    CommitReceipt Receipt, McpPersistedIdentity Identity, OutboxStatus Outbox);
