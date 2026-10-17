using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record MetamorphicPublicState(PartitionRef Partition, ImmutableArray<MetamorphicPublicRow> Rows,
    CommandRequest Seed, CommitReceipt Receipt, McpPersistedIdentity Identity, OutboxStatus Outbox);
