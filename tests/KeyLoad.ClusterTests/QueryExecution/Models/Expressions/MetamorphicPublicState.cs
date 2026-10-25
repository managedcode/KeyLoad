using System.Collections.Immutable;
using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record MetamorphicPublicState(PartitionRef Partition, ImmutableArray<MetamorphicPublicRow> Rows,
    CommandRequest Seed, CommitReceipt Receipt, McpPersistedIdentity Identity, OutboxStatus Outbox);
