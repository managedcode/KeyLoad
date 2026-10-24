using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal sealed record MetamorphicPublicState(PartitionRef Partition, ImmutableArray<MetamorphicPublicRow> Rows,
    CommandRequest Seed, CommitReceipt Receipt, McpPersistedIdentity Identity, OutboxStatus Outbox);
