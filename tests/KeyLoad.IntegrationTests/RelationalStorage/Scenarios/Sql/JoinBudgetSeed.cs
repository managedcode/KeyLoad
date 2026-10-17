using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record JoinBudgetSeed(PartitionRef Partition, ImmutableArray<EntityRef> Sources,
    long CommitPosition, string ExpectedOrderId, string ExpectedCustomerId, string ExpectedDetail, string ExpectedName);
