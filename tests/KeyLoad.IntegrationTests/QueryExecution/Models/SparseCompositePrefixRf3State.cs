using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SparseCompositePrefixRf3State(PartitionRef Partition, bool IncludeNull, bool IncludeMissing,
    CommandRequest Seed, CommitReceipt Receipt, McpPersistedIdentity Identity, OutboxStatus Outbox);
