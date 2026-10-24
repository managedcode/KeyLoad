using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record QueryRuleOptimizationClusterState(PartitionRef Partition, bool IncludeNull, bool IncludeMissing,
    McpPersistedIdentity Reader, OutboxStatus Outbox,
    (CommandRequest Command, CommitReceipt Receipt)[] Originals);
