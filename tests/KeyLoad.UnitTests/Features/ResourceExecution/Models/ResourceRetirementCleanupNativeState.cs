namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed record ResourceRetirementCleanupNativeState(ResourceLifetimeV1 Lifetime,
    PartitionRef[] Partitions, Dictionary<string, string> Original, HashSet<string> Owned);
