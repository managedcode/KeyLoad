namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed record MigratedAtomicDomainState(CommandRequest Refused,
    AtomicPartitionPlacementResolution Fallback);
