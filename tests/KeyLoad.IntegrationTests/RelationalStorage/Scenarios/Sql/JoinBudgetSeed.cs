using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.RelationalStorage;

internal sealed record JoinBudgetSeed(PartitionRef Partition, ImmutableArray<EntityRef> Sources,
    ImmutableArray<EntityRef> StoredSources, long CommitPosition, string ExpectedOrderId, string ExpectedCustomerId, string ExpectedDetail, string ExpectedName);
