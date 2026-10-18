using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record QueryRuleOptimizationClusterState(PartitionRef Partition, bool IncludeNull, bool IncludeMissing,
    McpPersistedIdentity Reader, OutboxStatus Outbox,
    (CommandRequest Command, CommitReceipt Receipt)[] Originals);
